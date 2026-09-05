using System.Net;

namespace B1SLayer.Test;

public class SLRedirectTests : TestBase
{
    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task Login_RedirectHop_CookiesFromEveryHopAreCaptured(string version)
    {
        var httpTest = new MockHttp();
        httpTest.ForCallsTo("*/b1s/v*/Login")
            .RespondWith("",
                307,
                new Dictionary<string, string>
                {
                    { "Location", $"https://sapserver:50000/b1s/{version}/Login" },
                    { "Set-Cookie", "ROUTEID=.node1; path=/" }
                })
            .RespondWithJson(LoginResponse, cookies: new { B1SESSION = "00000000-0000-0000-0000-000000000000" });
        httpTest.RespondWith("{}");

        var slConnection = CreateConnection(version, httpTest);

        await slConnection.Request("Orders").GetStringAsync();

        // The 307 hop preserves the POST verb and the login body
        httpTest.ShouldHaveCalled("*/b1s/v*/Login")
            .WithVerb(HttpMethod.Post)
            .WithRequestBody("*CompanyDB*")
            .Times(2);

        // The affinity cookie set on the intermediate hop is captured alongside the final session cookie
        httpTest.ShouldHaveCalled("*/b1s/v*/Orders")
            .WithHeader("Cookie", "B1SESSION=00000000-0000-0000-0000-000000000000; ROUTEID=.node1")
            .Times(1);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task Redirect302OnPost_ConvertsToGetWithoutBody(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.ForCallsTo("*/b1s/v*/Orders").RespondWith("", 302, new Dictionary<string, string> { { "Location", "Redirected" } });
        HttpTest.ForCallsTo("*/b1s/v*/Redirected").RespondWith("{}");

        await slConnection.Request("Orders").PostReceiveStringAsync(new { DocEntry = 1 });

        // A 302 on a POST converts the redirected request to a bodiless GET (browser/HttpClient semantics),
        // with the relative Location resolved against the original URL
        HttpTest.ShouldHaveCalled("*/b1s/v*/Redirected")
            .WithVerb(HttpMethod.Get)
            .With(call => call.RequestBody == null)
            .Times(1);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task Redirect_UserSetCookieHeader_IsNotForwardedRaw(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.ForCallsTo("*/b1s/v*/Orders").RespondWith("", 307, new Dictionary<string, string> { { "Location", "Redirected" } });
        HttpTest.ForCallsTo("*/b1s/v*/Redirected").RespondWith("{}");

        await slConnection.Request("Orders")
            .WithHeader("Cookie", "EXTRA=1")
            .GetStringAsync();

        // The original request carries the user cookie merged with the session cookies, but the hop
        // re-derives its Cookie header from the jar alone — the raw header is never blindly forwarded
        HttpTest.ShouldHaveCalled("*/b1s/v*/Orders")
            .WithHeader("Cookie", $"EXTRA=1; {SessionCookieHeader}")
            .Times(1);

        HttpTest.ShouldHaveCalled("*/b1s/v*/Redirected")
            .WithHeader("Cookie", SessionCookieHeader)
            .Times(1);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task Redirect_HttpsToHttpDowngrade_IsNotFollowed(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.ForCallsTo("*/b1s/v*/Orders").RespondWith("", 302, new Dictionary<string, string> { { "Location", "http://sapserver:50001/b1s/v1/Orders" } });

        // The downgrade would leak the session cookies over an insecure channel, so the redirect is not
        // followed and the 302 surfaces as the final (unsuccessful) response
        var exception = await Assert.ThrowsAsync<SLException>(() => slConnection.Request("Orders").GetStringAsync());

        Assert.Equal(HttpStatusCode.Found, exception.StatusCode);
        HttpTest.ShouldHaveCalled("*/b1s/v*/Orders").Times(1);
        HttpTest.ShouldNotHaveCalled("http://*");
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task Redirect_LoopBeyondMaxRedirects_StopsFollowing(string version)
    {
        var slConnection = GetConnection(version);

        // The sticky-last mock response redirects to itself indefinitely
        HttpTest.ForCallsTo("*/b1s/v*/Loop").RespondWith("", 302, new Dictionary<string, string> { { "Location", "Loop" } });

        var exception = await Assert.ThrowsAsync<SLException>(() => slConnection.Request("Loop").GetStringAsync());

        Assert.Equal(HttpStatusCode.Found, exception.StatusCode);

        // The original request plus at most 10 followed redirects
        HttpTest.ShouldHaveCalled("*/b1s/v*/Loop").Times(11);
    }
}
