using System;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer;

/// <summary>
///     A thread-safe <see cref="ISLAccessTokenSource" /> that caches the token obtained from a delegate and renews it
///     shortly before it expires, with concurrent callers sharing a single acquisition (single-flight).
/// </summary>
/// <remarks>
///     <para>
///         <see cref="SLOidcClient.CreateClientCredentialsTokenSource" /> and
///         <see cref="SLOidcClient.CreateRefreshTokenSource" />
///         return instances of this class. Create one directly to cache tokens from an application's own OIDC library.
///         Create one instance per user, as an instance holds a single token.
///     </para>
///     <para>
///         The acquisition isn't tied to any one caller, whose cancellation only stops that caller from waiting. It is
///         limited
///         to 100 seconds, after which it no longer holds up the next one, even when its delegate ignores the token and
///         keeps
///         running. When it fails, every caller waiting for it gets the failure, and the next call tries again.
///     </para>
///     <para>
///         An early renewal runs in the background while callers keep getting the current token, so an unresponsive server
///         doesn't delay them. When it fails, it is tried again no sooner than 10 seconds later, until the token expires.
///     </para>
///     <para>
///         A token acquired by this source and rejected within 30 seconds isn't replaced: it is returned again, so the
///         rejection
///         surfaces instead of spending a token request on each failed request when the cause is not the token, such as a
///         missing audience.
///     </para>
/// </remarks>
public sealed class SLCachingAccessTokenSource : ISLAccessTokenSource
{
    private static readonly TimeSpan DefaultExpirySkew = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EarlyRenewalRetryInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RejectedTokenRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly Func<CancellationToken, Task<SLTokenResponse>> _acquireToken;
    private readonly TimeSpan _expirySkew;
    private readonly object _invalidationGate = new();
    private readonly Func<SLTokenResponse, CancellationToken, Task> _onAcquired;
    private readonly SingleFlight<CachedToken> _singleFlight = new();
    private readonly Func<DateTimeOffset> _utcNow;
    private volatile CachedToken _current;
    private long _earlyRenewalNotBeforeTicks;
    private int _failedGeneration;
    private int _generation;
    private SLAuthenticationException _persistenceFailure;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SLCachingAccessTokenSource" /> class.
    /// </summary>
    /// <param name="acquireToken">
    ///     The delegate that acquires a new token. Called one at a time, unless a call ignores its token past the 100 second
    ///     limit.
    /// </param>
    /// <param name="expirySkew">
    ///     How long before its expiry a token is renewed, 60 seconds by default, so it doesn't expire on its way to the
    ///     server. Capped at half the lifetime of the token, so a short-lived token isn't renewed on every request.
    /// </param>
    public SLCachingAccessTokenSource(Func<CancellationToken, Task<SLTokenResponse>> acquireToken, TimeSpan? expirySkew = null)
        : this(acquireToken, expirySkew, null, null)
    {
    }

    /// <param name="acquireToken">
    ///     The delegate that acquires a new token.
    /// </param>
    /// <param name="expirySkew">
    ///     How long before its expiry a token is renewed.
    /// </param>
    /// <param name="initialToken">
    ///     A token obtained elsewhere to start with, which is always replaced when it is rejected, as its real age is unknown.
    /// </param>
    /// <param name="utcNow">
    ///     Gets the current time.
    /// </param>
    /// <param name="onAcquired">
    ///     Called with each acquired token once it has been published. Its failure reaches the next caller, once.
    /// </param>
    internal SLCachingAccessTokenSource(
        Func<CancellationToken, Task<SLTokenResponse>> acquireToken,
        TimeSpan? expirySkew,
        SLTokenResponse initialToken,
        Func<DateTimeOffset> utcNow,
        Func<SLTokenResponse, CancellationToken, Task> onAcquired = null)
    {
        _acquireToken = acquireToken ?? throw new ArgumentNullException(nameof(acquireToken));
        _expirySkew = expirySkew ?? DefaultExpirySkew;
        _utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
        _onAcquired = onAcquired;

        if (_expirySkew < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expirySkew));
        }

        // A malformed one would fail every request before it could be rejected and replaced
        if (initialToken != null && SLRequestCredential.IsValidBearerToken(initialToken.AccessToken))
        {
            _current = CreateCachedToken(initialToken, _utcNow(), false, 0);
        }
    }

    /// <summary>
    ///     Gets the limit of an acquisition. Replaceable for testing.
    /// </summary>
    internal TimeSpan AcquireTimeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    ///     Gets the cached token while it is valid, or acquires a new one.
    /// </summary>
    /// <param name="request">
    ///     Describes why the token is being requested. A forced refresh doesn't acquire a new token when
    ///     <see cref="SLAccessTokenRequest.RejectedAccessToken" /> is no longer the cached one, as another caller has already
    ///     refreshed it,
    ///     or when the cached one was acquired less than 30 seconds ago, in which case it is returned again.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to stop waiting for the token.
    /// </param>
    /// <returns>
    ///     The raw access token.
    /// </returns>
    public async Task<string> GetAccessTokenAsync(SLAccessTokenRequest request, CancellationToken cancellationToken = default)
    {
        request ??= SLAccessTokenRequest.Default;
        var generation = Volatile.Read(ref _generation);

        ThrowPendingPersistenceFailure();

        var current = _current;

        if (current != null && IsUsable(current, request))
        {
            return current.Response.AccessToken;
        }

        if (!request.ForceRefresh && current != null && IsUnexpired(current))
        {
            StartEarlyRenewal();
            return current.Response.AccessToken;
        }

        // Checked again where no acquisition can be in flight, as another caller may have completed one since the check above.
        // A forced refresh that went ahead regardless would spend a rotating refresh token for nothing
        CachedToken acquired = null;

        // The acquisition in flight may have started before an invalidation this caller already
        // saw, in which case neither its token nor its failure is meant for this caller
        do
        {
            try
            {
                acquired = await _singleFlight.RunAsync(
                        AcquireAsync,
                        cancellationToken,
                        () => _current is { } latest && IsUsable(latest, request) ? latest : null)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested && Volatile.Read(ref _failedGeneration) < generation)
            {
                acquired = null;
            }
        } while (acquired == null || acquired.Generation < generation);

        ThrowPendingPersistenceFailure();
        return acquired.Response.AccessToken;
    }

    /// <summary>
    ///     Discards the cached token, so the next call acquires a new one. That includes the token of an acquisition already
    ///     in flight.
    /// </summary>
    public void Invalidate()
    {
        lock (_invalidationGate)
        {
            _generation++;
            _current = null;
        }
    }

    /// <summary>
    ///     Returns a description of this instance, which never includes a token.
    /// </summary>
    public override string ToString() => nameof(SLCachingAccessTokenSource);

    private bool IsUsable(CachedToken cachedToken, SLAccessTokenRequest request)
    {
        if (cachedToken.RenewAtUtc is { } renewAtUtc && _utcNow() >= renewAtUtc)
        {
            return false;
        }

        if (!request.ForceRefresh)
        {
            return true;
        }

        if (request.RejectedAccessToken == null)
        {
            return false;
        }

        // Another caller has already replaced the rejected token
        if (!string.Equals(request.RejectedAccessToken, cachedToken.Response.AccessToken, StringComparison.Ordinal))
        {
            return true;
        }

        // A token this fresh wasn't rejected for its age, so a new one would most likely share its fate. Returning it again
        // surfaces the rejection, which keeps a persistent cause from turning every failed request into a token request
        return cachedToken.AcquiredAtUtc is { } acquiredAtUtc && _utcNow() - acquiredAtUtc < RejectedTokenRefreshInterval;
    }

    private bool IsUnexpired(CachedToken cachedToken) => cachedToken.Response.ExpiresAtUtc is { } expiresAtUtc && _utcNow() < expiresAtUtc;

    private void StartEarlyRenewal()
    {
        if (_utcNow().UtcTicks >= Volatile.Read(ref _earlyRenewalNotBeforeTicks))
        {
            _singleFlight.Start(AcquireAsync, () => _current is { } latest && IsUsable(latest, SLAccessTokenRequest.Default) ? latest : null);
        }
    }

    private void ThrowPendingPersistenceFailure()
    {
        if (Volatile.Read(ref _persistenceFailure) != null && Interlocked.Exchange(ref _persistenceFailure, null) is { } failure)
        {
            throw failure;
        }
    }

    private async Task<CachedToken> AcquireAsync()
    {
        var acquiredAtUtc = _utcNow();
        var generation = Volatile.Read(ref _generation);
        using var timeout = new CancellationTokenSource(AcquireTimeout);
        SLTokenResponse response;

        try
        {
            // Waited for separately, as a delegate ignoring its token would otherwise hold up every later acquisition for good
            response = await _acquireToken(timeout.Token).WithCancellation(timeout.Token).ConfigureAwait(false)
                       ?? throw new SLAuthenticationException("The token acquisition didn't return a token.");

            // Cached, it would fail every request before one could be rejected and replaced. The value stays out of the message, as it may well be a token
            if (!SLRequestCredential.IsValidBearerToken(response.AccessToken))
            {
                throw new SLAuthenticationException("The token acquisition returned a malformed access token.");
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _earlyRenewalNotBeforeTicks, (_utcNow() + EarlyRenewalRetryInterval).UtcTicks);
            Volatile.Write(ref _failedGeneration, generation);

            if (ex is OperationCanceledException && timeout.IsCancellationRequested)
            {
                throw new SLAuthenticationException("Acquiring the access token timed out.", ex);
            }

            throw;
        }

        // Published before persisting: a rotated refresh token has already replaced the previous one, and
        // going back to the old tokens would repeat the refresh on each request while persisting keeps failing
        var cachedToken = CreateCachedToken(response, acquiredAtUtc, true, generation);

        lock (_invalidationGate)
        {
            if (generation == _generation)
            {
                _current = cachedToken;
            }
        }

        if (_onAcquired != null)
        {
            try
            {
                await _onAcquired(response, timeout.Token).WithCancellation(timeout.Token).ConfigureAwait(false);
                Volatile.Write(ref _persistenceFailure, null);
            }
            catch (Exception ex)
            {
                // Held for the next caller, as a background renewal has no caller to report to
                Volatile.Write(ref _persistenceFailure, new SLAuthenticationException("New tokens were issued and are in use, but persisting them failed, so the stored tokens may be out of date. See the inner exception for details.", ex));
            }
        }

        return cachedToken;
    }

    private CachedToken CreateCachedToken(SLTokenResponse response, DateTimeOffset acquiredAtUtc, bool isAcquired, int generation)
    {
        DateTimeOffset? renewAtUtc = null;

        if (response.ExpiresAtUtc is { } expiresAtUtc)
        {
            var halfLifetime = TimeSpan.FromTicks(Math.Max(0, (expiresAtUtc - acquiredAtUtc).Ticks / 2));
            renewAtUtc = expiresAtUtc - (_expirySkew < halfLifetime ? _expirySkew : halfLifetime);
        }

        return new CachedToken(response, renewAtUtc, isAcquired ? acquiredAtUtc : null, generation);
    }

    private sealed class CachedToken(
        SLTokenResponse response,
        DateTimeOffset? renewAtUtc,
        DateTimeOffset? acquiredAtUtc,
        int generation)
    {
        public SLTokenResponse Response { get; } = response;
        public DateTimeOffset? RenewAtUtc { get; } = renewAtUtc;
        public DateTimeOffset? AcquiredAtUtc { get; } = acquiredAtUtc;
        public int Generation { get; } = generation;
    }
}
