using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer;

public partial class SLConnection
{
    /// <summary>
    ///     The header carrying the SLD company ID of a token request.
    /// </summary>
    private const string CompanyIdHeader = "X-b1-companyid";

    private const string AuthorizationHeader = "Authorization";
    private const string CookieHeader = "Cookie";

    private const string SessionRequestWithBearerTokenMessage = "This request has a session cookie and a bearer token in its Authorization header, in which case the Service Layer honors the cookie and ignores the token. "
                                                                + $"To run a request as a user, return its identity from the {nameof(SLConnectionOptions.IdentityResolver)}.";

    /// <summary>
    ///     The option through which the browser handler of Blazor WebAssembly takes the options of its fetch call.
    /// </summary>
    private const string BrowserFetchOptions = "WebAssemblyFetchOptions";

    /// <summary>
    ///     The client for requests sent with an access token, which has its own connection pool as certificate
    ///     validation only happens on new TLS connections, or null when this connection doesn't use access tokens.
    /// </summary>
    private readonly HttpClient _accessTokenHttpClient;

    private readonly Func<CancellationToken, Task<SLRequestIdentity>> _identityResolver;

    /// <summary>
    ///     Gets the identity a request runs with. Connections without access tokens always use the session.
    /// </summary>
    private async Task<SLRequestIdentity> ResolveIdentityAsync(TimeSpan? requestTimeout, CancellationToken cancellationToken)
    {
        if (_identityResolver == null)
        {
            return SLRequestIdentity.Session;
        }

        // A null identity fails closed: reading it as "use the session" would silently run the
        // request as the session account when, say, the ambient user failed to be determined
        return await RunWithRequestTimeoutAsync(token => _identityResolver(token) ?? Task.FromResult<SLRequestIdentity>(null), "Resolving the request identity", requestTimeout, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException($"The {nameof(SLConnectionOptions.IdentityResolver)} returned null. Return {nameof(SLRequestIdentity)}.{nameof(SLRequestIdentity.Session)} to explicitly use the session.");
    }

    /// <summary>
    ///     Bounds a phase before the send by the request timeout, even when the delegate running it ignores its token. The
    ///     work itself may carry on, which a token acquisition depends on, as it must not lose a rotated refresh token.
    /// </summary>
    private async Task<T> RunWithRequestTimeoutAsync<T>(Func<CancellationToken, Task<T>> phase, string description, TimeSpan? requestTimeout, CancellationToken cancellationToken)
    {
        var effectiveTimeout = requestTimeout ?? DefaultRequestTimeout;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(effectiveTimeout);

        try
        {
            return await phase(timeout.Token).WithCancellation(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new SLTimeoutException($"{description} timed out after {effectiveTimeout}.", ex);
        }
    }

    /// <summary>
    ///     Gets a validated access token for the given identity. Any failure surfaces as an exception, with no retry and no
    ///     login fallback.
    /// </summary>
    private static async Task<string> AcquireAccessTokenAsync(SLRequestIdentity identity, string rejectedAccessToken, CancellationToken cancellationToken)
    {
        var tokenRequest = rejectedAccessToken == null ? SLAccessTokenRequest.Default : new SLAccessTokenRequest(true, rejectedAccessToken);
        string accessToken;

        try
        {
            accessToken = await identity.TokenSource.GetAccessTokenAsync(tokenRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SLAuthenticationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SLAuthenticationException("The access token source failed. See the inner exception for details.", ex);
        }

        // The value is deliberately not part of the message, as it may well be a token with a formatting problem
        return SLRequestCredential.IsValidBearerToken(accessToken)
            ? accessToken
            : throw new SLAuthenticationException("The access token source returned an empty or malformed access token.");
    }

    /// <summary>
    ///     Fails when the connection has no session account to run a request or a login with.
    /// </summary>
    private void EnsureSessionCredentials()
    {
        if (!HasSessionCredentials)
        {
            throw new InvalidOperationException("This connection authenticates with access tokens and has no session credentials, so it can't log in, log out or run a request with the session identity. "
                                                + $"Sessions require {nameof(SLConnectionOptions.CompanyDB)}, {nameof(SLConnectionOptions.UserName)} and {nameof(SLConnectionOptions.Password)} (or {nameof(SLConnectionOptions.GetServiceLayerConnectionContext)}). "
                                                + "To sign a user out of an access token, use the end session endpoint of the OpenID Connect provider.");
        }
    }

    /// <summary>
    ///     The one place every send passes through, right before going on the wire and on every redirect hop. On a connection
    ///     using access tokens, it enforces the rule that a request never carries both a session cookie and a bearer token,
    ///     because
    ///     the Service Layer honors the cookie and ignores the token, even an invalid one, so the request would silently run
    ///     as the
    ///     cookie's account. A connection without access tokens has no identities to mix up, so its headers are left to the
    ///     caller.
    /// </summary>
    /// <returns>
    ///     The client to send the request with.
    /// </returns>
    private HttpClient PrepareForSend(HttpRequestMessage requestMessage, SLRequestCredential credential)
    {
        var headers = requestMessage.Headers;

        if (credential.IsToken)
        {
            // Applied here, after the BeforeCall handlers, so whatever a caller or handler has set can't override
            // the identity. This also drops every cookie, ROUTEID included, and keeps the token out of the handlers' reach
            headers.Remove(CookieHeader);
            headers.Remove(AuthorizationHeader);
            headers.Remove(CompanyIdHeader);
            headers.TryAddWithoutValidation(AuthorizationHeader, "Bearer " + credential.AccessToken);
            headers.TryAddWithoutValidation(CompanyIdHeader, credential.Identity.CompanyId);
            OmitBrowserCredentials(requestMessage);
        }

        var httpClient = credential.IsToken ? _accessTokenHttpClient : HttpClient;
        var defaultHeaders = httpClient.DefaultRequestHeaders;

        // HttpClient only fills in the default headers missing from the message, and those can't be removed per request.
        // A cookie and a bearer token that remain at this point can't be told apart from a programming error, so the
        // request fails instead of running as an account other than the one intended, which also covers session requests
        var hasCookie = headers.Contains(CookieHeader) || defaultHeaders.Contains(CookieHeader);
        var hasBearerToken = headers.Contains(AuthorizationHeader) ? HasBearerToken(headers) : HasBearerToken(defaultHeaders);

        if (IsUsingAccessTokens && hasCookie && hasBearerToken)
        {
            throw new InvalidOperationException(credential.IsToken
                ? $"The {nameof(HttpClient)}.{nameof(HttpClient.DefaultRequestHeaders)} of this connection set a Cookie header, which would override the access token. Remove it to send access tokens."
                : SessionRequestWithBearerTokenMessage);
        }

        return httpClient;
    }

    /// <summary>
    ///     Keeps a browser from adding a cookie of its own to a token request: Blazor WebAssembly has no
    ///     handler-level cookie setting, and fetch sends same-origin cookies by default, out of reach of the headers.
    /// </summary>
    private static void OmitBrowserCredentials(HttpRequestMessage requestMessage)
    {
#if NETSTANDARD2_0
        // Blazor WebAssembly on .NET 6 and 7 runs this target, where the options are reached through the store's older name
        var options = requestMessage.Properties;
#else
        IDictionary<string, object> options = requestMessage.Options;
#endif

        if (!options.TryGetValue(BrowserFetchOptions, out var value) || value is not IDictionary<string, object> fetchOptions)
        {
            fetchOptions = new Dictionary<string, object>(StringComparer.Ordinal);
            options[BrowserFetchOptions] = fetchOptions;
        }

        fetchOptions["credentials"] = "omit";
    }

    /// <summary>
    ///     Removes the access token from a request that has been sent, so it can't leak through the call event handlers
    ///     or the response's RequestMessage, where a logged <see cref="HttpRequestMessage.ToString" /> would include it.
    /// </summary>
    private static void RemoveAccessToken(HttpRequestMessage requestMessage) => requestMessage.Headers.Remove(AuthorizationHeader);

    private static bool HasBearerToken(HttpRequestHeaders headers) =>
        headers.TryGetValues(AuthorizationHeader, out var values)
        && values.Any(x => x.TrimStart().StartsWith("Bearer", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     Determines whether two URIs share the same scheme, host and port, the condition for an access token to follow a
    ///     redirect.
    /// </summary>
    private static bool IsSameOrigin(Uri first, Uri second) =>
        Uri.Compare(first, second, UriComponents.Scheme | UriComponents.Host | UriComponents.StrongPort, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
}
