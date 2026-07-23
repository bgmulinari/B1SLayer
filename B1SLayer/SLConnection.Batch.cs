using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer;

public partial class SLConnection
{
    /// <summary>
    ///     Sends a batch request (multiple operations sent in a single HTTP request) with the provided <see cref="SLBatchRequest" /> instances.
    ///     All requests are sent in a single change set.
    /// </summary>
    /// <remarks>
    ///     See section 'Batch Operations' in the Service Layer User Manual for more details.
    /// </remarks>
    /// <param name="requests">
    ///     <see cref="SLBatchRequest" /> instances to be sent in the batch.
    /// </param>
    /// <returns>
    ///     An <see cref="HttpResponseMessage" /> array containg the response messages of the batch request.
    /// </returns>
    public Task<HttpResponseMessage[]> PostBatchAsync(params SLBatchRequest[] requests) => PostBatchAsync(requests, true);

    /// <summary>
    ///     Sends a batch request (multiple operations sent in a single HTTP request) with the provided <see cref="SLBatchRequest" /> collection.
    /// </summary>
    /// <remarks>
    ///     See section 'Batch Operations' in the Service Layer User Manual for more details.
    /// </remarks>
    /// <param name="requests">
    ///     A collection of <see cref="SLBatchRequest" /> to be sent in the batch.
    /// </param>
    /// <param name="singleChangeSet">
    ///     Whether all the requests in this batch should be sent in a single change set. This means that any unsuccessful request will cause the whole batch to be rolled back.
    /// </param>
    /// <returns>
    ///     An <see cref="HttpResponseMessage" /> array containg the response messages of the batch request.
    /// </returns>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public async Task<HttpResponseMessage[]> PostBatchAsync(IEnumerable<SLBatchRequest> requests, bool singleChangeSet = true, CancellationToken cancellationToken = default)
    {
        if (requests == null)
        {
            throw new ArgumentNullException(nameof(requests));
        }

        var slBatchRequests = requests.ToList();

        if (slBatchRequests.Count == 0)
        {
            throw new ArgumentException("No requests to be sent.", nameof(requests));
        }

        return await ExecuteRequestAsync(
                async (sessionCookies, innerCancellationToken) =>
                {
                    var batchContents = await BuildBatchContentsAsync(slBatchRequests, singleChangeSet, innerCancellationToken).ConfigureAwait(false);
                    return CreateResourceRequestMessage(HttpMethod.Post,
                        "$batch",
                        sessionCookies,
                        MultipartHelper.CreateMultipartContent("mixed", batchContents));
                },
                async batchResponse =>
                {
                    if (batchResponse?.Content is null)
                    {
                        throw new Exception("The batch request did not return a valid response.");
                    }

                    return await MultipartHelper.ReadMultipartResponseAsync(batchResponse).ConfigureAwait(false);
                },
                requestTimeout: BatchRequestTimeout,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Builds the list of <see cref="HttpContent" /> parts for a batch request.
    ///     GET/HEAD requests are placed directly in the batch body, while mutation requests are wrapped in changesets.
    /// </summary>
    private async Task<List<HttpContent>> BuildBatchContentsAsync(IEnumerable<SLBatchRequest> requests, bool singleChangeSet, CancellationToken cancellationToken = default)
    {
        var parts = new List<HttpContent>();

        if (singleChangeSet)
        {
            // Collect consecutive mutations into a single changeset.
            // A query (GET/HEAD) between mutations closes the current changeset to preserve request ordering.
            MultipartContent changeset = null;

            foreach (var batchRequest in requests)
            {
                var httpContent = await BuildHttpContentFromBatchRequestAsync(batchRequest, cancellationToken).ConfigureAwait(false);

                if (IsQueryMethod(batchRequest.HttpMethod))
                {
                    // Detach from current changeset so the next mutation starts a new one
                    changeset = null;
                    parts.Add(httpContent);
                }
                else
                {
                    if (changeset == null)
                    {
                        changeset = new MultipartContent("mixed", "changeset_" + Guid.NewGuid());
                        parts.Add(changeset);
                    }

                    changeset.Add(httpContent);
                }
            }
        }
        else
        {
            foreach (var batchRequest in requests)
            {
                var httpContent = await BuildHttpContentFromBatchRequestAsync(batchRequest, cancellationToken).ConfigureAwait(false);

                if (IsQueryMethod(batchRequest.HttpMethod))
                {
                    parts.Add(httpContent);
                }
                else
                {
                    var changeset = new MultipartContent("mixed", "changeset_" + Guid.NewGuid());
                    changeset.Add(httpContent);
                    parts.Add(changeset);
                }
            }
        }

        return parts;
    }

    /// <summary>
    ///     Builds an <see cref="HttpContent" /> from a given <see cref="SLBatchRequest" />.
    /// </summary>
    private async Task<HttpContent> BuildHttpContentFromBatchRequestAsync(SLBatchRequest batchRequest, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(batchRequest.HttpMethod,
            SLUrl.Combine(ServiceLayerRoot.ToString(), batchRequest.Resource));

        if (batchRequest.HttpVersion != null)
        {
            request.Version = batchRequest.HttpVersion;
        }

        foreach (var header in batchRequest.Headers)
        {
            request.Headers.Add(header.Key, header.Value);
        }

        if (batchRequest.Data != null)
        {
            // Batch bodies honor the same serializer resolution as regular requests:
            // the per-request override when set, the connection-level options otherwise
            request.Content = batchRequest.Data is string dataString
                ? new StringContent(dataString, batchRequest.Encoding, "application/json")
                : new StringContent(JsonSerializer.Serialize(batchRequest.Data, batchRequest.JsonSerializerOptions ?? JsonSerializerOptions),
                    batchRequest.Encoding,
                    "application/json");
        }

        var innerContent = await MultipartHelper.CreateHttpContentAsync(request, cancellationToken).ConfigureAwait(false);
        innerContent.Headers.Add("content-transfer-encoding", "binary");

        if (batchRequest.ContentID.HasValue)
        {
            innerContent.Headers.Add("Content-ID", batchRequest.ContentID.ToString());
        }

        return innerContent;
    }

    /// <summary>
    ///     Determines whether the given HTTP method is a query (non-mutation) method.
    /// </summary>
    private static bool IsQueryMethod(HttpMethod method) => method == HttpMethod.Get || method == HttpMethod.Head;
}
