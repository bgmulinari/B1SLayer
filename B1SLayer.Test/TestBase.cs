namespace B1SLayer.Test;

public abstract class TestBase : IDisposable
{
    public const string SessionCookieHeader = "B1SESSION=00000000-0000-0000-0000-000000000000; ROUTEID=.node0";

    public TestBase()
    {
        HttpTest = new MockHttp();

        // standard Login response, carrying session cookies so the cached-session request path is exercised
        HttpTest.ForCallsTo("*/b1s/v*/Login")
            .RespondWithJson(LoginResponse, cookies: new { B1SESSION = "00000000-0000-0000-0000-000000000000", ROUTEID = ".node0" });

        SLConnectionV1 = CreateConnection("v1");
        SLConnectionV2 = CreateConnection("v2");
    }

    protected MockHttp HttpTest { get; }
    protected SLConnection SLConnectionV1 { get; }
    protected SLConnection SLConnectionV2 { get; }
    protected static SLLoginResponse LoginResponse { get; } = new() { SessionId = "00000000-0000-0000-0000-000000000000", Version = "1000000", SessionTimeout = 30 };

    public void Dispose()
    {
        HttpTest.Dispose();
    }

    protected SLConnection GetConnection(string version) => version == "v2" ? SLConnectionV2 : SLConnectionV1;

    /// <summary>
    ///     Creates a connection bound to the given mock handler (this instance's <see cref="HttpTest" /> by default).
    ///     Each connection gets a unique username so the process-wide session cache can't leak sessions between tests.
    /// </summary>
    protected SLConnection CreateConnection(string version, MockHttp httpTest = null) =>
        new(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri($"https://sapserver:50000/b1s/{version}"),
            CompanyDB = "CompanyDB",
            UserName = $"manager-{Guid.NewGuid():N}",
            Password = "12345",
            HttpMessageHandler = httpTest ?? HttpTest
        });

    public static IEnumerable<string> Versions()
    {
        yield return "v1";
        yield return "v2";
    }
}
