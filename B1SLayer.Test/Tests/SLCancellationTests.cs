using B1SLayer.Models;
using B1SLayer.Test.Models;

using Flurl;

namespace B1SLayer.Test;

public class SLCancellationTests : TestBase
{
    private const string RetryableErrorResponse = "{\"error\":{\"message\":{\"value\":\"Server error\"}}}";

    public static IEnumerable<object[]> CancelledRequestOperations()
    {
        foreach (var root in ServiceLayerRoots())
        {
            foreach (var operation in RequestOperationNames())
            {
                yield return [CreateIsolatedConnection(root), operation];
            }
        }
    }

    public static IEnumerable<object[]> CancelledConnectionOperations()
    {
        foreach (var root in ServiceLayerRoots())
        {
            foreach (var operation in ConnectionOperationNames())
            {
                yield return [CreateIsolatedConnection(root), operation];
            }
        }
    }

    public static IEnumerable<object[]> IsolatedSLConnections()
    {
        foreach (var root in ServiceLayerRoots())
        {
            yield return [CreateIsolatedConnection(root)];
        }
    }

    [Theory]
    [MemberData(nameof(CancelledRequestOperations))]
    public async Task SLRequestMethods_WithCancelledToken_ThrowOperationCanceledException(
        SLConnection slConnection,
        string operation)
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ExecuteRequestOperationAsync(slConnection, operation, cts.Token));
    }

    [Theory]
    [MemberData(nameof(CancelledConnectionOperations))]
    public async Task SLConnectionMethods_WithCancelledToken_ThrowOperationCanceledException(
        SLConnection slConnection,
        string operation)
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ExecuteConnectionOperationAsync(slConnection, operation, cts.Token));
    }

    [Theory]
    [MemberData(nameof(IsolatedSLConnections))]
    public async Task GetAllAsync_WithCancelledToken_ThrowsOperationCanceledException(SLConnection slConnection)
    {
        var page1 = new SLCollectionRoot<MarketingDocument>
        {
            Value =
            [
                new MarketingDocument { DocEntry = 1, CardCode = "C20001" }
            ],
            ODataNextLinkJson = "Orders?$skip=1"
        };

        HttpTest.RespondWithJson(page1);
        HttpTest.RespondWith("{\"value\":[]}");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            slConnection.Request("Orders").GetAllAsync<MarketingDocument>(cts.Token));
    }

    [Theory]
    [MemberData(nameof(IsolatedSLConnections))]
    public async Task GetStringAsync_WhenCancelledDuringRetryDelay_ThrowsOperationCanceledException(SLConnection slConnection)
    {
        HttpTest.ForCallsTo("*/b1s/v*/Orders").RespondWith(RetryableErrorResponse, 500);

        using var cts = new CancellationTokenSource();
        var requestTask = slConnection.Request("Orders").GetStringAsync(cts.Token);

        await WaitUntilAsync(() => HttpTest.CallLog.Any(call =>
            call.Request.Url.Path.EndsWith("/Orders", StringComparison.Ordinal)));
        await Task.Delay(20);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestTask);

        HttpTest.ShouldHaveCalled(slConnection.ServiceLayerRoot.AppendPathSegment("Orders"))
            .WithVerb(HttpMethod.Get)
            .Times(1);
    }

    private async Task ExecuteRequestOperationAsync(
        SLConnection slConnection,
        string operation,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case "GetAsync":
                HttpTest.RespondWith("{}");
                await slConnection.Request("Orders").GetAsync<object>(cancellationToken: cancellationToken);
                break;
            case "GetWithInlineCountAsync":
                HttpTest.RespondWith("{\"value\":[],\"odata.count\":0}");
                await slConnection.Request("Orders").GetWithInlineCountAsync<List<MarketingDocument>>(cancellationToken: cancellationToken);
                break;
            case "GetStringAsync":
                HttpTest.RespondWith("{}");
                await slConnection.Request("Orders").GetStringAsync(cancellationToken);
                break;
            case "GetAnonymousTypeAsync":
                HttpTest.RespondWith("{\"docEntry\":1,\"cardCode\":\"C20001\"}");
                await slConnection.Request("Orders(1)")
                    .GetAnonymousTypeAsync(new { DocEntry = 0, CardCode = string.Empty },
                        cancellationToken: cancellationToken);
                break;
            case "GetBytesAsync":
                HttpTest.RespondWith("file");
                await slConnection.Request("Attachments2(1)/$value").GetBytesAsync(cancellationToken);
                break;
            case "GetStreamAsync":
                HttpTest.RespondWith("file");

                using (await slConnection.Request("Attachments2(1)/$value").GetStreamAsync(cancellationToken))
                {
                }

                break;
            case "GetCountAsync":
                HttpTest.RespondWith("0");
                await slConnection.Request("Orders").GetCountAsync(cancellationToken);
                break;
            case "PostAsync":
                HttpTest.RespondWith("{}");
                await slConnection.Request("Orders").PostAsync(new { CardCode = "C00001" }, cancellationToken);
                break;
            case "PostAsyncOfT":
                HttpTest.RespondWith("true");
                await slConnection.Request("SomeService").PostAsync<bool>(cancellationToken: cancellationToken);
                break;
            case "PostStringAsyncOfT":
                HttpTest.RespondWith("true");
                await slConnection.Request("SomeService").PostStringAsync<bool>("{}", cancellationToken: cancellationToken);
                break;
            case "PostReceiveStringAsync":
                HttpTest.RespondWith("{}");
                await slConnection.Request("Orders").PostReceiveStringAsync(new { CardCode = "C00001" }, cancellationToken);
                break;
            case "PostStringAsync":
                HttpTest.RespondWith("{}");
                await slConnection.Request("Orders").PostStringAsync("{}", cancellationToken);
                break;
            case "PatchAsync":
                HttpTest.RespondWith(status: 204);
                await slConnection.Request("Orders(1)").PatchAsync(new { Comments = "Updated" }, cancellationToken);
                break;
            case "PatchStringAsync":
                HttpTest.RespondWith(status: 204);
                await slConnection.Request("Orders(1)").PatchStringAsync("{\"Comments\":\"Updated\"}", cancellationToken);
                break;
            case "PatchWithFileAsync":
                HttpTest.RespondWith(status: 204);
                await slConnection.Request("Attachments2(1)").PatchWithFileAsync("file.txt", [1, 2, 3], cancellationToken);
                break;
            case "PutAsync":
                HttpTest.RespondWith(status: 204);
                await slConnection.Request("Orders(1)").PutAsync(new { Comments = "Updated" }, cancellationToken);
                break;
            case "PutStringAsync":
                HttpTest.RespondWith(status: 204);
                await slConnection.Request("Orders(1)").PutStringAsync("{\"Comments\":\"Updated\"}", cancellationToken);
                break;
            case "DeleteAsync":
                HttpTest.RespondWith(status: 204);
                await slConnection.Request("Orders(1)").DeleteAsync(cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unknown request operation '{operation}'.");
        }
    }

    private async Task ExecuteConnectionOperationAsync(
        SLConnection slConnection,
        string operation,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case "LoginAsync":
                await slConnection.LoginAsync(cancellationToken);
                break;
            case "LogoutAsync":
                await slConnection.LogoutAsync(cancellationToken);
                break;
            case "PingAsync":
                HttpTest.RespondWithJson(new { message = "pong", sender = "node", timestamp = 1 });
                await slConnection.PingAsync(cancellationToken);
                break;
            case "PingNodeAsync":
                HttpTest.RespondWithJson(new { message = "pong", sender = "node", timestamp = 1 });
                await slConnection.PingNodeAsync(1, cancellationToken);
                break;
            case "PostAttachmentAsync":
                HttpTest.RespondWithJson(new SLAttachment { AbsoluteEntry = 1 });
                await slConnection.PostAttachmentAsync("file.txt", [1, 2, 3], cancellationToken);
                break;
            case "PostAttachmentsAsync":
                HttpTest.RespondWithJson(new SLAttachment { AbsoluteEntry = 1 });
                await slConnection.PostAttachmentsAsync(new Dictionary<string, byte[]> { ["file.txt"] = [1, 2, 3] }, cancellationToken);
                break;
            case "PatchAttachmentAsync":
                HttpTest.RespondWith(status: 204);
                await slConnection.PatchAttachmentAsync(1, "file.txt", [1, 2, 3], cancellationToken);
                break;
            case "PatchAttachmentsAsync":
                HttpTest.RespondWith(status: 204);
                await slConnection.PatchAttachmentsAsync(1, new Dictionary<string, byte[]> { ["file.txt"] = [1, 2, 3] }, cancellationToken);
                break;
            case "GetAttachmentAsBytesAsync":
                HttpTest.RespondWith("file");
                await slConnection.GetAttachmentAsBytesAsync(1, cancellationToken: cancellationToken);
                break;
            case "GetAttachmentAsStreamAsync":
                HttpTest.RespondWith("file");

                using (await slConnection.GetAttachmentAsStreamAsync(1, cancellationToken: cancellationToken))
                {
                }

                break;
            case "PostBatchAsync":
                HttpTest.RespondWith("{}");
                await slConnection.PostBatchAsync(
                    [new SLBatchRequest(HttpMethod.Post, "BusinessPartners", new { CardCode = "C00001" }, 1)],
                    cancellationToken: cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unknown connection operation '{operation}'.");
        }
    }

    private static IEnumerable<Uri> ServiceLayerRoots()
    {
        yield return SLConnectionV1.ServiceLayerRoot;
        yield return SLConnectionV2.ServiceLayerRoot;
    }

    private static IEnumerable<string> RequestOperationNames()
    {
        yield return "GetAsync";
        yield return "GetWithInlineCountAsync";
        yield return "GetStringAsync";
        yield return "GetAnonymousTypeAsync";
        yield return "GetBytesAsync";
        yield return "GetStreamAsync";
        yield return "GetCountAsync";
        yield return "PostAsync";
        yield return "PostAsyncOfT";
        yield return "PostStringAsyncOfT";
        yield return "PostReceiveStringAsync";
        yield return "PostStringAsync";
        yield return "PatchAsync";
        yield return "PatchStringAsync";
        yield return "PatchWithFileAsync";
        yield return "PutAsync";
        yield return "PutStringAsync";
        yield return "DeleteAsync";
    }

    private static IEnumerable<string> ConnectionOperationNames()
    {
        yield return "LoginAsync";
        yield return "LogoutAsync";
        yield return "PingAsync";
        yield return "PingNodeAsync";
        yield return "PostAttachmentAsync";
        yield return "PostAttachmentsAsync";
        yield return "PatchAttachmentAsync";
        yield return "PatchAttachmentsAsync";
        yield return "GetAttachmentAsBytesAsync";
        yield return "GetAttachmentAsStreamAsync";
        yield return "PostBatchAsync";
    }

    private static SLConnection CreateIsolatedConnection(Uri serviceLayerRoot)
    {
        return new SLConnection(serviceLayerRoot, "CompanyDB", $"manager-{Guid.NewGuid():N}", "12345");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        while (!condition()) await Task.Delay(10, cts.Token);
    }
}