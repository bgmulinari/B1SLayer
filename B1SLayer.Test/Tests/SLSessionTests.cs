using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Caching.Distributed;

namespace B1SLayer.Test;

public class SLSessionTests : TestBase
{
    private const string UnauthorizedResponse = "{\"error\":{\"message\":{\"value\":\"Invalid session\"}}}";

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task ForcedRelogin_WhenSessionAlreadyRefreshed_SkipsRedundantLogin(string version)
    {
        var httpTest = new MockHttp();
        httpTest.ForCallsTo("*/b1s/v*/Login").RespondWithJson(LoginResponse, cookies: new { B1SESSION = "stale" });
        httpTest.ForCallsTo("*/b1s/v*/Orders").RespondWith(UnauthorizedResponse, 401).RespondWith("{}");

        var slConnection = CreateConnection(version, httpTest);
        slConnection.RetryDelay = TimeSpan.Zero;

        var refreshedJar = new SLCookieJar();
        refreshedJar.AddOrReplace(new SLCookie { Name = "B1SESSION", Value = "refreshed" });
        var refreshedValue = refreshedJar.Serialize();

        // Simulates another caller refreshing the session between the failed request and the forced re-login
        slConnection.OnError(_ => slConnection.DistributedCache.SetString(slConnection.SessionCacheKey, refreshedValue));

        await slConnection.Request("Orders").GetStringAsync();

        // The initial automatic login is the only login performed; the forced re-login detects the
        // refreshed session and is skipped, and the retry carries the refreshed cookies
        httpTest.ShouldHaveCalled("*/b1s/v*/Login").WithVerb(HttpMethod.Post).Times(1);
        httpTest.ShouldHaveCalled("*/b1s/v*/Orders").WithHeader("Cookie", "B1SESSION=refreshed").Times(1);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task ForcedRelogin_TransientFailure_IsRetriedAndAggregated(string version)
    {
        var httpTest = new MockHttp();
        httpTest.ForCallsTo("*/b1s/v*/Login")
            .RespondWithJson(LoginResponse, cookies: new { B1SESSION = "session1" })
            .RespondWith("{\"error\":{\"message\":{\"value\":\"Bad gateway\"}}}", 502)
            .RespondWithJson(LoginResponse, cookies: new { B1SESSION = "session2" });
        httpTest.ForCallsTo("*/b1s/v*/Orders").RespondWith(UnauthorizedResponse, 401);

        var slConnection = CreateConnection(version, httpTest);
        slConnection.RetryDelay = TimeSpan.Zero;
        slConnection.NumberOfAttempts = 2;

        // The transient 502 from the forced re-login must participate in the retry policy and end up
        // aggregated with the 401s instead of escaping raw and discarding the collected context
        var exception = await Assert.ThrowsAsync<AggregateException>(() => slConnection.Request("Orders").GetStringAsync());

        Assert.Contains(exception.InnerExceptions, x => x is SLException { StatusCode: HttpStatusCode.Unauthorized });
        Assert.Contains(exception.InnerExceptions, x => x is SLException { StatusCode: HttpStatusCode.BadGateway });
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task SsoLogin_CookieValueContainingPathSubstring_IsPreserved(string version)
    {
        var httpTest = new MockHttp();
        httpTest.RespondWith("{}");

        var slConnection = new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri($"https://sapserver:50000/b1s/{version}"),
            GetServiceLayerConnectionContext = _ => "B1SESSION=abc123,ROUTEID=.node0,CompanyDB=sbo_pathfinder,path=/b1s/v1",
            HttpMessageHandler = httpTest
        });

        await slConnection.Request("Orders").GetStringAsync();

        // The CompanyDB cookie survives even though its value contains 'path', while the path attribute
        // itself is not treated as a cookie; rendering orders cookies by longest path first
        httpTest.ShouldHaveCalled("*/b1s/v*/Orders")
            .WithHeader("Cookie", "B1SESSION=abc123; CompanyDB=sbo_pathfinder; ROUTEID=.node0")
            .Times(1);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task ConcurrentColdStart_PerformsSingleLogin(string version)
    {
        var httpTest = new MockHttp();
        httpTest.ForCallsTo("*/b1s/v*/Login")
            .WithDelay(TimeSpan.FromMilliseconds(100))
            .RespondWithJson(LoginResponse, cookies: new { B1SESSION = "session" });
        httpTest.RespondWith("{}");
        var slConnection = CreateConnection(version, httpTest);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => slConnection.Request("Orders").GetStringAsync()));

        // The delayed login keeps all cold-start requests in flight together: they serialize on the
        // login semaphore and every caller after the first detects the refreshed session and skips
        httpTest.ShouldHaveCalled("*/b1s/v*/Login").Times(1);
        httpTest.ShouldHaveCalled("*/b1s/v*/Orders").Times(5);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task Login_SuccessStatusWithNonJsonBody_ThrowsTypedSLException(string version)
    {
        var httpTest = new MockHttp();
        httpTest.ForCallsTo("*/b1s/v*/Login").RespondWith("<html>maintenance</html>");
        var slConnection = CreateConnection(version, httpTest);

        var exception = await Assert.ThrowsAsync<SLException>(() => slConnection.Request("Orders").GetStringAsync());

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Contains("<html>", exception.ResponseContent);
        Assert.IsType<JsonException>(exception.InnerException);
    }
}
