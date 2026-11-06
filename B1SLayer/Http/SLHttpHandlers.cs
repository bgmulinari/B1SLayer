using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
#if NETSTANDARD2_0
using System.Reflection;
#endif

namespace B1SLayer;

/// <summary>
///     Creates and configures the handlers of <see cref="SLConnection" /> and <see cref="SLOidcClient" />.
/// </summary>
internal static class SLHttpHandlers
{
    /// <summary>
    ///     Creates a handler with the settings B1SLayer relies on and the given certificate validation.
    /// </summary>
    public static HttpClientHandler Create(Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> validationCallback, bool validateServerCertificate)
    {
        var handler = new HttpClientHandler();

        try
        {
            handler.UseCookies = false;
        }
        catch (PlatformNotSupportedException)
        {
            // Not configurable on some platforms (e.g. Blazor WebAssembly)
        }

        if (handler.SupportsRedirectConfiguration)
        {
            // Redirects are followed by B1SLayer itself (see SendWithRedirectsAsync) rather than by the
            // handler, so Set-Cookie headers on intermediate hops (e.g. a load balancer's affinity
            // cookie) are captured and the applicable cookies re-evaluated against each hop's URL
            handler.AllowAutoRedirect = false;
        }

        if (handler.SupportsAutomaticDecompression)
        {
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        }

        if (validationCallback != null)
        {
            // A custom validation callback (e.g. certificate pinning) takes precedence over the validation toggle.
            // Deliberately not guarded: an explicitly requested callback that can not be applied
            // (e.g. on Blazor WebAssembly) must fail loudly rather than silently validate differently
            handler.ServerCertificateCustomValidationCallback = validationCallback;
        }
        else if (!validateServerCertificate)
        {
            try
            {
                handler.ServerCertificateCustomValidationCallback = (a, b, c, d) => true;
            }
            catch (PlatformNotSupportedException)
            {
                // Not configurable on some platforms (e.g. Blazor WebAssembly), where certificate
                // validation is handled by the platform itself
            }
        }

        return handler;
    }

    /// <summary>
    ///     Disables cookie management on the innermost handler of the provided handler (chain).
    ///     Session cookies are managed by B1SLayer and sent through the Cookie header,
    ///     which handler-level cookie management would silently discard or interfere with.
    /// </summary>
    public static void DisableCookieManagement(HttpMessageHandler handler)
    {
        handler = GetInnermostHandler(handler);

        switch (handler)
        {
            case HttpClientHandler httpClientHandler:
                TryConfigure(() => httpClientHandler.UseCookies = false);
                break;
#if NETSTANDARD2_0
            default:
                TryConfigure(() => GetSocketsHandlerSetting(handler, "UseCookies")?.SetValue(handler, false));
                break;
#else
            case SocketsHttpHandler socketsHttpHandler:
                TryConfigure(() => socketsHttpHandler.UseCookies = false);
                break;
#endif
        }
    }

    /// <summary>
    ///     Disables automatic redirects on the innermost handler of the provided handler (chain), leaving them to B1SLayer.
    /// </summary>
    public static void DisableAutomaticRedirects(HttpMessageHandler handler)
    {
        handler = GetInnermostHandler(handler);

        switch (handler)
        {
            case HttpClientHandler httpClientHandler:
                TryConfigure(() => httpClientHandler.AllowAutoRedirect = false);
                break;
#if NETSTANDARD2_0
            default:
                TryConfigure(() => GetSocketsHandlerSetting(handler, "AllowAutoRedirect")?.SetValue(handler, false));
                break;
#else
            case SocketsHttpHandler socketsHttpHandler:
                TryConfigure(() => socketsHttpHandler.AllowAutoRedirect = false);
                break;
#endif
        }
    }

    /// <summary>
    ///     Determines whether the innermost handler of the provided handler (chain) stores and sends cookies of its own.
    /// </summary>
    public static bool IsCookieManagementEnabled(HttpMessageHandler handler)
    {
        handler = GetInnermostHandler(handler);

        return IsEnabled(() => handler switch
        {
            HttpClientHandler httpClientHandler => httpClientHandler.UseCookies,
#if NETSTANDARD2_0
            _ => GetSocketsHandlerSetting(handler, "UseCookies")?.GetValue(handler) is true
#else
            SocketsHttpHandler socketsHttpHandler => socketsHttpHandler.UseCookies,
            _ => false
#endif
        });
    }

    /// <summary>
    ///     Determines whether the innermost handler of the provided handler (chain) follows redirects by itself.
    /// </summary>
    public static bool IsAutomaticRedirectEnabled(HttpMessageHandler handler)
    {
        handler = GetInnermostHandler(handler);

        return IsEnabled(() => handler switch
        {
            HttpClientHandler httpClientHandler => httpClientHandler.AllowAutoRedirect,
#if NETSTANDARD2_0
            _ => GetSocketsHandlerSetting(handler, "AllowAutoRedirect")?.GetValue(handler) is true
#else
            SocketsHttpHandler socketsHttpHandler => socketsHttpHandler.AllowAutoRedirect,
            _ => false
#endif
        });
    }

    private static HttpMessageHandler GetInnermostHandler(HttpMessageHandler handler)
    {
        while (handler is DelegatingHandler delegatingHandler && delegatingHandler.InnerHandler != null) handler = delegatingHandler.InnerHandler;

        return handler;
    }

    private static void TryConfigure(Action configure)
    {
        try
        {
            configure();
        }
        catch (Exception ex) when (IsConfigurationRefused(ex))
        {
            // Not configurable on some platforms (e.g. Blazor WebAssembly), or the handler has already served requests,
            // in which case it is left untouched, as the setting may have been disabled beforehand by the caller
        }
    }

    private static bool IsEnabled(Func<bool> getSetting)
    {
        try
        {
            return getSetting();
        }
        catch (PlatformNotSupportedException)
        {
            // Not configurable on some platforms (e.g. Blazor WebAssembly), which is handled per request
            return false;
        }
    }

    private static bool IsConfigurationRefused(Exception exception) =>
        exception is PlatformNotSupportedException or InvalidOperationException
#if NETSTANDARD2_0
        || exception is TargetInvocationException { InnerException: PlatformNotSupportedException or InvalidOperationException }
#endif
    ;

#if NETSTANDARD2_0
    /// <summary>
    ///     Gets a setting of a SocketsHttpHandler, or null for any other handler. This target can't reference
    ///     that type, but an application on .NET 6 or 7 runs this target and can still provide one.
    /// </summary>
    private static PropertyInfo GetSocketsHandlerSetting(HttpMessageHandler handler, string name) =>
        handler.GetType().FullName == "System.Net.Http.SocketsHttpHandler" ? handler.GetType().GetProperty(name) : null;
#endif
}
