using Flurl;
using Flurl.Http;
using Flurl.Http.Testing;
using System.IO.Enumeration;
using System.Net;

namespace B1SLayer.Test
{
    public class SLBatchRequestTests : TestBase
    {
        private const string
            expectedRequestBody = "*--*-*-*-*-*Content-Type: multipart/mixed; boundary=\"changeset_*-*-*-*-*\"*--changeset_*-*-*-*-*Content-Type: application/http; msgtype=request*content-transfer-encoding: binary*Content-ID: 1*POST /b1s/v*/BusinessPartners HTTP/1.1*Host: *:50000*Prefer: return-no-content*Content-Type: application/json; charset=utf-8*{\"CardCode\":\"C00001\",\"CardName\":\"I am a new BP\"}*--changeset_*-*-*-*-*Content-Type: application/http; msgtype=request*content-transfer-encoding: binary*Content-ID: 2*PATCH /b1s/v*/BusinessPartners('C00001') HTTP/1.1*Host: *:50000*Content-Type: application/json; charset=utf-8*{\"CardName\":\"This is my updated name\"}*--changeset_*-*-*-*-*Content-Type: application/http; msgtype=request*content-transfer-encoding: binary*Content-ID: 3*DELETE /b1s/v*/BusinessPartners('C00001') HTTP/1.1*Host: *:50000*--changeset_*-*-*-*-*--*--*-*-*-*-*--*",
            v1Response = "\r\n--batchresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: multipart/mixed; boundary=changesetresponse_00000000-0000-0000-0000-000000000000\r\n\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\n\r\nHTTP/1.1 204 No Content\r\nContent-ID: 1\r\nDataServiceVersion: 3.0\r\nLocation: https://127.0.0.1:50000/b1s/v1/BusinessPartners('C00001')\r\nPreference-Applied: return-no-content\r\n\r\n\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\n\r\nHTTP/1.1 204 No Content\r\nContent-ID: 2\r\nDataServiceVersion: 3.0\r\n\r\n\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\n\r\nHTTP/1.1 204 No Content\r\nContent-ID: 3\r\nDataServiceVersion: 3.0\r\n\r\n\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000--\r\n\r\n--batchresponse_00000000-0000-0000-0000-000000000000--\r\n",
            v2Response = "--batchresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: multipart/mixed; boundary=changesetresponse_00000000-0000-0000-0000-000000000000\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\nContent-ID: 1\r\n\r\nHTTP/1.1 204 No Content\r\nLocation: https://127.0.0.1:50000/b1s/v2/BusinessPartners('C00001')\r\nOData-Version: 4.0\r\nPreference-Applied: return-no-content\r\n\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\nContent-ID: 2\r\n\r\nHTTP/1.1 204 No Content\r\nOData-Version: 4.0\r\n\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\nContent-ID: 3\r\n\r\nHTTP/1.1 204 No Content\r\nOData-Version: 4.0\r\n\r\n\r\n--changesetresponse_00000000-0000-0000-0000-000000000000--\r\n--batchresponse_00000000-0000-0000-0000-000000000000--\r\n";

        private static readonly SLBatchRequest[] _requests = new[]
        {
            new SLBatchRequest(HttpMethod.Post, "BusinessPartners", new { CardCode = "C00001", CardName = "I am a new BP" }, 1).WithReturnNoContent(),
            new SLBatchRequest(HttpMethod.Patch, "BusinessPartners('C00001')", new { CardName = "This is my updated name" }, 2),
            new SLBatchRequest(HttpMethod.Delete, "BusinessPartners('C00001')", contentID: 3)
        };

        [Fact]
        public async void PostBatchAsyncV1_ReturnsCorrectData()
        {
            HttpTest.RespondWith(
                body: v1Response,
                status: 202,
                headers: new Dictionary<string, string> { { "Content-Type", "multipart/mixed;boundary=batchresponse_00000000-0000-0000-0000-000000000000" } });

            var batchResult = await SLConnectionV1.PostBatchAsync(_requests);

            HttpTest.ShouldHaveCalled(SLConnectionV1.ServiceLayerRoot.AppendPathSegment("$batch"))
                .WithVerb(HttpMethod.Post)
                .With(call => FileSystemName.MatchesSimpleExpression(expectedRequestBody, call.RequestBody))
                .Times(1);

            Assert.Equal(3, batchResult.Length);
            Assert.Equal(HttpStatusCode.NoContent, batchResult[0].StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, batchResult[1].StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, batchResult[2].StatusCode);
        }

        [Fact]
        public async void PostBatchAsyncV2_ReturnsCorrectData()
        {
            HttpTest.RespondWith(
                body: v2Response,
                status: 202,
                headers: new Dictionary<string, string> { { "Content-Type", "multipart/mixed;boundary=batchresponse_00000000-0000-0000-0000-000000000000" } });

            var batchResult = await SLConnectionV2.PostBatchAsync(_requests);

            HttpTest.ShouldHaveCalled(SLConnectionV2.ServiceLayerRoot.AppendPathSegment("$batch"))
                .WithVerb(HttpMethod.Post)
                .With(call => FileSystemName.MatchesSimpleExpression(expectedRequestBody, call.RequestBody))
                .Times(1);

            Assert.Equal(3, batchResult.Length);
            Assert.Equal(HttpStatusCode.NoContent, batchResult[0].StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, batchResult[1].StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, batchResult[2].StatusCode);
        }
    }
}
