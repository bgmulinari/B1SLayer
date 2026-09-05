using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Distributed;

namespace B1SLayer;

public partial class SLConnection
{
    private readonly Func<string, string> _getServiceLayerConnectionContext;
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private readonly int _ssoSessionTimeout;
    private SLLoginResponse _loginResponse;
    private SessionCookieCacheEntry _sessionCookieCache;

    /// <summary>
    ///     Performs a POST Login request with the provided information, regardless of the current session state.
    /// </summary>
    /// <remarks>
    ///     Manually performing the Login is often unnecessary because it will be performed automatically anyway whenever needed.
    /// </remarks>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<SLLoginResponse> LoginAsync(CancellationToken cancellationToken = default) => ExecuteLoginAsync(true, cancellationToken: cancellationToken);

    /// <summary>
    ///     Performs the POST Login request to the Service Layer.
    /// </summary>
    /// <param name="expectReturn">
    ///     Whether the login information should be returned.
    /// </param>
    /// <param name="skipIfSessionRefreshed">
    ///     Whether the login should be skipped when the cached session no longer matches
    ///     <paramref name="staleSessionValue" />, meaning another caller has already refreshed it.
    /// </param>
    /// <param name="staleSessionValue">
    ///     The serialized session cookies the caller deemed invalid, or null when the caller found no session.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    private async Task<SLLoginResponse> ExecuteLoginAsync(bool expectReturn = false, bool skipIfSessionRefreshed = false,
        string staleSessionValue = null, CancellationToken cancellationToken = default)
    {
        // Prevents multiple login requests in a multi-threaded scenario
        await _semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (skipIfSessionRefreshed)
            {
                // Another caller may have already refreshed the session while this one awaited the
                // semaphore; logging in again would needlessly create (and orphan) yet another session
                var cachedValue = await DistributedCache.GetStringAsync(SessionCacheKey, cancellationToken).ConfigureAwait(false);

                if (!string.Equals(cachedValue, staleSessionValue, StringComparison.Ordinal) && DeserializeSessionCookies(cachedValue) != null)
                {
                    return expectReturn ? LoginResponse : null;
                }
            }

            if (!IsUsingSingleSignOn)
            {
                // The Login exchange has a fixed wire format, so it deliberately bypasses the user-configurable serializer options
                var loginBody = JsonSerializer.SerializeToUtf8Bytes(new { CompanyDB, UserName, Password, Language }, ProtocolSerializerOptions);
                using var requestMessage = CreateResourceRequestMessage(HttpMethod.Post, "Login", content: HttpExtensions.CreateJsonContent(loginBody));

                // The RequestBody helper is deliberately not populated for the call event handlers so the
                // password doesn't end up in logs through it (the raw request message remains accessible)
                var callContext = await SendCoreAsync(requestMessage, null, null, HttpCompletionOption.ResponseContentRead, null, null, 1, cancellationToken).ConfigureAwait(false);
                using var response = callContext.ResponseMessage;
                var responseContent = await response.ReadStringAsync(cancellationToken).ConfigureAwait(false);

                SLLoginResponse parsedLoginResponse;

                try
                {
                    parsedLoginResponse = JsonSerializer.Deserialize<SLLoginResponse>(responseContent, ProtocolSerializerOptions);
                }
                catch (JsonException jsonException)
                {
                    // A successful status with an unparseable body (e.g. an HTML page served by an intermediary)
                    // surfaces as the library's typed exception, carrying the status and body for diagnosis
                    throw new SLException($"The login response could not be parsed. Status code {(int)response.StatusCode} ({response.StatusCode}).",
                        null,
                        response.StatusCode,
                        responseContent,
                        jsonException);
                }

                _loginResponse = parsedLoginResponse ?? new SLLoginResponse();
                _loginResponse.LastLogin = DateTime.Now;

                // The captured cookies cover every response in the login's redirect chain, so cookies
                // issued on intermediate hops (e.g. a load balancer's affinity cookie) are preserved
                await SetSessionCookiesAsync(callContext.ResponseCookies ?? new SLCookieJar(), TimeSpan.FromMinutes(_loginResponse.SessionTimeout), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Obtains session context from UI API method
                var connectionContext = _getServiceLayerConnectionContext(ServiceLayerRoot.ToString());
                var cookies = CreateCookieJarFromConnectionContext(connectionContext);
                await SetSessionCookiesAsync(cookies, TimeSpan.FromMinutes(_ssoSessionTimeout), cancellationToken).ConfigureAwait(false);
                _loginResponse.LastLogin = DateTime.Now;
                _loginResponse.SessionTimeout = _ssoSessionTimeout;
                _loginResponse.SessionId = cookies.Find("B1SESSION")?.Value;
            }

            return expectReturn ? LoginResponse : null;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    /// <summary>
    ///     Obtains the current session cookies from the distribued cache.
    ///     A login is performed if no valid cookies are retrieved from cache.
    /// </summary>
    /// <returns>
    ///     The current session cookies to be used in each request.
    /// </returns>
    internal async Task<SLCookieJar> GetSessionCookiesAsync(CancellationToken cancellationToken = default)
    {
        var cookiesString = await DistributedCache.GetStringAsync(SessionCacheKey, cancellationToken).ConfigureAwait(false);
        var cookieJar = DeserializeSessionCookies(cookiesString);

        if (cookieJar != null)
        {
            await DistributedCache.RefreshAsync(SessionCacheKey, cancellationToken).ConfigureAwait(false);
            return cookieJar;
        }

        if (!string.IsNullOrEmpty(cookiesString))
        {
            // The cached value can't be parsed (e.g. it was created by a previous B1SLayer version),
            // so it is discarded and a fresh login is performed
            await DistributedCache.RemoveAsync(SessionCacheKey, cancellationToken).ConfigureAwait(false);
        }

        await ExecuteLoginAsync(skipIfSessionRefreshed: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        cookiesString = await DistributedCache.GetStringAsync(SessionCacheKey, cancellationToken).ConfigureAwait(false);
        return DeserializeSessionCookies(cookiesString);
    }

    /// <summary>
    ///     Gets the serialized form of the given session cookies, reusing the memoized cache value when possible.
    /// </summary>
    private string GetSerializedSessionValue(SLCookieJar sessionCookies)
    {
        var cachedEntry = _sessionCookieCache;

        return cachedEntry != null && ReferenceEquals(cachedEntry.Jar, sessionCookies)
            ? cachedEntry.SerializedValue
            : sessionCookies?.Serialize();
    }

    /// <summary>
    ///     Deserializes the cached session cookie value, memoizing the parsed jar so the same
    ///     value isn't reparsed on every request while the session remains active.
    /// </summary>
    private SLCookieJar DeserializeSessionCookies(string cookiesString)
    {
        if (string.IsNullOrEmpty(cookiesString))
        {
            return null;
        }

        var cachedEntry = _sessionCookieCache;

        if (cachedEntry != null && string.Equals(cachedEntry.SerializedValue, cookiesString, StringComparison.Ordinal))
        {
            return cachedEntry.Jar;
        }

        var cookieJar = SLCookieJar.Deserialize(cookiesString);

        if (cookieJar != null)
        {
            _sessionCookieCache = new SessionCookieCacheEntry { SerializedValue = cookiesString, Jar = cookieJar };
        }

        return cookieJar;
    }

    /// <summary>
    ///     Sets the given session cookies to the distributed cache.
    /// </summary>
    /// <param name="cookies">
    ///     The cookies to be set to the distributed cache.
    /// </param>
    /// <param name="slidingExpiration">
    ///     The sliding expiration time for the session cache.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    private async Task SetSessionCookiesAsync(SLCookieJar cookies, TimeSpan slidingExpiration, CancellationToken cancellationToken = default)
    {
        var cookieString = cookies?.Serialize();

        if (!string.IsNullOrEmpty(cookieString))
        {
            await DistributedCache.SetStringAsync(SessionCacheKey,
                    cookieString,
                    new DistributedCacheEntryOptions
                    {
                        SlidingExpiration = slidingExpiration
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Removes the current active session from the distributed cache.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task InvalidateSessionCacheAsync(CancellationToken cancellationToken = default) => DistributedCache.RemoveAsync(SessionCacheKey, cancellationToken);

    /// <summary>
    ///     Creates a <see cref="SLCookieJar" /> instance from the context string obtained from the UI API method "GetServiceLayerConnectionContext";
    /// </summary>
    /// <param name="connectionContext">
    ///     The connection context string obtained from UI API.
    /// </param>
    /// <returns>
    ///     A <see cref="SLCookieJar" /> instance containing session cookies obtained after a successful authentication.
    /// </returns>
    private SLCookieJar CreateCookieJarFromConnectionContext(string connectionContext)
    {
        var cookieJar = new SLCookieJar();

        // Mirrors the scoping of cookies obtained through a regular login: session cookies are
        // scoped to the Service Layer root path and host, ROUTEID to the whole server
        var rootPath = ServiceLayerRoot.AbsolutePath.TrimEnd('/');
        rootPath = rootPath.Length == 0 ? "/" : rootPath;

        // Attribute segments (e.g. "path=/b1s/v1") are naturally ignored by the cookie name
        // whitelist below, so no substring filtering is needed (a substring check would also
        // drop cookies whose value merely contains the attribute name)
        var cookies = connectionContext.Replace(',', ';')
            .Split(';')
            .Where(x => !string.IsNullOrEmpty(x) && x.Contains('='));

        foreach (var cookie in cookies)
        {
            var cookieKeyValue = cookie.Split('=');

            if (cookieKeyValue.Length != 2)
            {
                continue;
            }

            if (cookieKeyValue[0].Equals("B1SESSION", StringComparison.OrdinalIgnoreCase) || cookieKeyValue[0].Equals("CompanyDB", StringComparison.OrdinalIgnoreCase))
            {
                cookieJar.AddOrReplace(new SLCookie
                {
                    Name = cookieKeyValue[0],
                    Value = cookieKeyValue[1],
                    Path = rootPath,
                    OriginHost = ServiceLayerRoot.Host,
                    HttpOnly = true,
                    Secure = true
                });
            }
            else if (cookieKeyValue[0].Equals("ROUTEID", StringComparison.OrdinalIgnoreCase))
            {
                cookieJar.AddOrReplace(new SLCookie
                {
                    Name = cookieKeyValue[0],
                    Value = cookieKeyValue[1],
                    Path = "/",
                    OriginHost = ServiceLayerRoot.Host,
                    Secure = true
                });
            }
        }

        return cookieJar;
    }

    /// <summary>
    ///     Performs a POST Logout request, ending the current session.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var currentSessionCookies = await GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false);

        if (currentSessionCookies == null)
        {
            return;
        }

        using var requestMessage = CreateResourceRequestMessage(HttpMethod.Post, "Logout", currentSessionCookies);
        var callContext = await SendCoreAsync(requestMessage, currentSessionCookies, null, HttpCompletionOption.ResponseContentRead, null, null, 1, cancellationToken).ConfigureAwait(false);
        callContext.ResponseMessage.Dispose();

        await InvalidateSessionCacheAsync(cancellationToken).ConfigureAwait(false);
        _loginResponse = new SLLoginResponse();
    }

    /// <summary>
    ///     Memoizes the last parsed session cookies so the cached value isn't reparsed on every request.
    /// </summary>
    private sealed class SessionCookieCacheEntry
    {
        public string SerializedValue { get; set; }
        public SLCookieJar Jar { get; set; }
    }
}
