using B1SLayer.Models;
using B1SLayer.Test.Models;

namespace B1SLayer.Test;

public class SLRequestTests : TestBase
{
    private string RequestedUri => HttpTest.CallLog.Single(x => !x.Uri.AbsolutePath.EndsWith("/Login")).Uri.AbsoluteUri;

    [Theory]
    [InlineData("v1", "Orders?$select=DocEntry&$filter=CardName%20eq%20%27AT%26T%20%231%27", "/Orders?$select=DocEntry&$filter=CardName%20eq%20%27AT%26T%20%231%27")]
    [InlineData("v2", "Orders?$select=DocEntry&$filter=CardName%20eq%20%27AT%26T%20%231%27", "/Orders?$select=DocEntry&$filter=CardName%20eq%20%27AT%26T%20%231%27")]
    [InlineData("v1", "Orders?$filter=DocNum eq 1", "/Orders?$filter=DocNum%20eq%201")]
    [InlineData("v2", "Orders?$filter=DocNum eq 1", "/Orders?$filter=DocNum%20eq%201")]
    [InlineData("v1", "BusinessPartners('A?B')", "/BusinessPartners('A%3FB')")]
    [InlineData("v2", "BusinessPartners('A?B')", "/BusinessPartners('A%3FB')")]
    [InlineData("v1", "BusinessPartners('A?B')?$select=CardCode", "/BusinessPartners('A%3FB')?$select=CardCode")]
    [InlineData("v2", "BusinessPartners('A?B')?$select=CardCode", "/BusinessPartners('A%3FB')?$select=CardCode")]
    [InlineData("v2", "BusinessPartners(%27A?B%27)?$select=CardCode", "/BusinessPartners(%27A%3FB%27)?$select=CardCode")]
    [InlineData("v2", "Items?$filter=ItemName eq %27A'&$top=2", "/Items?$filter=ItemName%20eq%20%27A'&$top=2")]
    [InlineData("v2", "Items?$filter=ItemName eq %27A&B%27&$top=2", "/Items?$filter=ItemName%20eq%20%27A%26B%27&$top=2")]
    [InlineData("v1", "Items?$filter=ItemName eq 'A+B'&$top=2", "/Items?$filter=ItemName%20eq%20'A%2BB'&$top=2")]
    [InlineData("v2", "Items?$filter=ItemName eq 'A+B'&$top=2", "/Items?$filter=ItemName%20eq%20'A%2BB'&$top=2")]
    [InlineData("v2", "Items?$filter=ItemName+eq+%27A+B%27&$top=2", "/Items?$filter=ItemName+eq+%27A+B%27&$top=2")]
    [InlineData("v2", "BusinessPartners('A+B')", "/BusinessPartners('A+B')")]
    public async Task Resource_KeepsItsOwnQueryString(string version, string resource, string expectedPathAndQuery)
    {
        HttpTest.RespondWith("{}");

        await GetConnection(version).Request(resource).GetStringAsync();

        Assert.EndsWith(expectedPathAndQuery, RequestedUri);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task Resource_QueryString_FluentParametersAreMerged(string version)
    {
        HttpTest.RespondWith("{}");

        await GetConnection(version).Request("Orders?$select=DocEntry&$top=5").Top(1).Skip(2).GetStringAsync();

        Assert.EndsWith("/Orders?$select=DocEntry&$top=1&$skip=2", RequestedUri);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task Resource_QueryString_EncodedParameterNamesAreReplaced(string version)
    {
        HttpTest.RespondWith("{}");

        await GetConnection(version).Request("Orders?%24top=5&%24select=DocEntry").Top(1).GetStringAsync();

        Assert.EndsWith("/Orders?%24select=DocEntry&$top=1", RequestedUri);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task Resource_QueryString_AmpersandInAQuotedLiteralIsData(string version)
    {
        HttpTest.RespondWith("{}");

        await GetConnection(version).Request("Items?$filter=ItemName eq 'A&$top=9'&$top=2").Top(5).GetStringAsync();

        Assert.EndsWith("/Items?$filter=ItemName%20eq%20'A%26$top=9'&$top=5", RequestedUri);
    }

    [Theory]
    [InlineData("v1", "O'Brien", "/BusinessPartners('O''Brien')")]
    [InlineData("v2", "O'Brien", "/BusinessPartners('O''Brien')")]
    [InlineData("v2", "x')/ContactEmployees('y", "/BusinessPartners('x'')/ContactEmployees(''y')")]
    [InlineData("v2", "x%27)/ContactEmployees(%27y", "/BusinessPartners('x%27%27)/ContactEmployees(%27%27y')")]
    public async Task ResourceWithId_EscapesTheKeyAsData(string version, string id, string expectedPath)
    {
        HttpTest.RespondWith("{}");

        await GetConnection(version).Request("BusinessPartners", id).GetStringAsync();

        Assert.EndsWith(expectedPath, RequestedUri);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task ResourceWithId_CanNotStartAQueryString(string version)
    {
        HttpTest.RespondWith("{}");

        await GetConnection(version).Request("Items", "x')?$select=U_Secret&z=('").GetStringAsync();

        var uri = HttpTest.CallLog.Single(x => !x.Uri.AbsolutePath.EndsWith("/Login")).Uri;
        Assert.Empty(uri.Query);
        Assert.Contains("%3F", uri.AbsolutePath);
    }

    [Theory]
    [InlineData("v1", "Items?name=O'Brien&$top=1", "/Items?name=O'Brien&$top=5")]
    [InlineData("v2", "Items?name=O'Brien&$top=1", "/Items?name=O'Brien&$top=5")]
    [InlineData("v2", "Items('O'Brien')?$top=1", "/Items('O'Brien')?$top=5")]
    public async Task Resource_UnbalancedQuotes_DoNotMergeParameters(string version, string resource, string expectedPathAndQuery)
    {
        HttpTest.RespondWith("{}");

        await GetConnection(version).Request(resource).Top(5).GetStringAsync();

        Assert.EndsWith(expectedPathAndQuery, RequestedUri);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task Resource_OnlyAQueryString_StaysOnTheRoot(string version)
    {
        HttpTest.RespondWith("{}");

        await GetConnection(version).Request("?$top=1").GetStringAsync();

        Assert.EndsWith($"/{version}?$top=1", RequestedUri);
    }

    [Theory]
    [InlineData("v1", "BusinessPartners('A?B')?$select=CardCode", "/BusinessPartners('A%3FB')?$select=CardCode")]
    [InlineData("v2", "BusinessPartners('A?B')?$select=CardCode", "/BusinessPartners('A%3FB')?$select=CardCode")]
    [InlineData("v1", "Items?$filter=ItemName eq 'A&B'&$top=2", "/Items?$filter=ItemName%20eq%20'A%26B'&$top=2")]
    [InlineData("v2", "Items?$filter=ItemName eq 'A&B'&$top=2", "/Items?$filter=ItemName%20eq%20'A%26B'&$top=2")]
    [InlineData("v2", "Items?name=O'Brien&$top=1", "/Items?name=O'Brien&$top=1")]
    [InlineData("v2", "?$top=1", "/v2?$top=1")]
    [InlineData("v1", "Items?$filter=ItemName eq 'A+B'", "/Items?$filter=ItemName%20eq%20'A%252BB'")]
    [InlineData("v2", "Items?$filter=ItemName eq 'A+B'", "/Items?$filter=ItemName%20eq%20'A%252BB'")]
    [InlineData("v2", "Items?$filter=ItemName eq 'A%2BB'", "/Items?$filter=ItemName%20eq%20'A%252BB'")]
    [InlineData("v1", "BusinessPartners('A+B')", "/BusinessPartners('A%2BB')")]
    [InlineData("v2", "BusinessPartners('A+B')", "/BusinessPartners('A%2BB')")]
    public async Task BatchResource_FollowsTheSameQueryRulesAsARequest(string version, string resource, string expectedPathAndQuery)
    {
        HttpTest.RespondWith("--b\r\n\r\nHTTP/1.1 200 OK\r\n\r\n{}\r\n--b--");

        await GetConnection(version).PostBatchAsync(new SLBatchRequest(HttpMethod.Get, resource));

        Assert.Contains(expectedPathAndQuery + " HTTP/", HttpTest.CallLog.Single(x => x.Uri.AbsolutePath.EndsWith("/$batch")).RequestBody);
    }

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
    public async Task GetWithETagAsync_ReturnsResponseBodyAndETag(string version)
    {
        var slConnection = GetConnection(version);
        const string etag = "W/\"0000000000000000000000000000000000000000\"";
        HttpTest.RespondWith("{\"DocEntry\":1,\"CardCode\":\"C20001\"}",
            200,
            new Dictionary<string, string> { ["ETag"] = etag });

        var (result, resultETag) = await slConnection
            .Request("Orders", 1)
            .GetWithETagAsync<MarketingDocument>();

        Assert.Equal(1, result.DocEntry);
        Assert.Equal("C20001", result.CardCode);
        Assert.Equal(etag, resultETag);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task GetWithETagAsync_NoETagHeader_ReturnsNullETag(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("{\"DocEntry\":1}");

        var (result, resultETag) = await slConnection
            .Request("Orders", 1)
            .GetWithETagAsync<MarketingDocument>();

        Assert.Equal(1, result.DocEntry);
        Assert.Null(resultETag);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task WithETag_SendsIfMatchHeader(string version)
    {
        var slConnection = GetConnection(version);
        const string etag = "W/\"0000000000000000000000000000000000000000\"";
        HttpTest.RespondWith("", 204);

        await slConnection
            .Request("Orders", 1)
            .WithETag(etag)
            .PatchAsync(new { Comments = "Updated" });

        HttpTest.ShouldHaveCalled(slConnection.ServiceLayerRoot.AppendPathSegment("Orders(1)"))
            .WithVerb(HttpMethod.Patch)
            .WithHeader("If-Match", etag)
            .Times(1);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task PatchAsync_WithoutETag_DoesNotSendIfMatchHeader(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("", 204);

        await slConnection.Request("Orders", 1).PatchAsync(new { Comments = "Updated" });

        HttpTest.ShouldHaveCalled(slConnection.ServiceLayerRoot.AppendPathSegment("Orders(1)"))
            .WithVerb(HttpMethod.Patch)
            .WithoutHeader("If-Match")
            .Times(1);
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
