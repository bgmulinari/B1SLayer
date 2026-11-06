using System.Net;

namespace B1SLayer.Test;

public class SLAmbientSessionTests : TestBase
{
    public static TheoryData<string, Action<SLConnectionOptions>> ConflictingOptions => new()
    {
        { "CompanyDB", x => x.CompanyDB = "SBODEMOUS" },
        { "UserName", x => x.UserName = "manager" },
        { "Password", x => x.Password = "12345" },
        { "CompanyId", x => x.CompanyId = "1875" },
        { "AccessTokenSource", x => x.AccessTokenSource = SLAccessTokenSource.FromDelegate((_, _) => Task.FromResult("token")) },
        { "IdentityResolver", x => x.IdentityResolver = _ => Task.FromResult(SLRequestIdentity.Session) },
        { "SSO", x => x.GetServiceLayerConnectionContext = _ => "B1SESSION=abc" }
    };

    private SLConnection CreateAmbientConnection(Action<SLConnectionOptions> configure = null)
    {
        var options = new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://webclient:8443/b1s/v2"),
            UseAmbientSession = true,
            HttpMessageHandler = HttpTest,
            RetryDelay = TimeSpan.Zero
        };

        configure?.Invoke(options);
        return new SLConnection(options);
    }

    [Fact]
    public async Task Request_SendsNoCredential_AndNeverLogsIn()
    {
        HttpTest.ForCallsTo("*/b1s/v2/Items").RespondWith("{\"value\":[]}");
        var connection = CreateAmbientConnection();
        SLRequestIdentity identity = null;
        connection.BeforeCall(context => identity = context.Identity);

        await connection.Request("Items").GetStringAsync();

        HttpTest.ShouldHaveCalled("*/b1s/v2/Items").WithoutHeader("Cookie").WithoutHeader("Authorization").WithoutHeader("X-b1-companyid").Times(1);
        HttpTest.ShouldNotHaveCalled("*/Login");
        Assert.True(identity.IsAmbientSession);
        Assert.False(identity.IsSession);
        Assert.True(connection.IsUsingAmbientSession);
        Assert.False(connection.HasSessionCredentials);
        Assert.False(connection.IsUsingAccessTokens);
    }

    [Fact]
    public void CertificateValidationCallback_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => CreateAmbientConnection(options => options.ServerCertificateValidationCallback = (_, _, _, _) => true));
        Assert.Throws<ArgumentException>(() => CreateAmbientConnection(options => options.AccessTokenServerCertificateValidationCallback = (_, _, _, _) => true));
    }

    [Fact]
    public async Task DefaultCookieHeader_FailsBeforeSending()
    {
        using var httpClient = new HttpClient(HttpTest, false);
        httpClient.DefaultRequestHeaders.Add("Cookie", "B1SESSION=other");

        var connection = CreateAmbientConnection(options =>
        {
            options.HttpMessageHandler = null;
            options.HttpClient = httpClient;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.Request("Items").GetStringAsync());

        Assert.Empty(HttpTest.CallLog);
    }

    [Fact]
    public void ProvidedHandler_KeepsTheCookiesThatHoldTheSession()
    {
        using var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };

        _ = CreateAmbientConnection(options => options.HttpMessageHandler = handler);

        Assert.True(handler.UseCookies);
    }

    [Fact]
    public async Task Request_DoesNotAskTheBrowserToOmitItsCookies()
    {
        HttpTest.ForCallsTo("*/b1s/v2/Items").RespondWith("{}");
        var connection = CreateAmbientConnection();
        var hasFetchOptions = true;
        connection.AfterCall(context => hasFetchOptions = context.RequestMessage.Options.TryGetValue(new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"), out _));

        await connection.Request("Items").GetStringAsync();

        Assert.False(hasFetchOptions);
    }

    [Fact]
    public async Task Unauthorized_SurfacesWithoutRetryOrLogin()
    {
        HttpTest.ForCallsTo("*/b1s/v2/Items").RespondWith("{\"error\":{\"code\":\"301\",\"message\":{\"value\":\"Invalid session\"}}}", 401);
        var connection = CreateAmbientConnection();

        var exception = await Assert.ThrowsAsync<SLException>(() => connection.Request("Items").GetStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        HttpTest.ShouldHaveCalled("*/b1s/v2/Items").Times(1);
        HttpTest.ShouldNotHaveCalled("*/Login");
    }

    [Fact]
    public async Task OtherRetryableStatuses_AreStillRetried()
    {
        HttpTest.ForCallsTo("*/b1s/v2/Items").RespondWith("bad gateway", 502).RespondWith("{}");
        var connection = CreateAmbientConnection();

        await connection.Request("Items").GetStringAsync();

        HttpTest.ShouldHaveCalled("*/b1s/v2/Items").Times(2);
    }

    [Fact]
    public async Task LoginAndLogout_AreRejected()
    {
        var connection = CreateAmbientConnection();

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.LoginAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.LogoutAsync());

        Assert.Empty(HttpTest.CallLog);
    }

    [Fact]
    public async Task BearerToken_FailsTheRequest()
    {
        var connection = CreateAmbientConnection();

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.Request("Items").WithHeader("Authorization", "Bearer some.token").GetStringAsync());

        Assert.Empty(HttpTest.CallLog);
    }

    [Fact]
    public async Task OtherAuthorizationSchemes_AreLeftAlone()
    {
        HttpTest.ForCallsTo("*/b1s/v2/Items").RespondWith("{}");
        var connection = CreateAmbientConnection();

        await connection.Request("Items").WithHeader("Authorization", "Basic abc").GetStringAsync();

        HttpTest.ShouldHaveCalled("*/b1s/v2/Items").WithHeader("Authorization", "Basic abc");
    }

    [Fact]
    public async Task CookieHeader_IsDropped()
    {
        HttpTest.ForCallsTo("*/b1s/v2/Items").RespondWith("{}");
        var connection = CreateAmbientConnection();

        await connection.Request("Items").WithHeader("Cookie", "B1SESSION=other").GetStringAsync();

        HttpTest.ShouldHaveCalled("*/b1s/v2/Items").WithoutHeader("Cookie");
    }

    [Fact]
    public async Task ResponseCookies_AreNotCaptured()
    {
        HttpTest.ForCallsTo("*/b1s/v2/Items").RespondWith("{}", headers: new Dictionary<string, string> { ["Set-Cookie"] = "ROUTEID=.node1; path=/" });
        var connection = CreateAmbientConnection();
        SLCookieJar cookies = null;
        connection.AfterCall(context => cookies = context.ResponseCookies);

        await connection.Request("Items").GetStringAsync();

        Assert.Null(cookies);
    }

    [Fact]
    public async Task Batch_DropsCredentialHeadersOfItsInnerRequests()
    {
        HttpTest.RespondWith("--b\r\n\r\nHTTP/1.1 200 OK\r\n\r\n{}\r\n--b--");
        var connection = CreateAmbientConnection();

        var request = new SLBatchRequest(HttpMethod.Get, "Items");
        request.Headers.Add("Authorization", "Bearer other.token");
        request.Headers.Add("Cookie", "B1SESSION=other");

        await connection.PostBatchAsync(request);

        var call = HttpTest.CallLog.Single();
        Assert.DoesNotContain("other.token", call.RequestBody);
        Assert.DoesNotContain("B1SESSION", call.RequestBody);
    }

    [Theory]
    [MemberData(nameof(ConflictingOptions))]
    public void ConflictingCredential_IsRejected(string name, Action<SLConnectionOptions> configure)
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateAmbientConnection(configure));

        Assert.Contains(nameof(SLConnectionOptions.UseAmbientSession), exception.Message);
        Assert.NotNull(name);
    }

    [Fact]
    public void RelativeRoot_IsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateAmbientConnection(x => x.ServiceLayerRoot = new Uri("/b1s/v2", UriKind.Relative)));

        Assert.Contains(nameof(SLConnectionOptions.ServiceLayerRoot), exception.Message);
    }

    [Fact]
    public void OutsideABrowser_RequiresAProvidedTransport()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://webclient:8443/b1s/v2"),
            UseAmbientSession = true
        }));

        Assert.Contains("browser", exception.Message);
    }
}
