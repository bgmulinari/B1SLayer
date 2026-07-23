using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using B1SLayer.Test.Models;

namespace B1SLayer.Test;

public class SLBatchRequestTests : TestBase
{
    private const string
        expectedRequestBody =
            "*--*-*-*-*-*Content-Type: multipart/mixed; boundary=\"changeset_*-*-*-*-*\"*--changeset_*-*-*-*-*Content-Type: application/http; msgtype=request*content-transfer-encoding: binary*Content-ID: 1*POST /b1s/v*/BusinessPartners HTTP/1.1*Host: *:50000*Content-Type: application/json; charset=utf-8*{\"CardCode\":\"C00001\",\"CardName\":\"I am a new BP\"}*--changeset_*-*-*-*-*Content-Type: application/http; msgtype=request*content-transfer-encoding: binary*Content-ID: 2*PATCH /b1s/v*/BusinessPartners('C00001') HTTP/1.1*Host: *:50000*Content-Type: application/json; charset=utf-8*{\"CardName\":\"This is my updated name\"}*--changeset_*-*-*-*-*Content-Type: application/http; msgtype=request*content-transfer-encoding: binary*Content-ID: 3*DELETE /b1s/v*/BusinessPartners('C00001') HTTP/1.1*Host: *:50000*--changeset_*-*-*-*-*--*--*-*-*-*-*--*",
        v1Response =
            "--batchresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: multipart/mixed; boundary=changesetresponse_00000000-0000-0000-0000-000000000000\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\n\r\nHTTP/1.1 201 Created\r\nContent-ID: 1\r\nContent-Type: application/json;odata=minimalmetadata;charset=utf-8\r\nContent-Length: 8811\r\nDataServiceVersion: 3.0\r\nETag: W/\"0000000000000000000000000000000000000000\"\r\nLocation: https://localhost:50000/b1s/v1/BusinessPartners('C00001')\r\n\r\n{\"some\":\"content\"}\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\n\r\nHTTP/1.1 204 No Content\r\nContent-ID: 2\r\nDataServiceVersion: 3.0\r\n\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\n\r\nHTTP/1.1 204 No Content\r\nContent-ID: 3\r\nDataServiceVersion: 3.0\r\n\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000--\r\n--batchresponse_00000000-0000-0000-0000-000000000000--\r\n",
        v2Response =
            "--batchresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: multipart/mixed; boundary=changesetresponse_00000000-0000-0000-0000-000000000000\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\nContent-ID: 1\r\n\r\nHTTP/1.1 201 Created\r\nContent-Type: application/json;odata.metadata=minimal;charset=utf-8\r\nContent-Length: 8811\r\nETag: W/\"0000000000000000000000000000000000000000\"\r\nLocation: https://localhost:50000/b1s/v2/BusinessPartners('C00001')\r\nOData-Version: 4.0\r\n\r\n{\"some\":\"content\"}\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\nContent-ID: 2\r\n\r\nHTTP/1.1 204 No Content\r\nOData-Version: 4.0\r\n\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\nContent-ID: 3\r\n\r\nHTTP/1.1 204 No Content\r\nOData-Version: 4.0\r\n\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000--\r\n--batchresponse_00000000-0000-0000-0000-000000000000--\r\n";

    private static readonly SLBatchRequest[] _requests =
    [
        new(HttpMethod.Post, "BusinessPartners", new { CardCode = "C00001", CardName = "I am a new BP" }, 1),
        new(HttpMethod.Patch, "BusinessPartners('C00001')", new { CardName = "This is my updated name" }, 2),
        new(HttpMethod.Delete, "BusinessPartners('C00001')", contentID: 3)
    ];

    [Fact]
    public async Task PostBatchAsyncV1_ReturnsCorrectData()
    {
        HttpTest.RespondWith(
            v1Response,
            202,
            new Dictionary<string, string> { { "Content-Type", "multipart/mixed;boundary=batchresponse_00000000-0000-0000-0000-000000000000" } });

        var batchResult = await SLConnectionV1.PostBatchAsync(_requests);

        HttpTest.ShouldHaveCalled(SLConnectionV1.ServiceLayerRoot.AppendPathSegment("$batch"))
            .WithVerb(HttpMethod.Post)
            .WithRequestBody(expectedRequestBody)
            .Times(1);

        Assert.Equal(3, batchResult.Length);
        Assert.Equal(HttpStatusCode.Created, batchResult[0].StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, batchResult[1].StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, batchResult[2].StatusCode);
        Assert.Equal("{\"some\":\"content\"}", await batchResult[0].Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostBatchAsyncV2_ReturnsCorrectData()
    {
        HttpTest.RespondWith(
            v2Response,
            202,
            new Dictionary<string, string> { { "Content-Type", "multipart/mixed;boundary=batchresponse_00000000-0000-0000-0000-000000000000" } });

        var batchResult = await SLConnectionV2.PostBatchAsync(_requests);

        HttpTest.ShouldHaveCalled(SLConnectionV2.ServiceLayerRoot.AppendPathSegment("$batch"))
            .WithVerb(HttpMethod.Post)
            .WithRequestBody(expectedRequestBody)
            .Times(1);

        Assert.Equal(3, batchResult.Length);
        Assert.Equal(HttpStatusCode.Created, batchResult[0].StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, batchResult[1].StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, batchResult[2].StatusCode);
        Assert.Equal("{\"some\":\"content\"}", await batchResult[0].Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task PostBatchAsync_MixedGetAndMutations_GetsAreOutsideChangeset(string version)
    {
        var connection = GetConnection(version);

        // Batch: POST, GET, PATCH — the GET between mutations forces two separate changesets
        // and the serialized order must be: changeset{POST}, GET, changeset{PATCH}
        var mixedResponse =
            "--batchresponse_00000000-0000-0000-0000-000000000000\r\n"
            + "Content-Type: multipart/mixed; boundary=cs1\r\n\r\n"
            + "--cs1\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\n\r\n"
            + "HTTP/1.1 201 Created\r\nContent-Type: application/json;charset=utf-8\r\n\r\n{\"CardCode\":\"C00001\"}\n\r\n"
            + "--cs1--\r\n"
            + "--batchresponse_00000000-0000-0000-0000-000000000000\r\n"
            + "Content-Type: application/http\r\nContent-Transfer-Encoding: binary\r\n\r\n"
            + "HTTP/1.1 200 OK\r\nContent-Type: application/json;charset=utf-8\r\n\r\n{\"value\":[]}\n\r\n"
            + "--batchresponse_00000000-0000-0000-0000-000000000000\r\n"
            + "Content-Type: multipart/mixed; boundary=cs2\r\n\r\n"
            + "--cs2\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\n\r\n"
            + "HTTP/1.1 204 No Content\r\n\r\n\r\n"
            + "--cs2--\r\n"
            + "--batchresponse_00000000-0000-0000-0000-000000000000--\r\n";

        HttpTest.RespondWith(
            mixedResponse,
            202,
            new Dictionary<string, string> { { "Content-Type", "multipart/mixed;boundary=batchresponse_00000000-0000-0000-0000-000000000000" } });

        var mixedRequests = new SLBatchRequest[]
        {
            new(HttpMethod.Post, "BusinessPartners", new { CardCode = "C00001", CardName = "New BP" }, 1),
            new(HttpMethod.Get, "Orders"),
            new(HttpMethod.Patch, "BusinessPartners('C00001')", new { CardName = "Updated" }, 2)
        };

        var batchResult = await connection.PostBatchAsync(mixedRequests);

        Assert.Equal(3, batchResult.Length);
        Assert.Equal(HttpStatusCode.Created, batchResult[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, batchResult[1].StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, batchResult[2].StatusCode);

        // Verify the request structure by inspecting the raw multipart body
        HttpTest.ShouldHaveCalled(connection.ServiceLayerRoot.AppendPathSegment("$batch"))
            .WithVerb(HttpMethod.Post)
            .With(x =>
            {
                Assert.NotNull(x.RequestBody);
                var body = x.RequestBody;

                // All three requests must appear in the body
                var posPost = body.IndexOf("POST /b1s/", StringComparison.Ordinal);
                var posGet = body.IndexOf("GET /b1s/", StringComparison.Ordinal);
                var posPatch = body.IndexOf("PATCH /b1s/", StringComparison.Ordinal);

                Assert.True(posPost >= 0, "POST not found");
                Assert.True(posGet >= 0, "GET not found");
                Assert.True(posPatch >= 0, "PATCH not found");

                // Order must be preserved: POST, then GET, then PATCH
                Assert.True(posPost < posGet, "POST should come before GET");
                Assert.True(posGet < posPatch, "GET should come before PATCH");

                // Find all changeset boundary markers (--changeset_...)
                var changesetPositions = new List<int>();
                var searchFrom = 0;

                while (true)
                {
                    var pos = body.IndexOf("--changeset_", searchFrom, StringComparison.Ordinal);

                    if (pos < 0)
                    {
                        break;
                    }

                    changesetPositions.Add(pos);
                    searchFrom = pos + 1;
                }

                // With POST, GET, PATCH and singleChangeSet=true, the GET splits mutations
                // into two changesets. Each changeset has at least an open + close boundary,
                // so we expect at least 4 changeset boundary markers.
                Assert.True(changesetPositions.Count >= 4,
                    $"Expected at least 4 changeset boundary markers (2 changesets), found {changesetPositions.Count}");

                // POST must be inside the first changeset (after first boundary, before GET)
                Assert.True(posPost > changesetPositions[0], "POST should be after first changeset open");
                Assert.True(posPost < posGet, "POST should be before GET");

                // GET must be outside any changeset — between the two groups of changeset markers
                var boundariesBeforeGet = changesetPositions.Where(p => p < posGet).ToList();
                var boundariesAfterGet = changesetPositions.Where(p => p > posGet).ToList();
                Assert.True(boundariesBeforeGet.Count >= 2,
                    "First changeset (open+close) should be before GET");
                Assert.True(boundariesAfterGet.Count >= 2,
                    "Second changeset (open+close) should be after GET");

                // PATCH must be inside the second changeset (after GET)
                Assert.True(posPatch > boundariesAfterGet[0], "PATCH should be after second changeset open");

                return true;
            })
            .Times(1);
    }

    [Fact]
    public async Task PostBatchAsync_WithoutPerRequestOptions_UsesConnectionSerializerOptions()
    {
        HttpTest.RespondWith(
            v1Response,
            202,
            new Dictionary<string, string> { { "Content-Type", "multipart/mixed;boundary=batchresponse_00000000-0000-0000-0000-000000000000" } });

        var connection = CreateConnection("v1");
        connection.JsonSerializerOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        await connection.PostBatchAsync(new SLBatchRequest(HttpMethod.Post, "Orders", new { DocumentStatus = BoStatus.bost_Open }, 1));

        // The connection-level enum converter applies to the batch body when no per-request options are set
        HttpTest.ShouldHaveCalled("*/b1s/v1/$batch")
            .WithRequestBody("""*"DocumentStatus":"bost_Open"*""")
            .Times(1);
    }

    [Fact]
    public async Task PostBatchAsync_PerRequestOptions_TakePrecedenceOverConnectionOptions()
    {
        HttpTest.RespondWith(
            v1Response,
            202,
            new Dictionary<string, string> { { "Content-Type", "multipart/mixed;boundary=batchresponse_00000000-0000-0000-0000-000000000000" } });

        var connection = CreateConnection("v1");
        connection.JsonSerializerOptions = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

        var batchRequest = new SLBatchRequest(HttpMethod.Post, "Orders", new { DocumentStatus = BoStatus.bost_Open }, 1)
        {
            JsonSerializerOptions = new JsonSerializerOptions()
        };

        await connection.PostBatchAsync(batchRequest);

        // The per-request options (no enum converter) win, so the enum is serialized as a number
        HttpTest.ShouldHaveCalled("*/b1s/v1/$batch")
            .With(call => !call.RequestBody.Contains("bost_Open"))
            .Times(1);
    }

    [Fact]
    public async Task PostBatchAsync_SubResponseHeaders_EmptyAndColonContainingValuesAreParsed()
    {
        const string response =
            "--batchresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: multipart/mixed; boundary=changesetresponse_00000000-0000-0000-0000-000000000000\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\n\r\nHTTP/1.1 201 Created\r\nContent-ID: 1\r\nX-Empty-Header:\r\nX-Note: first: second\r\n\r\n{\"some\":\"content\"}\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000--\r\n--batchresponse_00000000-0000-0000-0000-000000000000--\r\n";

        HttpTest.RespondWith(
            response,
            202,
            new Dictionary<string, string> { { "Content-Type", "multipart/mixed;boundary=batchresponse_00000000-0000-0000-0000-000000000000" } });

        var batchResult = await SLConnectionV1.PostBatchAsync(new SLBatchRequest(HttpMethod.Post, "Orders", new { DocEntry = 1 }, 1));

        // An empty-value header must not fail the parse, and a value containing ': ' must not be truncated
        Assert.Single(batchResult);
        Assert.Equal(HttpStatusCode.Created, batchResult[0].StatusCode);
        Assert.Equal("first: second", batchResult[0].Content.Headers.GetValues("X-Note").Single());
    }
}
