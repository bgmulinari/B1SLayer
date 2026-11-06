using System;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer;

/// <summary>
///     Supplies the OAuth access token a request is sent with. Implemented by the application (typically on top of
///     its own OIDC library) or obtained from <see cref="SLOidcClient" /> and <see cref="SLCachingAccessTokenSource" />.
/// </summary>
/// <remarks>
///     <para>
///         Called once per send attempt, so implementations should be fast and return a cached token whenever possible.
///         Implementations are called concurrently and must be thread-safe. The call is bounded separately from the send
///         by the request
///         timeout, resulting in a <see cref="SLTimeoutException" />, and implementations should honor the cancellation
///         token.
///     </para>
///     <para>
///         A failure must be reported by throwing: B1SLayer surfaces it to the caller of the request as a
///         <see cref="SLAuthenticationException" /> and never falls back to a session login.
///     </para>
/// </remarks>
public interface ISLAccessTokenSource
{
    /// <summary>
    ///     Gets the access token to send.
    /// </summary>
    /// <param name="request">
    ///     Describes why the token is being requested. When <see cref="SLAccessTokenRequest.ForceRefresh" /> is set,
    ///     the Service Layer has just rejected a token and a cached one must not be returned.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    ///     The raw access token, without the "Bearer " prefix.
    /// </returns>
    Task<string> GetAccessTokenAsync(SLAccessTokenRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
///     Describes a request for an access token, passed to <see cref="ISLAccessTokenSource.GetAccessTokenAsync" />.
/// </summary>
public sealed class SLAccessTokenRequest
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SLAccessTokenRequest" /> class.
    /// </summary>
    /// <param name="forceRefresh">
    ///     Whether a fresh token is required instead of a cached one.
    /// </param>
    /// <param name="rejectedAccessToken">
    ///     The token the Service Layer rejected, when applicable.
    /// </param>
    public SLAccessTokenRequest(bool forceRefresh, string rejectedAccessToken = null)
    {
        ForceRefresh = forceRefresh;
        RejectedAccessToken = rejectedAccessToken;
    }

    /// <summary>
    ///     Gets a request for the current token, which may be served from a cache.
    /// </summary>
    public static SLAccessTokenRequest Default { get; } = new(false);

    /// <summary>
    ///     Gets whether a fresh token is required, because the Service Layer responded with 401 to the previous one.
    /// </summary>
    public bool ForceRefresh { get; }

    /// <summary>
    ///     Gets the token the Service Layer rejected, or null when no token was rejected. When concurrent requests fail
    ///     with the same token, this lets a cache return the token another caller has already refreshed instead of
    ///     refreshing it again. Never included in <see cref="ToString" />.
    /// </summary>
    public string RejectedAccessToken { get; }

    /// <summary>
    ///     Returns a description of this request that never includes the rejected token.
    /// </summary>
    public override string ToString() => $"{nameof(SLAccessTokenRequest)}({nameof(ForceRefresh)}={ForceRefresh})";
}

/// <summary>
///     Creates <see cref="ISLAccessTokenSource" /> instances from delegates.
/// </summary>
public static class SLAccessTokenSource
{
    /// <summary>
    ///     Creates a token source from the given delegate, which is how an application that owns its OIDC flow
    ///     (for example, authorization code with PKCE through a certified OIDC library) plugs in its tokens.
    /// </summary>
    /// <remarks>
    ///     The delegate is called once per send attempt with no caching. Use <see cref="SLCachingAccessTokenSource" />
    ///     when obtaining a token is expensive. A delegate that returns the rejected token again after a 401
    ///     results in the 401 being surfaced, rather than the request being retried with a token already known to be invalid.
    /// </remarks>
    /// <param name="getAccessToken">
    ///     The delegate that returns the raw access token.
    /// </param>
    public static ISLAccessTokenSource FromDelegate(Func<SLAccessTokenRequest, CancellationToken, Task<string>> getAccessToken)
    {
        if (getAccessToken == null)
        {
            throw new ArgumentNullException(nameof(getAccessToken));
        }

        return new DelegateAccessTokenSource(getAccessToken);
    }

    private sealed class DelegateAccessTokenSource(Func<SLAccessTokenRequest, CancellationToken, Task<string>> getAccessToken) : ISLAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(SLAccessTokenRequest request, CancellationToken cancellationToken = default) => getAccessToken(request, cancellationToken);
    }
}
