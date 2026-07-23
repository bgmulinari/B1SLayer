namespace B1SLayer.Test;

public class SLCallEventTests : TestBase
{
    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task RetriedRequest_ReportsAttemptNumbersAndParsedErrorToHandlers(string version)
    {
        var httpTest = new MockHttp();
        httpTest.ForCallsTo("*/b1s/v*/Login").RespondWithJson(LoginResponse, cookies: new { B1SESSION = "session" });
        httpTest.ForCallsTo("*/b1s/v*/Orders").RespondWith("{\"error\":{\"message\":{\"value\":\"Server error\"}}}", 500);

        var slConnection = CreateConnection(version, httpTest);
        slConnection.NumberOfAttempts = 2;
        slConnection.RetryDelay = TimeSpan.Zero;

        var attemptNumbers = new List<int>();
        var onErrorExceptions = new List<Exception>();

        slConnection
            .AfterCall(call =>
            {
                if (call.RequestMessage.RequestUri.AbsolutePath.EndsWith("/Orders", StringComparison.Ordinal))
                {
                    attemptNumbers.Add(call.AttemptNumber);
                }
            })
            .OnError(call => onErrorExceptions.Add(call.Exception));

        var exception = await Assert.ThrowsAsync<SLException>(() => slConnection.Request("Orders").GetStringAsync());

        Assert.Equal("Server error", exception.Message);
        Assert.Equal(new[] { 1, 2 }, attemptNumbers);
        Assert.Equal(2, onErrorExceptions.Count);
        Assert.All(onErrorExceptions,
            x =>
            {
                var slException = Assert.IsType<SLException>(x);
                Assert.Equal("Server error", slException.ErrorDetails.Message.Value);
            });
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task SuccessfulRequest_FiresBeforeAndAfterCallButNotOnError(string version)
    {
        var httpTest = new MockHttp();
        httpTest.ForCallsTo("*/b1s/v*/Login").RespondWithJson(LoginResponse, cookies: new { B1SESSION = "session" });
        httpTest.RespondWith("{}");

        var slConnection = CreateConnection(version, httpTest);

        var beforeCallCount = 0;
        var afterCallCount = 0;
        var onErrorCount = 0;

        slConnection
            .BeforeCall(_ => beforeCallCount++)
            .AfterCall(call =>
            {
                afterCallCount++;
                Assert.True(call.Completed);
                Assert.True(call.Succeeded);
                Assert.Equal(1, call.AttemptNumber);
                Assert.NotNull(call.Duration);
            })
            .OnError(_ => onErrorCount++);

        await slConnection.Request("Orders").GetStringAsync();

        // Both the automatic Login call and the Orders call flow through the handlers
        Assert.Equal(2, beforeCallCount);
        Assert.Equal(2, afterCallCount);
        Assert.Equal(0, onErrorCount);
    }
}
