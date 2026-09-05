using B1SLayer.Models;
using B1SLayer.Test.Models;

namespace B1SLayer.Test;

public class SLRequestTests : TestBase
{
    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task RequestParameters_AreApplied(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("{}");

        await slConnection.Request("$crossjoin(Orders,Orders/DocumentLines)")
            .Select("DocEntry,CardCode")
            .Expand("Orders/DocumentLines($select=ItemCode,LineNum)")
            .Filter("Orders/DocEntry eq Orders/DocumentLines/DocEntry")
            .OrderBy("DocTotal asc,DocEntry desc")
            .Apply("aggregate(DocRate with sum as TotalDocRate)")
            .Top(1)
            .Skip(2)
            .SetQueryParam("X", "Z")
            .WithReturnNoContent()
            .WithCaseInsensitive()
            .WithReplaceCollectionsOnPatch()
            .WithPageSize(50)
            .WithHeader("A", "B")
            .GetAsync<object>();

        HttpTest.ShouldHaveCalled(slConnection.ServiceLayerRoot.AppendPathSegment("$crossjoin(Orders,Orders/DocumentLines)"))
            .WithVerb(HttpMethod.Get)
            .WithQueryParam("$select", "DocEntry,CardCode")
            .WithQueryParam("$expand", "Orders/DocumentLines($select=ItemCode,LineNum)")
            .WithQueryParam("$filter", "Orders/DocEntry eq Orders/DocumentLines/DocEntry")
            .WithQueryParam("$orderby", "DocTotal asc,DocEntry desc")
            .WithQueryParam("$apply", "aggregate(DocRate with sum as TotalDocRate)")
            .WithQueryParam("$top", 1)
            .WithQueryParam("$skip", 2)
            .WithQueryParam("X", "Z")
            .WithHeader("Prefer", "return-no-content")
            .WithHeader("B1S-CaseInsensitive", "true")
            .WithHeader("B1S-ReplaceCollectionsOnPatch", "true")
            .WithHeader("B1S-PageSize", 50)
            .WithHeader("A", "B")
            .Times(1);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task LoginAsync_IsPerformedAutomatically(string version)
    {
        var slConnection = GetConnection(version);

        await slConnection.Request("Orders").GetStringAsync(); // random request

        Assert.Equal(LoginResponse.SessionId, slConnection.LoginResponse.SessionId);
        Assert.Equal(LoginResponse.Version, slConnection.LoginResponse.Version);
        Assert.Equal(LoginResponse.SessionTimeout, slConnection.LoginResponse.SessionTimeout);

        await slConnection.LogoutAsync();
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task GetAsync_ReturnsCorrectData(string version)
    {
        var slConnection = GetConnection(version);

        var expectedData = new List<MarketingDocument>
        {
            new() { DocEntry = 1, CardCode = "C20001" },
            new() { DocEntry = 2, CardCode = "C20002" }
        };

        HttpTest.RespondWithJson(new SLCollectionRoot<MarketingDocument> { Value = expectedData });

        var result = await slConnection
            .Request("Orders")
            .GetAsync<List<MarketingDocument>>();

        HttpTest.ShouldHaveCalled(slConnection.ServiceLayerRoot.AppendPathSegment("Orders"))
            .WithVerb(HttpMethod.Get)
            .Times(1);

        Assert.Equal(2, result.Count);
        Assert.Equal(expectedData[0].DocEntry, result[0].DocEntry);
        Assert.Equal(expectedData[1].CardCode, result[1].CardCode);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task GetAllAsync_ReturnsCorrectData(string version)
    {
        var slConnection = GetConnection(version);

        var page1 = new SLCollectionRoot<MarketingDocument>
        {
            Value =
            [
                new MarketingDocument { DocEntry = 1, CardCode = "C20001" },
                new MarketingDocument { DocEntry = 2, CardCode = "C20002" }
            ],
            ODataNextLinkJson = "Orders?$select=DocEntry,CardCode&$skip=2"
        };

        var page2 = new SLCollectionRoot<MarketingDocument>
        {
            Value =
            [
                new MarketingDocument { DocEntry = 3, CardCode = "C20003" },
                new MarketingDocument { DocEntry = 4, CardCode = "C20004" }
            ],
            ODataNextLinkJson = "Orders?$select=DocEntry,CardCode&$skip=4"
        };

        HttpTest.RespondWithJson(page1);
        HttpTest.RespondWithJson(page2);
        HttpTest.RespondWith("{\"value\":[]}");

        var orderList = await slConnection.Request("Orders").WithPageSize(2).GetAllAsync<MarketingDocument>();

        HttpTest.ShouldHaveCalled(slConnection.ServiceLayerRoot.AppendPathSegment("Orders"))
            .WithVerb(HttpMethod.Get)
            .Times(3);

        Assert.Equal(4, orderList.Count);
        Assert.Equal((page1.Value[0].DocEntry, page1.Value[0].CardCode), (orderList[0].DocEntry, orderList[0].CardCode));
        Assert.Equal((page1.Value[1].DocEntry, page1.Value[1].CardCode), (orderList[1].DocEntry, orderList[1].CardCode));
        Assert.Equal((page2.Value[0].DocEntry, page2.Value[0].CardCode), (orderList[2].DocEntry, orderList[2].CardCode));
        Assert.Equal((page2.Value[1].DocEntry, page2.Value[1].CardCode), (orderList[3].DocEntry, orderList[3].CardCode));
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task PostAsync_PrimitiveNumber_ReturnsCorrectValue(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("2.92920");

        var result = await slConnection
            .Request("SBOBobService_GetCurrencyRate")
            .PostAsync<double?>(new { Currency = "EUR", Date = "20240101" });

        Assert.Equal(2.92920, result);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task GetAsync_PrimitiveString_ReturnsCorrectValue(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("\"hello\"");

        var result = await slConnection
            .Request("SomeService")
            .GetAsync<string>();

        Assert.Equal("hello", result);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task PostAsync_PrimitiveBoolean_ReturnsCorrectValue(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("true");

        var result = await slConnection
            .Request("SomeService")
            .PostAsync<bool>();

        Assert.True(result);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task SessionCookies_AreCachedAndReusedAcrossRequests(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("{}");

        await slConnection.Request("Orders").GetStringAsync();
        await slConnection.Request("Orders").GetStringAsync();

        // A single login serves both requests, and the session cookies are attached to each resource request
        HttpTest.ShouldHaveCalled($"{slConnection.ServiceLayerRoot}/Login")
            .WithVerb(HttpMethod.Post)
            .Times(1);

        HttpTest.ShouldHaveCalled(slConnection.ServiceLayerRoot.AppendPathSegment("Orders"))
            .WithVerb(HttpMethod.Get)
            .WithHeader("Cookie", SessionCookieHeader)
            .Times(2);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task WithHeader_NullValue_RemovesHeader(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("{}");

        await slConnection.Request("Orders")
            .WithPageSize(50)
            .WithHeader("B1S-PageSize", null)
            .GetStringAsync();

        HttpTest.ShouldHaveCalled(slConnection.ServiceLayerRoot.AppendPathSegment("Orders"))
            .WithoutHeader("B1S-PageSize")
            .Times(1);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task SetQueryParam_NullValue_RemovesParameter(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("{}");

        await slConnection.Request("Orders")
            .Top(5)
            .SetQueryParam("$top", null)
            .GetStringAsync();

        HttpTest.ShouldHaveCalled(slConnection.ServiceLayerRoot.AppendPathSegment("Orders"))
            .WithoutQueryParam("$top")
            .Times(1);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task WithHeader_UserCookie_IsMergedWithSessionCookies(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("{}");

        await slConnection.Request("Orders")
            .WithHeader("Cookie", "ROUTEID=.node9")
            .GetStringAsync();

        // A single Cookie header field is sent, with the user's cookies taking precedence
        HttpTest.ShouldHaveCalled(slConnection.ServiceLayerRoot.AppendPathSegment("Orders"))
            .WithHeader("Cookie", $"ROUTEID=.node9; {SessionCookieHeader}")
            .Times(1);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task GetWithInlineCountAsync_StringType_ReturnsRawJsonAndCount(string version)
    {
        var slConnection = GetConnection(version);
        var countProperty = version == "v2" ? "@odata.count" : "odata.count";
        HttpTest.RespondWith($$"""{"{{countProperty}}":155,"value":[{"DocEntry":1}]}""");

        var (result, count) = await slConnection.Request("Orders").GetWithInlineCountAsync<string>();

        Assert.Equal(155, count);
        Assert.Contains("\"DocEntry\":1", result);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task PostStringAsync_SendsJsonContentType(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("{}");

        await slConnection.Request("Orders").PostStringAsync("{\"DocEntry\":1}");

        HttpTest.ShouldHaveCalled(slConnection.ServiceLayerRoot.AppendPathSegment("Orders"))
            .WithVerb(HttpMethod.Post)
            .WithHeader("Content-Type", "application/json; charset=utf-8")
            .WithRequestBody("{\"DocEntry\":1}")
            .Times(1);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task GetAsync_StringType_WithObjectResponse_ReturnsRawJson(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("{\"DocEntry\":1,\"CardCode\":\"C20001\"}");

        var result = await slConnection
            .Request("Orders")
            .GetAsync<string>();

        Assert.Equal("{\"DocEntry\":1,\"CardCode\":\"C20001\"}", result);
    }
}
