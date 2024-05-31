using B1SLayer.Models;
using B1SLayer.Test.Models;
using Flurl;
using Flurl.Http.Testing;

namespace B1SLayer.Test;

public class SLRequestTests : TestBase
{
    [Theory]
    [MemberData(nameof(SLConnections))]
    public async Task RequestParameters_AreApplied(SLConnection slConnection)
    {
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
    [MemberData(nameof(SLConnections))]
    public async Task LoginAsync_IsPerformedAutomatically(SLConnection slConnection)
    {
        await slConnection.Request("Orders").GetStringAsync(); // random request

        Assert.Equal(LoginResponse.SessionId, slConnection.LoginResponse.SessionId);
        Assert.Equal(LoginResponse.Version, slConnection.LoginResponse.Version);
        Assert.Equal(LoginResponse.SessionTimeout, slConnection.LoginResponse.SessionTimeout);

        await slConnection.LogoutAsync();
    }

    [Theory]
    [MemberData(nameof(SLConnections))]
    public async Task GetAsync_ReturnsCorrectData(SLConnection slConnection)
    {
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
    [MemberData(nameof(SLConnections))]
    public async Task GetAllAsync_ReturnsCorrectData(SLConnection slConnection)
    {
        var page1 = new SLCollectionRoot<MarketingDocument>
        {
            Value =
            [
                new() { DocEntry = 1, CardCode = "C20001" },
                new() { DocEntry = 2, CardCode = "C20002" }
            ],
            ODataNextLinkJson = "Orders?$select=DocEntry,CardCode&$skip=2"
        };

        var page2 = new SLCollectionRoot<MarketingDocument>
        {
            Value =
            [
                new() { DocEntry = 3, CardCode = "C20003" },
                new() { DocEntry = 4, CardCode = "C20004" }
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
}