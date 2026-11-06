using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace B1SLayer.Test;

public class SLOidcClientTests : IDisposable
{
    private const string SldAddress = "https://b1hana:40000";
    private const string Issuer = "https://b1hana:40020/auth/realms/sapb1";
    private const string TokenEndpoint = Issuer + "/protocol/openid-connect/token";
    private const string ClientId = "b1-ext-daemon-00000000-0000-0000-0000-000000000000";
    private const string ProviderUrl = SldAddress + "/sld/sld0100.svc/GetOpenIDConnectProvider";
    private const string UserInfoUrl = SldAddress + "/sld/sld0100.svc/CurrentUserInfo*";
    private const string DiscoveryUrl = Issuer + "/.well-known/openid-configuration";
    private static readonly object ProviderResponse = new { d = new { GetOpenIDConnectProvider = new { DiscoveryUri = DiscoveryUrl } } };
    private static readonly string SuccessfulToken = JsonSerializer.Serialize(new { access_token = "access-1", token_type = "Bearer", expires_in = 1500, refresh_expires_in = 0, id_token = "id-1", scope = "openid email" });

    private readonly MockHttp _http = new();
    private readonly RSA _rsa = RSA.Create(2048);

    public void Dispose()
    {
        _rsa.Dispose();
        _http.Dispose();
    }

    private SLOidcClient CreateClient(SLClientCredential credential = null, string clientId = ClientId, Action<SLOidcClientOptions> configure = null)
    {
        var options = new SLOidcClientOptions
        {
            SldAddress = new Uri(SldAddress),
            ClientId = clientId,
            ClientCredential = credential,
            HttpMessageHandler = _http
        };

        configure?.Invoke(options);
        return new SLOidcClient(options);
    }

    private void RespondWithDiscovery(string issuer = Issuer, string tokenEndpoint = TokenEndpoint, string discoveryUri = DiscoveryUrl)
    {
        _http.ForCallsTo(ProviderUrl).RespondWithJson(new { d = new { GetOpenIDConnectProvider = new { DiscoveryUri = discoveryUri, Realm = "sapb1" } } });

        _http.ForCallsTo(discoveryUri)
            .RespondWithJson(new Dictionary<string, object>
            {
                ["issuer"] = issuer,
                ["token_endpoint"] = tokenEndpoint,
                ["authorization_endpoint"] = Issuer + "/protocol/openid-connect/auth",
                ["userinfo_endpoint"] = Issuer + "/protocol/openid-connect/userinfo",
                ["end_session_endpoint"] = Issuer + "/protocol/openid-connect/logout",
                ["revocation_endpoint"] = Issuer + "/protocol/openid-connect/revoke",
                ["jwks_uri"] = Issuer + "/protocol/openid-connect/certs"
            });
    }

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&').Select(x => x.Split('=')).ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x[1].Replace('+', ' ')));

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }

    [Fact]
    public async Task Discovery_FollowsTheSldAndValidatesTheIssuer()
    {
        RespondWithDiscovery();
        using var client = CreateClient();

        var metadata = await client.GetProviderMetadataAsync();

        Assert.Equal(new Uri(DiscoveryUrl), metadata.DiscoveryUri);
        Assert.Equal(Issuer, metadata.Issuer);
        Assert.Equal(new Uri(TokenEndpoint), metadata.TokenEndpoint);
        Assert.EndsWith("/auth", metadata.AuthorizationEndpoint.AbsoluteUri);
        Assert.EndsWith("/userinfo", metadata.UserInfoEndpoint.AbsoluteUri);
        Assert.EndsWith("/logout", metadata.EndSessionEndpoint.AbsoluteUri);
        Assert.EndsWith("/revoke", metadata.RevocationEndpoint.AbsoluteUri);
        Assert.EndsWith("/certs", metadata.JwksUri.AbsoluteUri);

        // As in the SLD API reference: the JSON format is requested through the Accept header, with no credentials
        _http.ShouldHaveCalled(ProviderUrl).WithVerb(HttpMethod.Get).WithHeader("Accept", "application/json").WithoutHeader("Authorization").WithoutQueryParam("$format").Times(1);
    }

    [Theory]
    [InlineData("https://attacker.example/auth/realms/sapb1")]
    [InlineData(Issuer + "//")]
    [InlineData("https://B1HANA:40020/auth/realms/sapb1")]
    [InlineData("https://b1hana:40020/auth/realms/other")]
    [InlineData("")]
    public async Task Discovery_IssuerMismatch_IsRejected(string issuer)
    {
        RespondWithDiscovery(issuer);
        using var client = CreateClient();

        var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => client.GetProviderMetadataAsync());

        Assert.Contains("issuer", exception.Message);
    }

    [Fact]
    public async Task Discovery_IssuerWithTerminatingSlash_IsAccepted()
    {
        // OpenID Connect Discovery section 4.1 removes it before appending the well-known suffix
        RespondWithDiscovery(Issuer + "/");
        using var client = CreateClient();

        var metadata = await client.GetProviderMetadataAsync();

        Assert.Equal(Issuer + "/", metadata.Issuer);
    }

    [Fact]
    public async Task Discovery_IssuerIsComparedWithTheExactDiscoveryUri()
    {
        // Uri normalization lowercases the host, which must not make this fail when both have the same exact form
        const string upperIssuer = "https://B1HANA:40020/auth/realms/sapb1";
        _http.ForCallsTo(ProviderUrl).RespondWithJson(new { d = new { GetOpenIDConnectProvider = new { DiscoveryUri = upperIssuer + "/.well-known/openid-configuration" } } });
        _http.ForCallsTo(DiscoveryUrl).RespondWithJson(new { issuer = upperIssuer, token_endpoint = upperIssuer + "/token" });
        using var client = CreateClient();

        var metadata = await client.GetProviderMetadataAsync();

        Assert.Equal(upperIssuer, metadata.Issuer);
    }

    [Fact]
    public async Task Discovery_IssuerMismatch_PreventsAnyTokenRequest()
    {
        RespondWithDiscovery("https://attacker.example");
        using var client = CreateClient(SLClientCredential.FromClientSecret("secret"));

        await Assert.ThrowsAsync<SLAuthenticationException>(() => client.RequestClientCredentialsTokenAsync());

        Assert.DoesNotContain(_http.CallLog, x => x.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData("http://b1hana:40020/auth/realms/sapb1/protocol/openid-connect/token")]
    [InlineData("not a uri")]
    [InlineData(null)]
    public async Task Discovery_InvalidTokenEndpoint_IsRejected(string tokenEndpoint)
    {
        RespondWithDiscovery(tokenEndpoint: tokenEndpoint);
        using var client = CreateClient();

        await Assert.ThrowsAsync<SLAuthenticationException>(() => client.GetProviderMetadataAsync());
    }

    [Fact]
    public async Task Discovery_InsecureDiscoveryUri_IsRejected()
    {
        _http.ForCallsTo(ProviderUrl).RespondWithJson(new { d = new { GetOpenIDConnectProvider = new { DiscoveryUri = "http://b1hana:40020/auth/realms/sapb1/.well-known/openid-configuration" } } });
        using var client = CreateClient();

        await Assert.ThrowsAsync<SLAuthenticationException>(() => client.GetProviderMetadataAsync());

        Assert.Single(_http.CallLog);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("<html></html>")]
    [InlineData("{\"d\":{\"GetOpenIDConnectProvider\":{}}}")]
    [InlineData("{\"d\":{\"GetOpenIDConnectProvider\":{\"DiscoveryUri\":\"https://b1hana:40020/unexpected\"}}}")]
    public async Task Discovery_InvalidSldResponse_IsRejected(string response)
    {
        _http.ForCallsTo(ProviderUrl).RespondWith(response);
        _http.ForCallsTo("https://b1hana:40020/unexpected").RespondWith("{\"issuer\":\"https://b1hana:40020/unexpected\"}");
        using var client = CreateClient();

        await Assert.ThrowsAsync<SLAuthenticationException>(() => client.GetProviderMetadataAsync());
    }

    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Discovery_SldFailure_CarriesTheStatusCode(int status)
    {
        _http.ForCallsTo(ProviderUrl).RespondWith("nope", status);
        using var client = CreateClient();

        var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => client.GetProviderMetadataAsync());

        Assert.Equal((HttpStatusCode)status, exception.StatusCode);
    }

    [Fact]
    public async Task Discovery_IsCachedUntilItsLifetimeEnds()
    {
        RespondWithDiscovery();
        var now = DateTimeOffset.UtcNow;
        using var client = CreateClient();
        client.UtcNow = () => now;

        await client.GetProviderMetadataAsync();
        await client.GetProviderMetadataAsync();
        _http.ShouldHaveCalled(DiscoveryUrl).Times(1);

        now += TimeSpan.FromHours(2);
        await client.GetProviderMetadataAsync();
        _http.ShouldHaveCalled(DiscoveryUrl).Times(2);
    }

    [Fact]
    public async Task Discovery_SldUnavailable_KeepsUsingThePreviousMetadata()
    {
        _http.ForCallsTo(ProviderUrl).RespondWithJson(ProviderResponse).RespondWith("restarting", 503);
        _http.ForCallsTo(DiscoveryUrl).RespondWithJson(new { issuer = Issuer, token_endpoint = TokenEndpoint });
        _http.ForCallsTo(TokenEndpoint).RespondWith(SuccessfulToken);
        var now = DateTimeOffset.UtcNow;
        using var client = CreateClient(SLClientCredential.FromClientSecret("secret"));
        client.UtcNow = () => now;
        await client.GetProviderMetadataAsync();

        now += TimeSpan.FromHours(2);
        var token = await client.RequestClientCredentialsTokenAsync();

        Assert.Equal("access-1", token.AccessToken);
        _http.ShouldHaveCalled(ProviderUrl).Times(2);
    }

    [Fact]
    public async Task Discovery_InvalidNewMetadata_IsNotReplacedByThePreviousOne()
    {
        _http.ForCallsTo(ProviderUrl).RespondWithJson(ProviderResponse);
        _http.ForCallsTo(DiscoveryUrl)
            .RespondWithJson(new { issuer = Issuer, token_endpoint = TokenEndpoint })
            .RespondWithJson(new { issuer = "https://attacker.example/auth/realms/sapb1", token_endpoint = TokenEndpoint });
        var now = DateTimeOffset.UtcNow;
        using var client = CreateClient();
        client.UtcNow = () => now;
        await client.GetProviderMetadataAsync();

        now += TimeSpan.FromHours(2);

        await Assert.ThrowsAsync<SLAuthenticationException>(() => client.GetProviderMetadataAsync());
    }

    [Fact]
    public async Task Discovery_SldClientError_KeepsUsingThePreviousMetadata()
    {
        // SAP documents 4xx as how the SLD reports a failure of this API
        _http.ForCallsTo(ProviderUrl).RespondWithJson(ProviderResponse).RespondWith("gone", 404);
        _http.ForCallsTo(DiscoveryUrl).RespondWithJson(new { issuer = Issuer, token_endpoint = TokenEndpoint });
        var now = DateTimeOffset.UtcNow;
        using var client = CreateClient();
        client.UtcNow = () => now;
        await client.GetProviderMetadataAsync();

        now += TimeSpan.FromHours(2);

        Assert.Equal(Issuer, (await client.GetProviderMetadataAsync()).Issuer);
        _http.ShouldHaveCalled(ProviderUrl).Times(2);
    }

    [Fact]
    public async Task Discovery_DuringAnOutage_IsOnlyRepeatedAfterTheRetryInterval()
    {
        _http.ForCallsTo(ProviderUrl).RespondWithJson(ProviderResponse).RespondWith("unavailable", 503);
        _http.ForCallsTo(DiscoveryUrl).RespondWithJson(new { issuer = Issuer, token_endpoint = TokenEndpoint });
        var now = DateTimeOffset.UtcNow;
        using var client = CreateClient();
        client.UtcNow = () => now;
        await client.GetProviderMetadataAsync();

        now += TimeSpan.FromHours(2);
        await client.GetProviderMetadataAsync();
        await client.GetProviderMetadataAsync();
        _http.ShouldHaveCalled(ProviderUrl).Times(2);

        now += TimeSpan.FromSeconds(29);
        await client.GetProviderMetadataAsync();
        _http.ShouldHaveCalled(ProviderUrl).Times(2);

        now += TimeSpan.FromSeconds(1);
        Assert.Equal(Issuer, (await client.GetProviderMetadataAsync()).Issuer);
        _http.ShouldHaveCalled(ProviderUrl).Times(3);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public async Task TokenResponse_NonFiniteLifetime_IsTreatedAsAbsent(string value)
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith($"{{\"access_token\":\"a\",\"token_type\":\"Bearer\",\"expires_in\":\"{value}\",\"refresh_expires_in\":\"{value}\"}}");
        using var client = CreateClient(SLClientCredential.FromClientSecret("secret"));

        var response = await client.RequestClientCredentialsTokenAsync();

        Assert.Null(response.ExpiresAtUtc);
        Assert.Null(response.RefreshExpiresAtUtc);
    }

    [Fact]
    public async Task Discovery_TransportFailure_KeepsUsingThePreviousMetadata()
    {
        _http.ForCallsTo(ProviderUrl).RespondWithJson(ProviderResponse).SimulateException(new HttpRequestException("connection refused"));
        _http.ForCallsTo(DiscoveryUrl).RespondWithJson(new { issuer = Issuer, token_endpoint = TokenEndpoint });
        var now = DateTimeOffset.UtcNow;
        using var client = CreateClient();
        client.UtcNow = () => now;
        await client.GetProviderMetadataAsync();

        now += TimeSpan.FromHours(2);

        Assert.Equal(Issuer, (await client.GetProviderMetadataAsync()).Issuer);
    }

    [Fact]
    public async Task Discovery_AnotherCallerCompletingItSinceTheCheck_IsNotRepeated()
    {
        _http.ForCallsTo(ProviderUrl).RespondWithJson(ProviderResponse);
        _http.ForCallsTo(DiscoveryUrl).RespondWithJson(new { issuer = Issuer, token_endpoint = TokenEndpoint });
        var now = DateTimeOffset.UtcNow;
        var interleave = false;
        using var client = CreateClient();

        // The clock is read right after the cached metadata was, which is where another caller can get its discovery in
        client.UtcNow = () =>
        {
            if (interleave)
            {
                interleave = false;
                client.GetProviderMetadataAsync().GetAwaiter().GetResult();
            }

            return now;
        };

        await client.GetProviderMetadataAsync();
        now += TimeSpan.FromHours(2);
        interleave = true;

        await client.GetProviderMetadataAsync();

        _http.ShouldHaveCalled(ProviderUrl).Times(2);
    }

    [Fact]
    public async Task TransportFailure_SurfacesAsAnAuthenticationFailure()
    {
        _http.ForCallsTo(ProviderUrl).SimulateException(new HttpRequestException("connection refused"));
        _http.ForCallsTo(UserInfoUrl).SimulateException(new HttpRequestException("connection refused"));
        using var client = CreateClient(clientId: null);

        var discovery = await Assert.ThrowsAsync<SLAuthenticationException>(() => client.GetProviderMetadataAsync());
        var userInfo = await Assert.ThrowsAsync<SLAuthenticationException>(() => client.GetCurrentUserInfoAsync("access-1"));

        Assert.IsType<HttpRequestException>(discovery.InnerException);
        Assert.IsType<HttpRequestException>(userInfo.InnerException);
        Assert.Null(discovery.StatusCode);
    }

    [Fact]
    public async Task Discovery_FailureIsNotCached()
    {
        _http.ForCallsTo(ProviderUrl).RespondWith("nope", 500).RespondWithJson(new { d = new { GetOpenIDConnectProvider = new { DiscoveryUri = DiscoveryUrl } } });
        _http.ForCallsTo(DiscoveryUrl).RespondWithJson(new { issuer = Issuer, token_endpoint = TokenEndpoint });
        using var client = CreateClient();

        await Assert.ThrowsAsync<SLAuthenticationException>(() => client.GetProviderMetadataAsync());
        var metadata = await client.GetProviderMetadataAsync();

        Assert.Equal(Issuer, metadata.Issuer);
    }

    [Fact]
    public async Task Discovery_Concurrent_IsSingleFlight()
    {
        _http.ForCallsTo(ProviderUrl).WithDelay(TimeSpan.FromMilliseconds(50)).RespondWithJson(new { d = new { GetOpenIDConnectProvider = new { DiscoveryUri = DiscoveryUrl } } });
        _http.ForCallsTo(DiscoveryUrl).RespondWithJson(new { issuer = Issuer, token_endpoint = TokenEndpoint });
        using var client = CreateClient();

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.GetProviderMetadataAsync()));

        _http.ShouldHaveCalled(ProviderUrl).Times(1);
    }

    [Fact]
    public async Task ClientCredentials_WithPrivateKey_SendsSignedJwtWithExpectedClaims()
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith(SuccessfulToken, headers: new Dictionary<string, string> { ["Content-Type"] = "application/json" });

        var now = DateTimeOffset.UtcNow;
        var privateKey = Convert.ToBase64String(_rsa.ExportPkcs8PrivateKey());
        using var client = CreateClient(SLClientCredential.FromPrivateKey(privateKey));
        client.UtcNow = () => now;

        var response = await client.RequestClientCredentialsTokenAsync();

        Assert.Equal("access-1", response.AccessToken);
        Assert.Equal("id-1", response.IdToken);
        Assert.Equal("openid email", response.Scope);
        Assert.Null(response.RefreshToken);
        Assert.Equal(now.AddSeconds(1500), response.ExpiresAtUtc);

        var call = _http.CallLog.Single(x => x.Method == HttpMethod.Post);
        Assert.Equal(new Uri(TokenEndpoint), call.Uri);
        Assert.Contains(call.Headers, x => x is { Key: "Content-Type", Value: "application/x-www-form-urlencoded" });
        Assert.Contains(call.Headers, x => x is { Key: "Accept", Value: "application/json" });

        var form = ParseForm(call.RequestBody);
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal("openid", form["scope"]);
        Assert.Equal(ClientId, form["client_id"]);
        Assert.Equal("urn:ietf:params:oauth:client-assertion-type:jwt-bearer", form["client_assertion_type"]);
        Assert.False(form.ContainsKey("client_secret"));

        var parts = form["client_assertion"].Split('.');
        Assert.Equal(3, parts.Length);

        using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
        Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.RootElement.GetProperty("typ").GetString());

        using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        var claims = payload.RootElement;
        Assert.Equal(ClientId, claims.GetProperty("iss").GetString());
        Assert.Equal(ClientId, claims.GetProperty("sub").GetString());
        Assert.Equal(TokenEndpoint, claims.GetProperty("aud").GetString());
        Assert.True(Guid.TryParse(claims.GetProperty("jti").GetString(), out _));

        // Short-lived per RFC 7523, with some tolerance for clock differences, unlike the ±120 minutes of SAP's sample
        var nowSeconds = now.ToUnixTimeSeconds();
        var expires = claims.GetProperty("exp").GetInt64();
        Assert.Equal(nowSeconds + 300, expires);
        // SAP's Keycloak rejects an iat over 60 seconds old, so a backdated one leaves an assertion little time to get there
        Assert.InRange(claims.GetProperty("iat").GetInt64(), nowSeconds - 15, nowSeconds);
        Assert.InRange(claims.GetProperty("nbf").GetInt64(), nowSeconds - 120, nowSeconds);
        Assert.True(expires - claims.GetProperty("iat").GetInt64() <= 600);

        Assert.True(_rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Base64UrlDecode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public async Task ClientCredentials_EachRequest_UsesANewJti()
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith(SuccessfulToken);
        using var client = CreateClient(SLClientCredential.FromPrivateKey(Convert.ToBase64String(_rsa.ExportPkcs8PrivateKey())));

        await client.RequestClientCredentialsTokenAsync();
        await client.RequestClientCredentialsTokenAsync();

        var jtis = _http.CallLog.Where(x => x.Method == HttpMethod.Post)
            .Select(x =>
            {
                using var payload = JsonDocument.Parse(Base64UrlDecode(ParseForm(x.RequestBody)["client_assertion"].Split('.')[1]));
                return payload.RootElement.GetProperty("jti").GetString();
            })
            .ToList();

        Assert.Equal(2, jtis.Distinct().Count());
    }

    [Fact]
    public async Task ClientCredentials_WithCustomSigner_UsesItsSignature()
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith(SuccessfulToken);

        using var client = CreateClient(SLClientCredential.FromSigner((data, _) => Task.FromResult(_rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))));

        await client.RequestClientCredentialsTokenAsync();

        var parts = ParseForm(_http.CallLog.Single(x => x.Method == HttpMethod.Post).RequestBody)["client_assertion"].Split('.');
        Assert.True(_rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Base64UrlDecode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public async Task ClientCredentials_SignerFailure_SurfacesAsAuthenticationFailure()
    {
        RespondWithDiscovery();
        using var client = CreateClient(SLClientCredential.FromSigner((_, _) => throw new CryptographicException("hsm offline")));

        var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => client.RequestClientCredentialsTokenAsync());

        Assert.IsType<CryptographicException>(exception.InnerException);
        Assert.DoesNotContain(_http.CallLog, x => x.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ClientCredentials_WithClientSecret_SendsItInTheBody()
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith(SuccessfulToken);
        using var client = CreateClient(SLClientCredential.FromClientSecret("s3cret&value"));

        await client.RequestClientCredentialsTokenAsync();

        var form = ParseForm(_http.CallLog.Single(x => x.Method == HttpMethod.Post).RequestBody);
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal("openid", form["scope"]);
        Assert.Equal(ClientId, form["client_id"]);
        Assert.Equal("s3cret&value", form["client_secret"]);
        Assert.False(form.ContainsKey("client_assertion"));
    }

    [Fact]
    public async Task ClientCredentials_WithoutCredentialOrClientId_FailClearly()
    {
        RespondWithDiscovery();
        using var noCredential = CreateClient();
        using var noClientId = CreateClient(SLClientCredential.FromClientSecret("secret"), null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => noCredential.RequestClientCredentialsTokenAsync());
        Assert.Throws<InvalidOperationException>(() => noCredential.CreateClientCredentialsTokenSource());
        await Assert.ThrowsAsync<InvalidOperationException>(() => noClientId.RequestClientCredentialsTokenAsync());

        // Failing here, not when the initial token needs its first renewal in production
        Assert.Throws<InvalidOperationException>(() => noClientId.CreateClientCredentialsTokenSource());
        Assert.Throws<InvalidOperationException>(() => noClientId.CreateRefreshTokenSource(new SLTokenResponse("a0", null, "r0")));
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    [InlineData("")]
    public void FromPrivateKey_InvalidKey_FailsWithoutEchoingIt(string key)
    {
        var exception = Assert.Throws<ArgumentException>(() => SLClientCredential.FromPrivateKey(key));

        Assert.Null(exception.InnerException);

        if (key.Length > 0)
        {
            Assert.DoesNotContain(key, exception.ToString());
        }
    }

    [Fact]
    public void Credentials_ToString_NeverIncludeTheirContent()
    {
        const string secret = "s3cret-value";

        Assert.DoesNotContain(secret, SLClientCredential.FromClientSecret(secret).ToString());
        Assert.DoesNotContain(Convert.ToBase64String(_rsa.ExportPkcs8PrivateKey()), SLClientCredential.FromPrivateKey(Convert.ToBase64String(_rsa.ExportPkcs8PrivateKey())).ToString());
    }

    [Fact]
    public async Task TokenEndpointError_ExposesTheOAuthErrorButNoCredentials()
    {
        const string secret = "s3cret-value";
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith("{\"error\":\"invalid_client\",\"error_description\":\"Invalid client credentials\"}", 401);
        using var client = CreateClient(SLClientCredential.FromClientSecret(secret));

        var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => client.RequestClientCredentialsTokenAsync());

        Assert.Equal("invalid_client", exception.OAuthError);
        Assert.Equal("Invalid client credentials", exception.OAuthErrorDescription);
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Null(exception.ResponseContent);
        Assert.DoesNotContain(secret, exception.ToString());
    }

    [Theory]
    [InlineData("{\"access_token\":\"leaked-token\",\"token_type\":\"mac\"}")]
    [InlineData("{\"access_token\":\"leaked-token\"}")]
    [InlineData("{\"token_type\":\"Bearer\",\"refresh_token\":\"leaked-token\"}")]
    [InlineData("{\"access_token\":\"leaked token\",\"token_type\":\"Bearer\"}")]
    [InlineData("leaked-token")]
    [InlineData("[\"leaked-token\"]")]
    public async Task InvalidTokenResponse_IsRejectedWithoutLeakingTheBody(string body)
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith(body);
        using var client = CreateClient(SLClientCredential.FromClientSecret("secret"));

        var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => client.RequestClientCredentialsTokenAsync());

        Assert.DoesNotContain("leaked", exception.ToString());
        Assert.Null(exception.ResponseContent);
    }

    [Fact]
    public async Task TokenResponse_RedirectIsNotFollowedByTheLibrary()
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith(status: 302, headers: new Dictionary<string, string> { ["Location"] = "https://attacker.example/token" });
        using var client = CreateClient(SLClientCredential.FromClientSecret("secret"));

        var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => client.RequestClientCredentialsTokenAsync());

        Assert.Equal(HttpStatusCode.Redirect, exception.StatusCode);
        _http.ShouldNotHaveCalled("https://attacker.example/*");
    }

    [Theory]
    [InlineData("\"3600\"", 3600)]
    [InlineData("3600", 3600)]
    [InlineData("0", 0)]
    [InlineData("-5", 0)]
    public async Task TokenResponse_ExpiresIn_IsParsedTolerantly(string expiresIn, int expectedSeconds)
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith($"{{\"access_token\":\"a\",\"token_type\":\"bearer\",\"expires_in\":{expiresIn}}}");
        var now = DateTimeOffset.UtcNow;
        using var client = CreateClient(SLClientCredential.FromClientSecret("secret"));
        client.UtcNow = () => now;

        var response = await client.RequestClientCredentialsTokenAsync();

        Assert.Equal(now.AddSeconds(expectedSeconds), response.ExpiresAtUtc);
    }

    [Fact]
    public async Task TokenResponse_AbsurdExpiresIn_DoesNotOverflow()
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith("{\"access_token\":\"a\",\"token_type\":\"Bearer\",\"expires_in\":1e300}");
        using var client = CreateClient(SLClientCredential.FromClientSecret("secret"));

        var response = await client.RequestClientCredentialsTokenAsync();

        Assert.NotNull(response.ExpiresAtUtc);
    }

    [Fact]
    public async Task TokenResponse_WithoutExpiresIn_HasUnknownExpiry()
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith("{\"access_token\":\"a\",\"token_type\":\"Bearer\"}");
        using var client = CreateClient(SLClientCredential.FromClientSecret("secret"));

        var response = await client.RequestClientCredentialsTokenAsync();

        Assert.Null(response.ExpiresAtUtc);
    }

    [Fact]
    public async Task Refresh_RotatedToken_IsReturned()
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith("{\"access_token\":\"a2\",\"token_type\":\"Bearer\",\"expires_in\":300,\"refresh_token\":\"rotated\"}");
        using var client = CreateClient(SLClientCredential.FromClientSecret("secret"));

        var response = await client.RefreshTokenAsync("original");

        Assert.Equal("a2", response.AccessToken);
        Assert.Equal("rotated", response.RefreshToken);

        var form = ParseForm(_http.CallLog.Single(x => x.Method == HttpMethod.Post).RequestBody);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("original", form["refresh_token"]);
        Assert.Equal(ClientId, form["client_id"]);
        Assert.Equal("secret", form["client_secret"]);
        Assert.False(form.ContainsKey("scope"));
    }

    [Fact]
    public async Task Refresh_WithoutRotation_KeepsTheCurrentToken()
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith("{\"access_token\":\"a2\",\"token_type\":\"Bearer\",\"expires_in\":300}");
        using var client = CreateClient();

        var response = await client.RefreshTokenAsync("original");

        Assert.Equal("original", response.RefreshToken);
    }

    [Fact]
    public async Task Refresh_PublicClient_SendsOnlyTheClientId()
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith(SuccessfulToken);
        using var client = CreateClient();

        await client.RefreshTokenAsync("original", "openid offline_access");

        var form = ParseForm(_http.CallLog.Single(x => x.Method == HttpMethod.Post).RequestBody);
        Assert.Equal(ClientId, form["client_id"]);
        Assert.Equal("openid offline_access", form["scope"]);
        Assert.DoesNotContain(form.Keys, x => x.StartsWith("client_secret") || x.StartsWith("client_assertion"));
    }

    [Fact]
    public async Task Refresh_InvalidGrant_IsDistinguishable()
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith("{\"error\":\"invalid_grant\",\"error_description\":\"Token is not active\"}", 400);
        using var client = CreateClient();

        var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => client.RefreshTokenAsync("original-refresh-token"));

        Assert.Equal("invalid_grant", exception.OAuthError);
        Assert.DoesNotContain("original-refresh-token", exception.ToString());
    }

    [Fact]
    public async Task RefreshTokenSource_UsesRotatedTokenAndPersistsIt_SingleFlight()
    {
        RespondWithDiscovery();

        // Every refresh rotates. Reusing a previous refresh token would fail, like on a realm that revokes them
        var issued = 0;
        _http.ForCallsTo(TokenEndpoint)
            .WithDelay(TimeSpan.FromMilliseconds(50))
            .RespondWith("{\"access_token\":\"a1\",\"token_type\":\"Bearer\",\"expires_in\":3600,\"refresh_token\":\"r1\"}")
            .RespondWith("{\"access_token\":\"a2\",\"token_type\":\"Bearer\",\"expires_in\":3600,\"refresh_token\":\"r2\"}");

        var persisted = new List<string>();
        var now = DateTimeOffset.UtcNow;
        using var client = CreateClient();
        client.UtcNow = () => now;

        var source = client.CreateRefreshTokenSource(new SLTokenResponse("a0", now.AddHours(1), "r0"),
            (tokens, _) =>
            {
                Interlocked.Increment(ref issued);
                persisted.Add(tokens.RefreshToken);
                return Task.CompletedTask;
            });

        // Seeded with a valid token: no refresh is needed yet
        Assert.Equal("a0", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
        Assert.DoesNotContain(_http.CallLog, x => x.Method == HttpMethod.Post);

        // Many requests see the same token rejected: only one refresh may go out, or the rotated token would be replayed
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => source.GetAccessTokenAsync(new SLAccessTokenRequest(true, "a0"))));
        Assert.All(results, x => Assert.Equal("a1", x));

        now += TimeSpan.FromMinutes(1);
        var second = await source.GetAccessTokenAsync(new SLAccessTokenRequest(true, "a1"));
        Assert.Equal("a2", second);

        var forms = _http.CallLog.Where(x => x.Method == HttpMethod.Post).Select(x => ParseForm(x.RequestBody)).ToList();
        Assert.Equal(["r0", "r1"], forms.Select(x => x["refresh_token"]));
        Assert.Equal(["r1", "r2"], persisted);
        Assert.Equal(2, issued);
    }

    [Fact]
    public async Task RefreshTokenSource_ResponseWithoutIdToken_KeepsThePreviousOne()
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint)
            .RespondWith("{\"access_token\":\"a1\",\"token_type\":\"Bearer\",\"expires_in\":3600}")
            .RespondWith("{\"access_token\":\"a2\",\"token_type\":\"Bearer\",\"expires_in\":3600,\"id_token\":\"id-2\"}")
            .RespondWith("{\"access_token\":\"a3\",\"token_type\":\"Bearer\",\"expires_in\":3600}");

        var persisted = new List<string>();
        var now = DateTimeOffset.UtcNow;
        using var client = CreateClient();
        client.UtcNow = () => now;

        var source = client.CreateRefreshTokenSource(new SLTokenResponse("a0", now.AddHours(1), "r0", "id-0"),
            (tokens, _) =>
            {
                persisted.Add(tokens.IdToken);
                return Task.CompletedTask;
            });

        await source.GetAccessTokenAsync(new SLAccessTokenRequest(true, "a0"));
        now += TimeSpan.FromMinutes(1);
        await source.GetAccessTokenAsync(new SLAccessTokenRequest(true, "a1"));
        now += TimeSpan.FromMinutes(1);
        await source.GetAccessTokenAsync(new SLAccessTokenRequest(true, "a2"));

        Assert.Equal(["id-0", "id-2", "id-2"], persisted);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1800", true)]
    public async Task TokenResponse_RefreshExpiresIn_IsExposed(string refreshExpiresIn, bool expected)
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith($"{{\"access_token\":\"a1\",\"token_type\":\"Bearer\",\"refresh_token\":\"r1\",\"refresh_expires_in\":{refreshExpiresIn}}}");
        var now = DateTimeOffset.UtcNow;
        using var client = CreateClient();
        client.UtcNow = () => now;

        var response = await client.RefreshTokenAsync("r0");

        Assert.Equal(expected ? now.AddSeconds(1800) : null, response.RefreshExpiresAtUtc);
    }

    [Fact]
    public async Task RefreshTokenSource_RevokedRefreshToken_FailsTheRequestWithoutLogin()
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith("{\"error\":\"invalid_grant\"}", 400);
        _http.ForCallsTo("*/b1s/v2/Items").RespondWith("{\"error\":{\"message\":{\"value\":\"Invalid token\"}}}", 401);
        using var client = CreateClient();

        var connection = new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
            CompanyDB = "CompanyDB",
            UserName = $"manager-{Guid.NewGuid():N}",
            Password = "12345",
            AccessTokenSource = client.CreateRefreshTokenSource(new SLTokenResponse("a0", DateTimeOffset.UtcNow.AddHours(1), "r0")),
            CompanyId = "1875",
            HttpMessageHandler = _http,
            RetryDelay = TimeSpan.Zero
        });

        var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => connection.Request("Items").GetStringAsync());

        Assert.Equal("invalid_grant", exception.OAuthError);
        _http.ShouldNotHaveCalled("*/Login");
    }

    [Fact]
    public void RefreshTokenSource_WithoutRefreshToken_IsRejected()
    {
        using var client = CreateClient();

        Assert.Throws<ArgumentException>(() => client.CreateRefreshTokenSource(new SLTokenResponse("a0")));
    }

    [Fact]
    public async Task ClientCredentialsTokenSource_EndToEnd_DrivesAConnection()
    {
        RespondWithDiscovery();
        _http.ForCallsTo(TokenEndpoint).RespondWith(SuccessfulToken);
        _http.ForCallsTo("*/b1s/v2/Items").RespondWith("{}");
        using var client = CreateClient(SLClientCredential.FromPrivateKey(Convert.ToBase64String(_rsa.ExportPkcs8PrivateKey())));

        var connection = new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
            AccessTokenSource = client.CreateClientCredentialsTokenSource(),
            CompanyId = "1875",
            HttpMessageHandler = _http
        });

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => connection.Request("Items").GetStringAsync()));

        _http.ShouldHaveCalled(TokenEndpoint).Times(1);
        _http.ShouldHaveCalled("*/b1s/v2/Items").WithHeader("Authorization", "Bearer access-1").WithHeader("X-b1-companyid", "1875").WithoutHeader("Cookie").Times(10);
    }

    [Fact]
    public async Task CurrentUserInfo_ParsesBindings()
    {
        _http.ForCallsTo(UserInfoUrl)
            .RespondWithJson(new
            {
                d = new
                {
                    CurrentUserInfo = new
                    {
                        UserIdentity = "uid",
                        UserName = "b1ext_user",
                        HasB1UserBindings = true,
                        B1UserBindings = new
                        {
                            results = new object[]
                            {
                                new
                                {
                                    CompanyID = "1875", CompanySchemaName = "SBODEMOUS", CompanyDisplayName = "Demo", B1UserCode = "b1ext_user",
                                    ServiceUnitID = "su1", ServiceUnitName = "SU", DatabaseInstanceName = "db", Version = "10", Localization = "US", Confirmed = true, IsSuperuser = false
                                },
                                new { CompanyID = 42, CompanySchemaName = "OTHER", UserCode = "fallback" }
                            }
                        }
                    }
                }
            });

        using var client = CreateClient(clientId: null);

        var userInfo = await client.GetCurrentUserInfoAsync("access-1");

        Assert.Equal("uid", userInfo.UserIdentity);
        Assert.Equal("b1ext_user", userInfo.UserName);
        Assert.Equal(2, userInfo.CompanyBindings.Count);
        Assert.Contains("\"HasB1UserBindings\":true", userInfo.RawJson);

        var binding = userInfo.CompanyBindings[0];
        Assert.Equal("1875", binding.CompanyId);
        Assert.Equal("SBODEMOUS", binding.CompanySchemaName);
        Assert.Equal("Demo", binding.CompanyDisplayName);
        Assert.Equal("b1ext_user", binding.UserCode);
        Assert.Equal("su1", binding.ServiceUnitId);
        Assert.Equal("SU", binding.ServiceUnitName);
        Assert.Equal("db", binding.DatabaseInstanceName);
        Assert.Equal("10", binding.Version);
        Assert.Equal("US", binding.Localization);
        Assert.True(binding.IsConfirmed);
        Assert.False(binding.IsSuperuser);

        Assert.Equal("42", userInfo.CompanyBindings[1].CompanyId);
        Assert.Equal("fallback", userInfo.CompanyBindings[1].UserCode);

        _http.ShouldHaveCalled(UserInfoUrl)
            .WithVerb(HttpMethod.Get)
            .WithQueryParam("IncludeB1UserBinding", "true")
            .WithHeader("Authorization", "Bearer access-1")
            .WithHeader("Accept", "application/json")
            .Times(1);
    }

    private void RespondWithCompanies(params string[] schemas) =>
        _http.ForCallsTo(UserInfoUrl)
            .RespondWithJson(new
            {
                d = new { CurrentUserInfo = new { UserName = "user", B1UserBindings = new { results = schemas.Select((x, i) => new { CompanyID = (100 + i).ToString(), CompanySchemaName = x }).ToArray() } } }
            });

    [Fact]
    public async Task GetCompanyId_SelectsByCaseInsensitiveSchemaName()
    {
        RespondWithCompanies("OTHER", "SBODEMOUS");
        using var client = CreateClient();

        Assert.Equal("101", await client.GetCompanyIdAsync("access-1", "sbodemous"));
    }

    [Fact]
    public async Task GetCompanyId_NoMatchOrAmbiguous_FailsInsteadOfPickingOne()
    {
        RespondWithCompanies("SBODEMOUS", "SBODEMOUS");
        using var client = CreateClient();

        var ambiguous = await Assert.ThrowsAsync<SLAuthenticationException>(() => client.GetCompanyIdAsync("access-1", "SBODEMOUS"));
        var missing = await Assert.ThrowsAsync<SLAuthenticationException>(() => client.GetCompanyIdAsync("access-1", "MISSING"));

        Assert.Contains("More than one", ambiguous.Message);
        Assert.Contains("not bound", missing.Message);
    }

    [Fact]
    public async Task CurrentUserInfo_Rejected_CarriesStatusButNoToken()
    {
        _http.ForCallsTo(UserInfoUrl).RespondWith("{}", 401);
        using var client = CreateClient();

        var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => client.GetCurrentUserInfoAsync("very.secret-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.DoesNotContain("secret-token", exception.ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"d\":{}}")]
    [InlineData("<html/>")]
    public async Task CurrentUserInfo_InvalidResponse_IsRejected(string body)
    {
        _http.ForCallsTo(UserInfoUrl).RespondWith(body);
        using var client = CreateClient();

        await Assert.ThrowsAsync<SLAuthenticationException>(() => client.GetCurrentUserInfoAsync("access-1"));
    }

    [Fact]
    public async Task CurrentUserInfo_MalformedToken_IsNotSent()
    {
        using var client = CreateClient();

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetCurrentUserInfoAsync("a\r\nX-Injected: 1"));

        Assert.Empty(_http.CallLog);
    }

    [Fact]
    public void Options_InvalidCombinations_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new SLOidcClient(null));
        Assert.Throws<ArgumentException>(() => new SLOidcClient(new SLOidcClientOptions()));
        Assert.Throws<ArgumentException>(() => new SLOidcClient(new SLOidcClientOptions { SldAddress = new Uri("http://b1hana:40001") }));
        Assert.NotNull(new SLOidcClient(new SLOidcClientOptions { SldAddress = new Uri("http://b1hana:40001"), AllowInsecureHttp = true }));

        Assert.Throws<ArgumentException>(() => new SLOidcClient(new SLOidcClientOptions { SldAddress = new Uri(SldAddress), HttpMessageHandler = _http, HttpClient = new HttpClient() }));
        Assert.Throws<ArgumentException>(() => new SLOidcClient(new SLOidcClientOptions { SldAddress = new Uri(SldAddress), HttpMessageHandler = _http, ServerCertificateValidationCallback = (_, _, _, _) => true }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SLOidcClient(new SLOidcClientOptions { SldAddress = new Uri(SldAddress), RequestTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SLOidcClient(new SLOidcClientOptions { SldAddress = new Uri(SldAddress), ProviderMetadataLifetime = TimeSpan.MaxValue }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SLOidcClient(new SLOidcClientOptions { SldAddress = new Uri(SldAddress), ProviderMetadataLifetime = TimeSpan.FromSeconds(-1) }));
        Assert.NotNull(new SLOidcClient(new SLOidcClientOptions { SldAddress = new Uri(SldAddress), ProviderMetadataLifetime = TimeSpan.Zero }));
    }

    [Fact]
    public async Task CustomHttpClient_IsLeftUntouched()
    {
        RespondWithDiscovery();
        var httpClient = new HttpClient(_http, false);
        var client = new SLOidcClient(new SLOidcClientOptions { SldAddress = new Uri(SldAddress), HttpClient = httpClient });

        await client.GetProviderMetadataAsync();
        client.Dispose();

        // Would throw if it had been disposed by the client
        await httpClient.GetAsync(ProviderUrl);
        httpClient.Dispose();
    }

    [Fact]
    public async Task SlowServer_TimesOut()
    {
        _http.ForCallsTo(ProviderUrl).WithDelay(TimeSpan.FromSeconds(30)).RespondWith("{}");
        using var client = CreateClient(configure: x => x.RequestTimeout = TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<SLTimeoutException>(() => client.GetProviderMetadataAsync());
    }

    [Fact]
    public async Task CallerCancellation_WhileWaitingForDiscovery_IsHonored()
    {
        _http.ForCallsTo(ProviderUrl).WithDelay(TimeSpan.FromMilliseconds(500)).RespondWith("{}");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        using var client = CreateClient();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetProviderMetadataAsync(cancellation.Token));
    }
}
