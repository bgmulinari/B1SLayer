using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Serialization;

using Xunit.Abstractions;

namespace B1SLayer.Test;

/// <summary>
///     A <see cref="FactAttribute" /> that is skipped unless a real landscape is configured through environment variables.
/// </summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (LiveLandscape.Current == null)
        {
            Skip = $"Set {LiveLandscape.SldVariable}, {LiveLandscape.ServiceLayerVariable}, {LiveLandscape.ClientIdVariable}, {LiveLandscape.PrivateKeyVariable} and {LiveLandscape.CompanySchemaVariable} to run against a real landscape.";
        }
    }
}

/// <summary>
///     The real landscape to run against, a daemon client registered in the SAP Business One Extension Single Sign-On
///     Manager.
///     Set <see cref="InsecureTlsVariable" /> to 1 for a landscape with self-signed certificates.
/// </summary>
public sealed class LiveLandscape
{
    public const string SldVariable = "B1SLAYER_LIVE_SLD";
    public const string ServiceLayerVariable = "B1SLAYER_LIVE_SERVICE_LAYER";
    public const string ClientIdVariable = "B1SLAYER_LIVE_CLIENT_ID";
    public const string PrivateKeyVariable = "B1SLAYER_LIVE_PRIVATE_KEY";
    public const string CompanySchemaVariable = "B1SLAYER_LIVE_COMPANY_SCHEMA";
    public const string InsecureTlsVariable = "B1SLAYER_LIVE_INSECURE_TLS";

    private LiveLandscape()
    {
    }

    public static LiveLandscape Current { get; } = Load();

    public Uri Sld { get; private init; }
    public Uri ServiceLayer { get; private init; }
    public string ClientId { get; private init; }
    public string PrivateKey { get; private init; }
    public string CompanySchema { get; private init; }
    public bool InsecureTls { get; private init; }

    private static LiveLandscape Load()
    {
        var sld = Environment.GetEnvironmentVariable(SldVariable);
        var serviceLayer = Environment.GetEnvironmentVariable(ServiceLayerVariable);
        var clientId = Environment.GetEnvironmentVariable(ClientIdVariable);
        var privateKey = Environment.GetEnvironmentVariable(PrivateKeyVariable);
        var companySchema = Environment.GetEnvironmentVariable(CompanySchemaVariable);

        if (new[] { sld, serviceLayer, clientId, privateKey, companySchema }.Any(string.IsNullOrEmpty))
        {
            return null;
        }

        return new LiveLandscape
        {
            Sld = new Uri(sld),
            ServiceLayer = new Uri(serviceLayer),
            ClientId = clientId,
            PrivateKey = privateKey,
            CompanySchema = companySchema,
            InsecureTls = Environment.GetEnvironmentVariable(InsecureTlsVariable) == "1"
        };
    }
}

/// <summary>
///     End-to-end checks against a real landscape, skipped by default. They only ever read from SAP.
/// </summary>
public class SLLiveOAuthTests(ITestOutputHelper output)
{
    private static readonly LiveLandscape Landscape = LiveLandscape.Current;

    private static SLOidcClient CreateOidcClient() =>
        new(new SLOidcClientOptions
        {
            SldAddress = Landscape.Sld,
            ClientId = Landscape.ClientId,
            ClientCredential = SLClientCredential.FromPrivateKey(Landscape.PrivateKey),
            ValidateServerCertificate = !Landscape.InsecureTls
        });

    private static SLConnection CreateConnection(Func<CancellationToken, Task<SLRequestIdentity>> resolver, HeaderRecordingHandler recorder = null) =>
        new(new SLConnectionOptions
        {
            ServiceLayerRoot = Landscape.ServiceLayer,
            IdentityResolver = resolver,
            ValidateAccessTokenServerCertificate = !Landscape.InsecureTls,
            HttpMessageHandler = recorder,
            RetryDelay = TimeSpan.Zero
        });

    private static HeaderRecordingHandler CreateRecorder() =>
        new(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = Landscape.InsecureTls ? (_, _, _, _) => true : null
        });

    private static string Tamper(string token) => token.Substring(0, token.Length - 8) + "AAAAAAAA";

    [LiveFact]
    public async Task Daemon_DiscoveryTokenCompanyAndReads_WorkThroughThePublicApi()
    {
        using var client = CreateOidcClient();

        var metadata = await client.GetProviderMetadataAsync();
        Assert.StartsWith(metadata.Issuer, metadata.TokenEndpoint.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        output.WriteLine($"Issuer validated: {metadata.Issuer}");

        var source = client.CreateClientCredentialsTokenSource();
        var token = await source.GetAccessTokenAsync(SLAccessTokenRequest.Default);
        Assert.Equal(token, await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
        output.WriteLine("Token acquired through private_key_jwt, and served from the cache the second time");

        var companyId = await client.GetCompanyIdAsync(token, Landscape.CompanySchema);
        Assert.False(string.IsNullOrEmpty(companyId));
        output.WriteLine($"Company ID: {companyId}");

        var recorder = CreateRecorder();
        var connection = CreateConnection(_ => Task.FromResult(SLRequestIdentity.FromTokenSource(source, companyId)), recorder);

        var items = await connection.Request("Items").Select("ItemCode").Top(2).GetAsync<List<LiveItem>>();
        Assert.Equal(2, items.Count);
        Assert.All(items, x => Assert.False(string.IsNullOrEmpty(x.ItemCode)));
        output.WriteLine($"GET Items?$select=ItemCode&$top=2 returned {items.Count} items");

        var count = await connection.Request("Items").GetCountAsync();
        Assert.True(count >= 2);
        output.WriteLine($"GET Items/$count returned {count}");

        var metadataDocument = await connection.Request("$metadata").GetStringAsync();
        Assert.Contains("Items", metadataDocument);
        output.WriteLine($"GET $metadata returned {metadataDocument.Length} characters");

        await using (var stream = await connection.Request("Items").Select("ItemCode").Top(1).GetStreamAsync())
        {
            using var reader = new StreamReader(stream);
            Assert.Contains("ItemCode", await reader.ReadToEndAsync());
        }

        var batch = await connection.PostBatchAsync(new SLBatchRequest(HttpMethod.Get, "Items?$select=ItemCode&$top=2"));
        var batchResponse = Assert.Single(batch);
        Assert.Equal(HttpStatusCode.OK, batchResponse.StatusCode);
        Assert.Contains("ItemCode", await batchResponse.Content.ReadAsStringAsync());
        output.WriteLine("$batch GET returned one successful inner response");

        // On the wire, as received by the transport: a token and a company on every request, a cookie on none,
        // even though the Service Layer sets a ROUTEID cookie on its responses
        Assert.All(recorder.Requests,
            request =>
            {
                Assert.True(request.HasBearer);
                Assert.Equal(companyId, request.CompanyId);
                Assert.False(request.HasCookie);
            });

        Assert.DoesNotContain(recorder.Requests, x => x.Uri.AbsolutePath.EndsWith("/Login"));
        output.WriteLine($"{recorder.Requests.Count} requests on the wire, all with Bearer and company and none with a cookie; responses setting cookies: {recorder.ResponsesSettingCookies}");
    }

    [LiveFact]
    public async Task PerRequestSwitching_RunsEachRequestWithItsOwnIdentity()
    {
        using var client = CreateOidcClient();
        var source = client.CreateClientCredentialsTokenSource();
        var token = await source.GetAccessTokenAsync(SLAccessTokenRequest.Default);
        var companyId = await client.GetCompanyIdAsync(token, Landscape.CompanySchema);

        var valid = SLRequestIdentity.FromTokenSource(source, companyId);

        // The only other identity available for a read-only check is an invalid one, which is enough to prove the decision is
        // per request, as this identity failing while the others succeed can only come from what each request carried
        var rejected = SLRequestIdentity.FromTokenSource(SLAccessTokenSource.FromDelegate((_, _) => Task.FromResult(Tamper(token))), companyId);

        var ambient = new AsyncLocal<SLRequestIdentity>();
        var recorder = CreateRecorder();
        var connection = CreateConnection(_ => Task.FromResult(ambient.Value ?? SLRequestIdentity.Session), recorder);

        ambient.Value = valid;
        Assert.Contains("ItemCode", await connection.Request("Items").Select("ItemCode").Top(1).GetStringAsync());

        ambient.Value = rejected;
        var exception = await Assert.ThrowsAsync<SLException>(() => connection.Request("Items").Select("ItemCode").Top(1).GetStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        output.WriteLine($"Tampered token: {(int)exception.StatusCode} code {exception.ErrorDetails?.Code}, with no login attempt");

        ambient.Value = valid;
        Assert.Contains("ItemCode", await connection.Request("Items").Select("ItemCode").Top(1).GetStringAsync());

        // This connection has no session account, so the session identity fails before anything goes out
        ambient.Value = null;
        var requestsSoFar = recorder.Requests.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.Request("Items").GetStringAsync());
        Assert.Equal(requestsSoFar, recorder.Requests.Count);

        Assert.DoesNotContain(recorder.Requests, x => x.Uri.AbsolutePath.EndsWith("/Login") || x.HasCookie);
        output.WriteLine("Switched valid, tampered and valid identities per request on one connection");
    }

    [LiveFact]
    public async Task UnauthorizedToken_IsRefreshedOnce_AndTheRequestSucceeds()
    {
        using var client = CreateOidcClient();
        var source = client.CreateClientCredentialsTokenSource();
        var token = await source.GetAccessTokenAsync(SLAccessTokenRequest.Default);
        var companyId = await client.GetCompanyIdAsync(token, Landscape.CompanySchema);

        var requests = new ConcurrentQueue<SLAccessTokenRequest>();
        var refreshingSource = SLAccessTokenSource.FromDelegate(async (request, cancellationToken) =>
        {
            requests.Enqueue(request);
            return request.ForceRefresh ? await source.GetAccessTokenAsync(new SLAccessTokenRequest(true), cancellationToken) : Tamper(token);
        });

        var recorder = CreateRecorder();
        var connection = CreateConnection(_ => Task.FromResult(SLRequestIdentity.FromTokenSource(refreshingSource, companyId)), recorder);

        Assert.Contains("ItemCode", await connection.Request("Items").Select("ItemCode").Top(1).GetStringAsync());

        Assert.Equal(2, recorder.Requests.Count);
        Assert.Equal([false, true], requests.Select(x => x.ForceRefresh));
        output.WriteLine("A rejected token was replaced through the token source and the request succeeded on the retry");
    }

    [LiveFact]
    public async Task UnknownCompany_IsUnauthorized_WithoutCookieOrLogin()
    {
        using var client = CreateOidcClient();
        var source = client.CreateClientCredentialsTokenSource();

        // The company is part of the identity on the Service Layer, so a wrong one is a 401. The token was
        // only just acquired, so the source doesn't replace it, and the request isn't sent again
        var recorder = CreateRecorder();
        var connection = CreateConnection(_ => Task.FromResult(SLRequestIdentity.FromTokenSource(source, "0")), recorder);

        var exception = await Assert.ThrowsAsync<SLException>(() => connection.Request("Items").Top(1).GetStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Single(recorder.Requests);
        Assert.DoesNotContain(recorder.Requests, x => x.HasCookie || x.Uri.AbsolutePath.EndsWith("/Login"));
        output.WriteLine($"Unknown company: {(int)exception.StatusCode} after {recorder.Requests.Count} attempt");
    }

    [LiveFact]
    public async Task DefaultTransport_RejectsSelfSignedCertificates()
    {
        if (!Landscape.InsecureTls)
        {
            return;
        }

        using var secureClient = new SLOidcClient(new SLOidcClientOptions { SldAddress = Landscape.Sld });
        var discoveryFailure = await Assert.ThrowsAsync<SLAuthenticationException>(() => secureClient.GetProviderMetadataAsync());
        Assert.IsType<HttpRequestException>(discoveryFailure.InnerException);

        var secureConnection = new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = Landscape.ServiceLayer,
            AccessTokenSource = SLAccessTokenSource.FromDelegate((_, _) => Task.FromResult("never-sent")),
            CompanyId = "1"
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => secureConnection.Request("Items").Top(1).GetStringAsync());
        output.WriteLine("With the default options, the self-signed certificates of the SLD and the Service Layer are rejected");
    }

    private sealed class LiveItem
    {
        [JsonPropertyName("ItemCode")]
        public string ItemCode { get; set; }
    }

    private sealed record WireRequest(
        Uri Uri,
        bool HasBearer,
        bool HasCookie,
        string CompanyId);

    /// <summary>
    ///     Records what each request carries on the wire, without ever keeping a credential.
    /// </summary>
    private sealed class HeaderRecordingHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
    {
        private int _responsesSettingCookies;

        public ConcurrentQueue<WireRequest> Requests { get; } = new();

        public int ResponsesSettingCookies => _responsesSettingCookies;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(new WireRequest(
                request.RequestUri,
                request.Headers.Authorization?.Scheme == "Bearer",
                request.Headers.Contains("Cookie"),
                request.Headers.TryGetValues("X-b1-companyid", out var values) ? values.Single() : null));

            var response = await base.SendAsync(request, cancellationToken);

            if (response.Headers.Contains("Set-Cookie"))
            {
                Interlocked.Increment(ref _responsesSettingCookies);
            }

            return response;
        }
    }
}
