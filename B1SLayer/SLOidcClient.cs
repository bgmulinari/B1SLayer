using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer;

/// <summary>
///     The non-interactive part of authenticating against the SAP Business One authentication server: discovery through
///     the System Landscape Directory (SLD), the client credentials and refresh token grants, and the company lookup.
/// </summary>
/// <remarks>
///     <para>
///         SAP recommends certified third-party libraries for OpenID Connect. Signing users in (browser redirects,
///         state, nonce, PKCE, the authorization code exchange, ID token validation and logout) is deliberately left out
///         and belongs
///         to such a library. An application doing that plugs its tokens in through <see cref="ISLAccessTokenSource" />.
///     </para>
///     <para>
///         An instance is thread-safe and should be reused. Certificates are validated by default,
///         see <see cref="SLOidcClientOptions.ValidateServerCertificate" />.
///     </para>
/// </remarks>
public sealed class SLOidcClient : IDisposable
{
    private const string WellKnownPath = "/.well-known/openid-configuration";
    private const string SldServicePath = "/sld/sld0100.svc/";

    // An absurd value from the server must not overflow the expiry calculation
    private const double MaxTokenLifetimeSeconds = 10 * 365 * 24 * 3600;

    private static readonly TimeSpan MaxProviderMetadataLifetime = TimeSpan.FromSeconds(MaxTokenLifetimeSeconds);

    // How long previous metadata is used without asking again during an outage, so renewals don't each wait for it
    private static readonly TimeSpan DiscoveryRetryInterval = TimeSpan.FromSeconds(30);
    private readonly bool _allowInsecureHttp;
    private readonly SLClientCredential _clientCredential;

    private readonly string _clientId;
    private readonly HttpClient _httpClient;
    private readonly SingleFlight<CachedMetadata> _metadataFlight = new();
    private readonly bool _ownsHttpClient;
    private readonly TimeSpan _providerMetadataLifetime;
    private readonly TimeSpan _requestTimeout;
    private readonly string _sldAddress;
    private volatile CachedMetadata _metadata;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SLOidcClient" /> class.
    /// </summary>
    /// <param name="options">
    ///     The configuration options for this client.
    /// </param>
    public SLOidcClient(SLOidcClientOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.SldAddress is not { IsAbsoluteUri: true })
        {
            throw new ArgumentException($"{nameof(SLOidcClientOptions.SldAddress)} must be an absolute URI.", nameof(options));
        }

        if (options.HttpClient != null && options.HttpMessageHandler != null)
        {
            throw new ArgumentException($"{nameof(SLOidcClientOptions.HttpClient)} and {nameof(SLOidcClientOptions.HttpMessageHandler)} can not be provided together.", nameof(options));
        }

        if (options.ServerCertificateValidationCallback != null && (options.HttpClient != null || options.HttpMessageHandler != null))
        {
            throw new ArgumentException($"{nameof(SLOidcClientOptions.ServerCertificateValidationCallback)} can not be combined with a provided {nameof(SLOidcClientOptions.HttpClient)} or {nameof(SLOidcClientOptions.HttpMessageHandler)}. Configure certificate validation on the provided instance instead.", nameof(options));
        }

        if (options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > TimeSpan.FromMilliseconds(int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(nameof(options), $"{nameof(SLOidcClientOptions.RequestTimeout)} is out of range.");
        }

        if (options.ProviderMetadataLifetime < TimeSpan.Zero || options.ProviderMetadataLifetime > MaxProviderMetadataLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(options), $"{nameof(SLOidcClientOptions.ProviderMetadataLifetime)} is out of range.");
        }

        _allowInsecureHttp = options.AllowInsecureHttp;

        if (!IsAllowedEndpoint(options.SldAddress))
        {
            throw new ArgumentException($"{nameof(SLOidcClientOptions.SldAddress)} must use HTTPS, as OAuth requires TLS (RFC 6749 section 3.2). To allow it regardless, set {nameof(SLOidcClientOptions.AllowInsecureHttp)}.", nameof(options));
        }

        _sldAddress = options.SldAddress.AbsoluteUri.TrimEnd('/');
        _clientId = options.ClientId;
        _clientCredential = options.ClientCredential;
        _requestTimeout = options.RequestTimeout;
        _providerMetadataLifetime = options.ProviderMetadataLifetime;

        if (options.HttpClient != null)
        {
            _httpClient = options.HttpClient;
        }
        else if (options.HttpMessageHandler != null)
        {
            SLHttpHandlers.DisableCookieManagement(options.HttpMessageHandler);
            _httpClient = new HttpClient(options.HttpMessageHandler, false) { Timeout = Timeout.InfiniteTimeSpan };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = new HttpClient(SLHttpHandlers.Create(options.ServerCertificateValidationCallback, options.ValidateServerCertificate)) { Timeout = Timeout.InfiniteTimeSpan };
            _ownsHttpClient = true;
        }
    }

    /// <summary>
    ///     Gets the current time. Replaceable for testing.
    /// </summary>
    internal Func<DateTimeOffset> UtcNow { get; set; } = static () => DateTimeOffset.UtcNow;

    /// <summary>
    ///     Releases the resources of the <see cref="System.Net.Http.HttpClient" /> created by this
    ///     instance. An instance provided through the options is left untouched.
    /// </summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    /// <summary>
    ///     Discovers the provider metadata through the SLD's GetOpenIDConnectProvider API, validating
    ///     that the issuer matches the discovery URI as required by OpenID Connect Discovery section 4.3.
    /// </summary>
    /// <remarks>
    ///     The metadata is cached for <see cref="SLOidcClientOptions.ProviderMetadataLifetime" />, and keeps being used
    ///     past that when the SLD or the provider is unavailable, asking again every 30 seconds. See
    ///     "GetOpenIDConnectProvider" in SAP's
    ///     "Identity and Authentication Management in SAP Business One".
    /// </remarks>
    /// <param name="cancellationToken">
    ///     A token to stop waiting for the metadata.
    /// </param>
    /// <exception cref="SLAuthenticationException">
    ///     The SLD or the provider failed or returned invalid metadata, such as a mismatching issuer.
    ///     When the server couldn't be reached, the <see cref="Exception.InnerException" /> is the
    ///     <see cref="HttpRequestException" />.
    /// </exception>
    /// <exception cref="SLTimeoutException">
    ///     The SLD or the provider didn't respond within <see cref="SLOidcClientOptions.RequestTimeout" />.
    /// </exception>
    public async Task<SLOidcProviderMetadata> GetProviderMetadataAsync(CancellationToken cancellationToken = default)
    {
        var cached = _metadata;

        if (cached != null && UtcNow() < cached.ExpiresAtUtc)
        {
            return cached.Metadata;
        }

        // Checked again where no discovery can be in flight, as another caller may have completed one since the check above
        var discovered = await _metadataFlight.RunAsync(
                DiscoverOrKeepPreviousAsync,
                cancellationToken,
                () => _metadata is { } latest && UtcNow() < latest.ExpiresAtUtc ? latest : null)
            .ConfigureAwait(false);

        return discovered.Metadata;
    }

    /// <summary>
    ///     Requests an access token through the client credentials grant, the way a daemon service authenticates. It doesn't
    ///     cache
    ///     the token, see <see cref="CreateClientCredentialsTokenSource" /> for that.
    /// </summary>
    /// <param name="scope">
    ///     The scope to request. SAP's documentation uses "openid".
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    /// <exception cref="SLAuthenticationException">
    ///     The token endpoint rejected the request, returned an invalid response or couldn't be reached.
    /// </exception>
    /// <exception cref="SLTimeoutException">
    ///     A server didn't respond within <see cref="SLOidcClientOptions.RequestTimeout" />.
    /// </exception>
    public Task<SLTokenResponse> RequestClientCredentialsTokenAsync(string scope = "openid", CancellationToken cancellationToken = default)
    {
        if (_clientCredential == null)
        {
            throw new InvalidOperationException($"The client credentials grant requires {nameof(SLOidcClientOptions.ClientCredential)}.");
        }

        var form = new List<KeyValuePair<string, string>> { new("grant_type", "client_credentials") };

        if (!string.IsNullOrEmpty(scope))
        {
            form.Add(new KeyValuePair<string, string>("scope", scope));
        }

        return RequestTokenAsync(form, null, cancellationToken);
    }

    /// <summary>
    ///     Requests new tokens through the refresh token grant. It doesn't cache the tokens, see
    ///     <see cref="CreateRefreshTokenSource" /> for that.
    /// </summary>
    /// <remarks>
    ///     The server may rotate the refresh token, in which case the previous one must no longer be used, so
    ///     always continue with <see cref="SLTokenResponse.RefreshToken" /> of the result, whether it is a new one or not.
    /// </remarks>
    /// <param name="refreshToken">
    ///     The current refresh token.
    /// </param>
    /// <param name="scope">
    ///     The scope to request, or null to keep the scope of the refresh token.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    /// <exception cref="SLAuthenticationException">
    ///     The token endpoint rejected the request, returned an invalid response or couldn't be reached. When the refresh
    ///     token
    ///     is no longer valid and the user must sign in again, <see cref="SLAuthenticationException.OAuthError" /> is
    ///     "invalid_grant".
    /// </exception>
    /// <exception cref="SLTimeoutException">
    ///     A server didn't respond within <see cref="SLOidcClientOptions.RequestTimeout" />.
    /// </exception>
    public Task<SLTokenResponse> RefreshTokenAsync(string refreshToken, string scope = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            throw new ArgumentException("The refresh token can not be empty.", nameof(refreshToken));
        }

        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken)
        };

        if (!string.IsNullOrEmpty(scope))
        {
            form.Add(new KeyValuePair<string, string>("scope", scope));
        }

        return RequestTokenAsync(form, refreshToken, cancellationToken);
    }

    /// <summary>
    ///     Gets the user an access token belongs to and the companies the user can access, through the SLD's CurrentUserInfo
    ///     API.
    /// </summary>
    /// <param name="accessToken">
    ///     The access token of the user.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    /// <exception cref="SLAuthenticationException">
    ///     The SLD rejected the token (status code 401), returned an invalid response or couldn't be reached.
    /// </exception>
    /// <exception cref="SLTimeoutException">
    ///     The SLD didn't respond within <see cref="SLOidcClientOptions.RequestTimeout" />.
    /// </exception>
    public async Task<SLCurrentUserInfo> GetCurrentUserInfoAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (!SLRequestCredential.IsValidBearerToken(accessToken))
        {
            throw new ArgumentException("The access token is empty or malformed.", nameof(accessToken));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, _sldAddress + SldServicePath + "CurrentUserInfo?IncludeB1UserBinding=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var (statusCode, body) = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (statusCode != HttpStatusCode.OK)
        {
            throw new SLAuthenticationException($"The SLD's CurrentUserInfo API failed with status code {(int)statusCode} ({statusCode}).", statusCode: statusCode);
        }

        return ParseCurrentUserInfo(body);
    }

    /// <summary>
    ///     Gets the SLD company ID of the company with the given schema name, among the companies the user of the access token
    ///     can access.
    /// </summary>
    /// <remarks>
    ///     Like SAP's samples, this selects by the schema name alone. A binding the user hasn't confirmed yet is included,
    ///     which
    ///     <see cref="SLCompanyBinding.IsConfirmed" /> tells through <see cref="GetCurrentUserInfoAsync" />.
    /// </remarks>
    /// <param name="accessToken">
    ///     The access token of the user.
    /// </param>
    /// <param name="companySchemaName">
    ///     The database schema name of the company, with a case-insensitive match.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    /// <exception cref="SLAuthenticationException">
    ///     The SLD rejected the token or couldn't be reached, the user can't access such a company, or more than one company
    ///     has this schema name, in which case <see cref="GetCurrentUserInfoAsync" /> allows selecting one by its service
    ///     unit.
    /// </exception>
    /// <exception cref="SLTimeoutException">
    ///     The SLD didn't respond within <see cref="SLOidcClientOptions.RequestTimeout" />.
    /// </exception>
    public async Task<string> GetCompanyIdAsync(string accessToken, string companySchemaName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(companySchemaName))
        {
            throw new ArgumentException("The company schema name can not be empty.", nameof(companySchemaName));
        }

        var userInfo = await GetCurrentUserInfoAsync(accessToken, cancellationToken).ConfigureAwait(false);

        var matches = userInfo.CompanyBindings
            .Where(x => string.Equals(x.CompanySchemaName, companySchemaName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            0 => throw new SLAuthenticationException($"The user of the access token is not bound to a company with the schema name '{companySchemaName}'."),
            1 when string.IsNullOrEmpty(matches[0].CompanyId) => throw new SLAuthenticationException($"The SLD's CurrentUserInfo API returned no company ID for the company with the schema name '{companySchemaName}'."),
            1 => matches[0].CompanyId,
            _ => throw new SLAuthenticationException($"More than one company bound to the user of the access token has the schema name '{companySchemaName}'. Select one through {nameof(GetCurrentUserInfoAsync)}.")
        };
    }

    /// <summary>
    ///     Creates a token source for daemon services, which requests a token through the client credentials grant, caches it
    ///     and requests a new one when it is about to expire or is rejected. There is no refresh token in this grant.
    /// </summary>
    /// <param name="scope">
    ///     The scope to request. SAP's documentation uses "openid".
    /// </param>
    /// <remarks>
    ///     The source requests its tokens through this instance, which must not be disposed while the source is in use.
    /// </remarks>
    /// <param name="expirySkew">
    ///     How long before its expiry a token is renewed, 60 seconds by default.
    /// </param>
    public SLCachingAccessTokenSource CreateClientCredentialsTokenSource(string scope = "openid", TimeSpan? expirySkew = null)
    {
        if (_clientCredential == null)
        {
            throw new InvalidOperationException($"The client credentials grant requires {nameof(SLOidcClientOptions.ClientCredential)}.");
        }

        EnsureClientId();

        return new SLCachingAccessTokenSource(cancellationToken => RequestClientCredentialsTokenAsync(scope, cancellationToken), expirySkew, null, () => UtcNow());
    }

    /// <summary>
    ///     Creates a token source for the tokens of one signed-in user, obtained by the application through its own OIDC
    ///     library, which
    ///     caches the access token and uses the refresh token when it is about to expire or is rejected, accepting a rotated
    ///     refresh token.
    /// </summary>
    /// <remarks>
    ///     Refreshes are single-flight within this instance, which is what a rotating refresh token needs. When several
    ///     processes share a user's tokens, they need to coordinate on their own, by implementing
    ///     <see cref="ISLAccessTokenSource" />.
    ///     When the refresh token is no longer valid, requests fail with a <see cref="SLAuthenticationException" /> whose
    ///     <see cref="SLAuthenticationException.OAuthError" /> is "invalid_grant", and the user must sign in again.
    ///     The source refreshes through this instance, which must not be disposed while the source is in use.
    /// </remarks>
    /// <param name="initialTokens">
    ///     The tokens of the user, whose <see cref="SLTokenResponse.RefreshToken" /> is required.
    /// </param>
    /// <param name="onTokensRefreshed">
    ///     A delegate called after each refresh, for persisting the new tokens. The source is already using the new tokens by
    ///     then. When
    ///     the delegate fails, the next request fails with a <see cref="SLAuthenticationException" /> carrying that failure,
    ///     once, and
    ///     the next refresh calls it again with newer tokens. The tokens aren't persisted until then, so a process restart may
    ///     find
    ///     a refresh token that was rotated away, meaning the user has to sign in again.
    /// </param>
    /// <param name="expirySkew">
    ///     How long before its expiry a token is renewed, 60 seconds by default.
    /// </param>
    public SLCachingAccessTokenSource CreateRefreshTokenSource(SLTokenResponse initialTokens, Func<SLTokenResponse, CancellationToken, Task> onTokensRefreshed = null, TimeSpan? expirySkew = null)
    {
        if (initialTokens == null)
        {
            throw new ArgumentNullException(nameof(initialTokens));
        }

        if (string.IsNullOrEmpty(initialTokens.RefreshToken))
        {
            throw new ArgumentException($"The initial tokens must have a {nameof(SLTokenResponse.RefreshToken)}.", nameof(initialTokens));
        }

        EnsureClientId();

        var refreshToken = initialTokens.RefreshToken;
        var idToken = initialTokens.IdToken;

        return new SLCachingAccessTokenSource(
            async cancellationToken =>
            {
                // Acquisitions don't overlap, as one past its limit can't get beyond its now cancelled request, so the current tokens need no further synchronization
                var response = await RefreshTokenAsync(refreshToken, null, cancellationToken).ConfigureAwait(false);
                refreshToken = response.RefreshToken;

                // A refresh may omit the ID token (OpenID Connect Core section 12.2). The previous one is still what a logout needs
                if (response.IdToken == null && idToken != null)
                {
                    response = new SLTokenResponse(response.AccessToken, response.ExpiresAtUtc, response.RefreshToken, idToken, response.Scope, response.RefreshExpiresAtUtc);
                }

                idToken = response.IdToken;
                return response;
            },
            expirySkew,
            initialTokens,
            () => UtcNow(),
            onTokensRefreshed);
    }

    /// <summary>
    ///     Determines whether a discovery failure says nothing about the metadata itself: any HTTP failure, 4xx included,
    ///     as that is what SAP documents the SLD returning when it fails. Invalid metadata carries no status code.
    /// </summary>
    private static bool IsUnavailabilityFailure(Exception exception) =>
        exception is SLTimeoutException or SLAuthenticationException { StatusCode: not null } or SLAuthenticationException { InnerException: HttpRequestException };

    private void EnsureClientId()
    {
        if (string.IsNullOrEmpty(_clientId))
        {
            throw new InvalidOperationException($"Requesting tokens requires {nameof(SLOidcClientOptions.ClientId)}.");
        }
    }

    private async Task<CachedMetadata> DiscoverOrKeepPreviousAsync()
    {
        try
        {
            return await DiscoverAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (_metadata != null && IsUnavailabilityFailure(ex))
        {
            // Metadata rarely changes, so an SLD restart shouldn't stop tokens from being renewed
            var previous = new CachedMetadata(_metadata.Metadata, UtcNow() + DiscoveryRetryInterval);
            _metadata = previous;
            return previous;
        }
    }

    private async Task<CachedMetadata> DiscoverAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _sldAddress + SldServicePath + "GetOpenIDConnectProvider");
        var (statusCode, body) = await SendAsync(request, CancellationToken.None).ConfigureAwait(false);

        if (statusCode != HttpStatusCode.OK)
        {
            throw new SLAuthenticationException($"The SLD's GetOpenIDConnectProvider API failed with status code {(int)statusCode} ({statusCode}).", statusCode: statusCode);
        }

        var discoveryUri = ParseDiscoveryUri(body);

        using var discoveryRequest = new HttpRequestMessage(HttpMethod.Get, discoveryUri);
        (statusCode, body) = await SendAsync(discoveryRequest, CancellationToken.None).ConfigureAwait(false);

        if (statusCode != HttpStatusCode.OK)
        {
            throw new SLAuthenticationException($"The OpenID Connect discovery document request failed with status code {(int)statusCode} ({statusCode}).", statusCode: statusCode);
        }

        var cached = new CachedMetadata(ParseProviderMetadata(body, discoveryUri), UtcNow() + _providerMetadataLifetime);
        _metadata = cached;
        return cached;
    }

    private Uri ParseDiscoveryUri(string body)
    {
        string discoveryUriValue = null;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("d", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("GetOpenIDConnectProvider", out var provider)
                && provider.ValueKind == JsonValueKind.Object)
            {
                discoveryUriValue = GetString(provider, "DiscoveryUri");
            }
        }
        catch (JsonException)
        {
            throw new SLAuthenticationException("The SLD's GetOpenIDConnectProvider API returned an invalid response.");
        }

        if (!Uri.TryCreate(discoveryUriValue, UriKind.Absolute, out var discoveryUri))
        {
            throw new SLAuthenticationException("The SLD's GetOpenIDConnectProvider API didn't return a discovery URI.");
        }

        EnsureSecureEndpoint(discoveryUri, "discovery URI");
        return discoveryUri;
    }

    private SLOidcProviderMetadata ParseProviderMetadata(string body, Uri discoveryUri)
    {
        // OpenID Connect Discovery section 4.1: the discovery document is at the issuer, without a terminating slash, plus this exact
        // suffix, and section 4.3: its issuer must exactly match that, or the document can't be trusted to describe this issuer.
        // The original string is what is compared, as the normalized forms of Uri could hide a difference
        var discoveryUriValue = discoveryUri.OriginalString;

        if (!discoveryUriValue.EndsWith(WellKnownPath, StringComparison.Ordinal) || discoveryUri.Query.Length > 0 || discoveryUri.Fragment.Length > 0)
        {
            throw new SLAuthenticationException($"The discovery URI '{discoveryUri}' doesn't end with '{WellKnownPath}', so the issuer can't be validated.");
        }

        var expectedIssuer = discoveryUriValue.Substring(0, discoveryUriValue.Length - WellKnownPath.Length);

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new SLAuthenticationException("The OpenID Connect discovery document is invalid.");
            }

            var issuer = GetString(root, "issuer");

            if (!string.Equals(issuer, expectedIssuer, StringComparison.Ordinal) && !string.Equals(issuer, expectedIssuer + "/", StringComparison.Ordinal))
            {
                throw new SLAuthenticationException($"The issuer '{issuer}' of the OpenID Connect discovery document doesn't match the expected issuer '{expectedIssuer}'.");
            }

            return new SLOidcProviderMetadata
            {
                DiscoveryUri = discoveryUri,
                Issuer = issuer,
                TokenEndpoint = ParseEndpoint(root, "token_endpoint") ?? throw new SLAuthenticationException("The OpenID Connect discovery document has no token endpoint."),
                AuthorizationEndpoint = ParseEndpoint(root, "authorization_endpoint"),
                UserInfoEndpoint = ParseEndpoint(root, "userinfo_endpoint"),
                EndSessionEndpoint = ParseEndpoint(root, "end_session_endpoint"),
                RevocationEndpoint = ParseEndpoint(root, "revocation_endpoint"),
                JwksUri = ParseEndpoint(root, "jwks_uri")
            };
        }
        catch (JsonException)
        {
            throw new SLAuthenticationException("The OpenID Connect discovery document is invalid.");
        }
    }

    private Uri ParseEndpoint(JsonElement root, string name)
    {
        var value = GetString(root, name);

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint))
        {
            throw new SLAuthenticationException($"The '{name}' of the OpenID Connect discovery document is invalid.");
        }

        EnsureSecureEndpoint(endpoint, name);
        return endpoint;
    }

    private bool IsAllowedEndpoint(Uri uri) => _allowInsecureHttp || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private void EnsureSecureEndpoint(Uri uri, string name)
    {
        if (!IsAllowedEndpoint(uri))
        {
            throw new SLAuthenticationException($"The {name} must use HTTPS, as OAuth requires TLS (RFC 6749 section 3.2). To allow it regardless, set {nameof(SLOidcClientOptions.AllowInsecureHttp)}.");
        }
    }

    /// <param name="form">
    ///     The grant parameters.
    /// </param>
    /// <param name="currentRefreshToken">
    ///     The refresh token being used, kept when the server doesn't issue a new one (RFC 6749 section 6).
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    private async Task<SLTokenResponse> RequestTokenAsync(List<KeyValuePair<string, string>> form, string currentRefreshToken, CancellationToken cancellationToken)
    {
        EnsureClientId();

        var metadata = await GetProviderMetadataAsync(cancellationToken).ConfigureAwait(false);
        var requestedAtUtc = UtcNow();

        form.Add(new KeyValuePair<string, string>("client_id", _clientId));

        if (_clientCredential != null)
        {
            try
            {
                await _clientCredential.AddToTokenRequestAsync(form, _clientId, metadata.TokenEndpoint, requestedAtUtc, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new SLAuthenticationException("Signing the client assertion failed. See the inner exception for details.", ex);
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, metadata.TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        var (statusCode, body) = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        return statusCode == HttpStatusCode.OK
            ? ParseTokenResponse(body, requestedAtUtc, currentRefreshToken)
            : throw CreateTokenErrorException(statusCode, body);
    }

    /// <summary>
    ///     Parses a successful token response. Nothing in the body makes it into an exception, as it holds tokens.
    /// </summary>
    private static SLTokenResponse ParseTokenResponse(string body, DateTimeOffset requestedAtUtc, string currentRefreshToken)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new SLAuthenticationException("The token endpoint returned an invalid response.", statusCode: HttpStatusCode.OK);
            }

            var accessToken = GetString(root, "access_token");

            if (!SLRequestCredential.IsValidBearerToken(accessToken))
            {
                throw new SLAuthenticationException("The token endpoint returned no access token or a malformed one.", statusCode: HttpStatusCode.OK);
            }

            // RFC 6749 section 7.1: a client must not use a token whose type it doesn't understand
            if (!string.Equals(GetString(root, "token_type"), "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                throw new SLAuthenticationException("The token endpoint returned an access token that is not a bearer token.", statusCode: HttpStatusCode.OK);
            }

            DateTimeOffset? expiresAtUtc = null;

            // Counted from before the request was sent, so the token can only be treated as expired too soon, not too late
            if (GetDouble(root, "expires_in") is { } expiresIn)
            {
                expiresAtUtc = requestedAtUtc.AddSeconds(Math.Max(0, Math.Min(expiresIn, MaxTokenLifetimeSeconds)));
            }

            var refreshToken = GetString(root, "refresh_token");

            // 0 means that there is no refresh token, or that it has no expiry of its own, like an offline token
            DateTimeOffset? refreshExpiresAtUtc = GetDouble(root, "refresh_expires_in") is > 0 and var refreshExpiresIn
                ? requestedAtUtc.AddSeconds(Math.Min(refreshExpiresIn, MaxTokenLifetimeSeconds))
                : null;

            return new SLTokenResponse(accessToken,
                expiresAtUtc,
                string.IsNullOrEmpty(refreshToken) ? currentRefreshToken : refreshToken,
                GetString(root, "id_token"),
                GetString(root, "scope"),
                refreshExpiresAtUtc);
        }
        catch (JsonException)
        {
            throw new SLAuthenticationException("The token endpoint returned an invalid response.", statusCode: HttpStatusCode.OK);
        }
    }

    private static SLAuthenticationException CreateTokenErrorException(HttpStatusCode statusCode, string body)
    {
        string error = null;
        string errorDescription = null;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                error = GetString(document.RootElement, "error");
                errorDescription = GetString(document.RootElement, "error_description");
            }
        }
        catch (JsonException)
        {
            // Not an OAuth error response, so only the status code is reported
        }

        var message = error == null
            ? $"The token request failed with status code {(int)statusCode} ({statusCode})."
            : $"The token request failed with status code {(int)statusCode} ({statusCode}): {error}{(errorDescription == null ? string.Empty : " - " + errorDescription)}";

        return new SLAuthenticationException(message, statusCode: statusCode, oAuthError: error, oAuthErrorDescription: errorDescription);
    }

    private static SLCurrentUserInfo ParseCurrentUserInfo(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("d", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("CurrentUserInfo", out var userInfo)
                || userInfo.ValueKind != JsonValueKind.Object)
            {
                throw new SLAuthenticationException("The SLD's CurrentUserInfo API returned an invalid response.");
            }

            var bindings = new List<SLCompanyBinding>();

            if (userInfo.TryGetProperty("B1UserBindings", out var bindingsElement)
                && bindingsElement.ValueKind == JsonValueKind.Object
                && bindingsElement.TryGetProperty("results", out var results)
                && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var binding in results.EnumerateArray())
                {
                    if (binding.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    bindings.Add(new SLCompanyBinding
                    {
                        CompanyId = GetString(binding, "CompanyID"),
                        CompanySchemaName = GetString(binding, "CompanySchemaName"),
                        CompanyDisplayName = GetString(binding, "CompanyDisplayName"),
                        UserCode = GetString(binding, "B1UserCode") ?? GetString(binding, "UserCode"),
                        ServiceUnitId = GetString(binding, "ServiceUnitID"),
                        ServiceUnitName = GetString(binding, "ServiceUnitName"),
                        DatabaseInstanceName = GetString(binding, "DatabaseInstanceName"),
                        Version = GetString(binding, "Version"),
                        Localization = GetString(binding, "Localization"),
                        IsConfirmed = GetBoolean(binding, "Confirmed"),
                        IsSuperuser = GetBoolean(binding, "IsSuperuser")
                    });
                }
            }

            return new SLCurrentUserInfo
            {
                UserIdentity = GetString(userInfo, "UserIdentity"),
                UserName = GetString(userInfo, "UserName"),
                CompanyBindings = bindings,
                RawJson = userInfo.GetRawText()
            };
        }
        catch (JsonException)
        {
            throw new SLAuthenticationException("The SLD's CurrentUserInfo API returned an invalid response.");
        }
    }

    private static string GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),

            // These styles also accept "NaN" and "Infinity", which are no lifetime
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                                      && !double.IsNaN(parsed)
                                      && !double.IsInfinity(parsed) => parsed,
            _ => null
        };
    }

    private static bool GetBoolean(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private async Task<(HttpStatusCode StatusCode, string Body)> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
            return (response.StatusCode, await response.ReadStringAsync(timeout.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SLTimeoutException($"The request timed out: {request.Method} {request.RequestUri}", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SLAuthenticationException($"The request failed: {request.Method} {request.RequestUri}. See the inner exception for details.", ex);
        }
    }

    private sealed class CachedMetadata(
        SLOidcProviderMetadata metadata,
        DateTimeOffset expiresAtUtc)
    {
        public SLOidcProviderMetadata Metadata { get; } = metadata;
        public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
    }
}
