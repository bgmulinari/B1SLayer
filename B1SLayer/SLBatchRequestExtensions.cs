using System.Net;

namespace B1SLayer
{
    public static class SLBatchRequestExtensions
    {
        /// <summary>
        /// Enables a case-insensitive query.
        /// </summary>
        /// <remarks>
        /// This is only applicable to SAP HANA databases, where every query is case-sensitive by default.
        /// </remarks>
        public static SLBatchRequest WithCaseInsensitive(this SLBatchRequest batchRequest)
        {
            batchRequest.Headers.Add("B1S-CaseInsensitive", "true");
            return batchRequest;
        }

        /// <summary>
        /// Allows a PATCH request to remove items in a collection.
        /// </summary>
        public static SLBatchRequest WithReplaceCollectionsOnPatch(this SLBatchRequest batchRequest)
        {
            batchRequest.Headers.Add("B1S-ReplaceCollectionsOnPatch", "true");
            return batchRequest;
        }

        /// <summary>
        /// Configures a POST request to not return the created entity.
        /// This is suitable for better performance in demanding scenarios where the return content is not needed.
        /// </summary>
        /// <remarks>
        /// On success, <see cref="HttpStatusCode.NoContent"/> is returned, instead of <see cref="HttpStatusCode.Created"/>.
        /// </remarks>
        public static SLBatchRequest WithReturnNoContent(this SLBatchRequest batchRequest)
        {
            batchRequest.Headers.Add("Prefer", "return-no-content");
            return batchRequest;
        }

        /// <summary>
        /// Adds a custom request header to be sent.
        /// </summary>
        /// <param name="name">
        /// The name of the header.
        /// </param>
        /// <param name="value">
        /// The value of the header.
        /// </param>
        public static SLBatchRequest WithHeader(this SLBatchRequest batchRequest, string name, string value)
        {
            batchRequest.Headers.Add(name, value);
            return batchRequest;
        }
    }
}
