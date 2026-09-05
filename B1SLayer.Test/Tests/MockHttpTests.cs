using System.Net;

using Xunit.Sdk;

namespace B1SLayer.Test;

/// <summary>
///     Pins the semantics of the in-repo mock harness itself, so the whole
///     suite can rely on them going forward.
/// </summary>
public class MockHttpTests
{
    private const string BaseUrl = "https://sapserver:50000/b1s/v1";

    private static HttpClient CreateClient(MockHttp mockHttp) => new(mockHttp, false);

    [Fact]
    public async Task ResponseQueue_ServesInOrderThenRepeatsLast()
    {
        var mockHttp = new MockHttp();
        mockHttp.RespondWith("first").RespondWith("second");
        using var client = CreateClient(mockHttp);

        Assert.Equal("first", await client.GetStringAsync($"{BaseUrl}/Orders"));
        Assert.Equal("second", await client.GetStringAsync($"{BaseUrl}/Orders"));

        // The last registered response is sticky — retry tests rely on this
        Assert.Equal("second", await client.GetStringAsync($"{BaseUrl}/Orders"));
    }

    [Fact]
    public async Task EmptyQueue_ServesEmpty200()
    {
        var mockHttp = new MockHttp();
        using var client = CreateClient(mockHttp);

        using var response = await client.GetAsync($"{BaseUrl}/Orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ForCallsTo_WithVerb_RoutesByMethod()
    {
        var mockHttp = new MockHttp();
        mockHttp.ForCallsTo("*/Orders").WithVerb(HttpMethod.Post).RespondWith("created", 201);
        mockHttp.ForCallsTo("*/Orders").RespondWith("fetched");
        using var client = CreateClient(mockHttp);

        using var postResponse = await client.PostAsync($"{BaseUrl}/Orders", new StringContent("{}"));
        var getBody = await client.GetStringAsync($"{BaseUrl}/Orders");

        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        Assert.Equal("fetched", getBody);

        mockHttp.ShouldHaveCalled("*/Orders").WithVerb(HttpMethod.Post, HttpMethod.Get).Times(2);
    }

    [Fact]
    public async Task ForCallsTo_MultiplePatterns_MatchesAny()
    {
        var mockHttp = new MockHttp();
        mockHttp.ForCallsTo("*/Orders", "*/Invoices").RespondWith("matched");
        mockHttp.RespondWith("default");
        using var client = CreateClient(mockHttp);

        Assert.Equal("matched", await client.GetStringAsync($"{BaseUrl}/Orders"));
        Assert.Equal("matched", await client.GetStringAsync($"{BaseUrl}/Invoices"));
        Assert.Equal("default", await client.GetStringAsync($"{BaseUrl}/BusinessPartners"));
    }

    [Fact]
    public async Task WithDelay_HonorsCancellation()
    {
        var mockHttp = new MockHttp();
        mockHttp.ForCallsTo("*/Orders").WithDelay(TimeSpan.FromSeconds(10)).RespondWith("{}");
        using var client = CreateClient(mockHttp);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetStringAsync($"{BaseUrl}/Orders", cts.Token));
    }

    [Fact]
    public async Task CallServedByEmptyDefaultSetup_IsFlaggedAsUnmatched()
    {
        var mockHttp = new MockHttp();
        mockHttp.ForCallsTo("*/Orders").RespondWith("{}");
        using var client = CreateClient(mockHttp);

        using var response = await client.GetAsync($"{BaseUrl}/Typo");

        // A request no setup matched gets the synthetic empty 200 and is flagged, so a
        // typo'd pattern shows up in assertion-failure dumps instead of passing silently
        Assert.Contains("unmatched", mockHttp.CallLog.Single().ToString());
    }

    [Fact]
    public async Task AssertionFailure_ListsTheRecordedCalls()
    {
        var mockHttp = new MockHttp();
        mockHttp.RespondWith("{}");
        using var client = CreateClient(mockHttp);
        using var response = await client.GetAsync($"{BaseUrl}/Orders");

        var exception = Assert.Throws<XunitException>(() =>
        {
            mockHttp.ShouldHaveCalled("*/Nope");
        });

        Assert.Contains("All recorded calls (1):", exception.Message);
        Assert.Contains($"GET {BaseUrl}/Orders", exception.Message);
    }

    [Fact]
    public async Task AssertionFailure_OnNarrowedCondition_ShowsSurvivingCallDetails()
    {
        var mockHttp = new MockHttp();
        mockHttp.RespondWith("{}");
        using var client = CreateClient(mockHttp);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/Orders");
        request.Headers.TryAddWithoutValidation("X-Custom", "abc");
        (await client.SendAsync(request)).Dispose();
        using var otherResponse = await client.GetAsync($"{BaseUrl}/Other");

        var exception = Assert.Throws<XunitException>(() =>
            mockHttp.ShouldHaveCalled("*/Orders").WithHeader("X-Custom", "wrong"));

        // The calls the failing condition was evaluated against are dumped with their headers,
        // so the actual value is visible directly in the failure message
        Assert.Contains("Calls matching the previous conditions (1):", exception.Message);
        Assert.Contains("X-Custom: abc", exception.Message);
    }
}
