namespace B1SLayer.Test;

public class SLCookieJarTests
{
    private static readonly Uri LoginUri = new("https://sapserver:50000/b1s/v1/Login");

    private static SLCookieJar CreateJar(params string[] setCookieHeaders)
    {
        var jar = new SLCookieJar();
        jar.MergeSetCookieHeaders(setCookieHeaders, LoginUri);
        return jar;
    }

    [Fact]
    public void GetCookieHeaderFor_SecureCookie_IsNotSentOverHttp()
    {
        var jar = CreateJar("B1SESSION=abc; Secure");

        Assert.Equal("B1SESSION=abc", jar.GetCookieHeaderFor(new Uri("https://sapserver:50000/b1s/v1/Orders")));
        Assert.Null(jar.GetCookieHeaderFor(new Uri("http://sapserver:50000/b1s/v1/Orders")));
    }

    [Fact]
    public void GetCookieHeaderFor_HostOnlyCookie_IsOnlySentToOriginHost()
    {
        var jar = CreateJar("B1SESSION=abc");

        Assert.Equal("B1SESSION=abc", jar.GetCookieHeaderFor(new Uri("https://sapserver:50000/b1s/v1/Orders")));
        Assert.Null(jar.GetCookieHeaderFor(new Uri("https://othernode:50000/b1s/v1/Orders")));
    }

    [Fact]
    public void GetCookieHeaderFor_DomainCookie_MatchesSubdomains()
    {
        var jar = CreateJar("B1SESSION=abc; Domain=.example.com");

        Assert.Equal("B1SESSION=abc", jar.GetCookieHeaderFor(new Uri("https://node1.example.com/b1s/v1/Orders")));
        Assert.Equal("B1SESSION=abc", jar.GetCookieHeaderFor(new Uri("https://example.com/b1s/v1/Orders")));
        Assert.Null(jar.GetCookieHeaderFor(new Uri("https://example.org/b1s/v1/Orders")));
    }

    [Fact]
    public void GetCookieHeaderFor_DefaultPath_IsComputedFromOrigin()
    {
        // No path attribute: the RFC 6265 default-path of the Login origin is /b1s/v1
        var jar = CreateJar("B1SESSION=abc");

        Assert.Equal("B1SESSION=abc", jar.GetCookieHeaderFor(new Uri("https://sapserver:50000/b1s/v1/Orders")));
        Assert.Equal("B1SESSION=abc", jar.GetCookieHeaderFor(new Uri("https://sapserver:50000/b1s/v1")));
        Assert.Null(jar.GetCookieHeaderFor(new Uri("https://sapserver:50000/other")));
        Assert.Null(jar.GetCookieHeaderFor(new Uri("https://sapserver:50000/b1s/v2/Orders")));
    }

    [Fact]
    public void GetCookieHeaderFor_CookiesAreOrderedByLongestPathFirst()
    {
        var jar = CreateJar("ROUTEID=.node0; path=/", "B1SESSION=abc; path=/b1s/v1");

        Assert.Equal("B1SESSION=abc; ROUTEID=.node0", jar.GetCookieHeaderFor(new Uri("https://sapserver:50000/b1s/v1/Orders")));
    }

    [Fact]
    public void MergeSetCookieHeaders_MaxAgeZero_RemovesExistingCookie()
    {
        var jar = CreateJar("ROUTEID=.node0; path=/");

        jar.MergeSetCookieHeaders(["ROUTEID=deleted; path=/; Max-Age=0"], LoginUri);

        Assert.Null(jar.Find("ROUTEID"));
        Assert.Null(jar.GetCookieHeaderFor(new Uri("https://sapserver:50000/b1s/v1/Orders")));
    }

    [Fact]
    public void MergeSetCookieHeaders_ExpiredCookie_IsNotSent()
    {
        var jar = CreateJar("B1SESSION=abc; Expires=Wed, 01 Jan 2020 00:00:00 GMT");

        Assert.Null(jar.GetCookieHeaderFor(new Uri("https://sapserver:50000/b1s/v1/Orders")));
    }

    [Fact]
    public void Serialize_RoundTrip_PreservesMatchingRules()
    {
        var jar = CreateJar("B1SESSION=abc; Secure", "ROUTEID=.node0; path=/");

        var roundTripped = SLCookieJar.Deserialize(jar.Serialize());

        Assert.Equal("B1SESSION=abc; ROUTEID=.node0", roundTripped.GetCookieHeaderFor(new Uri("https://sapserver:50000/b1s/v1/Orders")));
        Assert.Null(roundTripped.GetCookieHeaderFor(new Uri("https://othernode:50000/b1s/v1/Orders")));
    }
}
