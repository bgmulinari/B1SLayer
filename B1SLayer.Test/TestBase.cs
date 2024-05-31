using Flurl.Http.Testing;

namespace B1SLayer.Test;

public abstract class TestBase : IDisposable
{
    protected HttpTest HttpTest { get; private set; }
    protected static SLConnection SLConnectionV1 { get; } = new SLConnection("https://sapserver:50000/b1s/v1", "CompanyDB", "manager", "12345");
    protected static SLConnection SLConnectionV2 { get; } = new SLConnection("https://sapserver:50000/b1s/v2", "CompanyDB", "manager", "12345");
    protected static SLLoginResponse LoginResponse { get; } = new() { SessionId = "00000000-0000-0000-0000-000000000000", Version = "1000000", SessionTimeout = 30 };

    public TestBase()
    {
        HttpTest = new HttpTest();

        // standard Login response
        HttpTest.ForCallsTo("*/b1s/v*/Login").RespondWithJson(LoginResponse);
    }

    public static IEnumerable<object[]> SLConnections()
    {
        yield return new SLConnection[] { SLConnectionV1 };
        yield return new SLConnection[] { SLConnectionV2 };
    }

    public void Dispose()
    {
        HttpTest.Dispose();
    }
}
