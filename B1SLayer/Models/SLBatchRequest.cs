using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace B1SLayer
{
    /// <summary>
    /// Represents a single request to be sent in a batch to the Service Layer.
    /// </summary>
    public class SLBatchRequest
    {
        /// <summary>
        /// Gets or sets the HTTP method to be used for this request.
        /// </summary>
        public HttpMethod HttpMethod { get; set; }
        /// <summary>
        /// Gets or sets the Service Layer resource to be requested.
        /// </summary>
        public string Resource { get; set; }
        /// <summary>
        /// Gets or sets the JSON body to be sent. It can be either an object to be serialized as JSON or a JSON string.
        /// </summary>
        public object Data { get; set; }
        /// <summary>
        /// Gets or sets the Content-ID for this request, an entity reference that can be used by subsequent requests to refer to a new entity created within the same change set.
        /// This is optional for OData Version 3 (b1s/v1) but mandatory for OData Version 4 (b1s/v2).
        /// </summary>
        public int? ContentID { get; set; }
        /// <summary>
        /// Gets or sets the <see cref="System.Text.Encoding"/> to be used for this request. UTF8 will be used by default.
        /// </summary>
        public Encoding Encoding { get; set; } = Encoding.UTF8;
        /// <summary>
        /// Gets or sets the <see cref="Newtonsoft.Json.JsonSerializerSettings"/> to be used for this request.
        /// By default it is configured so the <see cref="JsonSerializerSettings.NullValueHandling"/> is set to <see cref="NullValueHandling.Ignore"/>.
        /// </summary>
        public JsonSerializerSettings JsonSerializerSettings { get; set; } = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
        /// <summary>
        /// Gets or sets the HTTP message version to be used for this request. Version 1.1 will be used by default.
        /// </summary>
        public Version HttpVersion { get; set; } = new Version(1, 1);
        /// <summary>
        /// The HTTP headers to be sent in this request.
        /// </summary>
        internal HttpRequestHeaders Headers { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SLBatchRequest"/> class, which represents the details of a request to be sent in a batch.
        /// </summary>
        public SLBatchRequest() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="SLBatchRequest"/> class, which represents the details of a request to be sent in a batch.
        /// </summary>
        /// <param name="httpMethod">
        /// The HTTP method to be used for this request.
        /// </param>
        /// <param name="resource">
        /// The Service Layer resource to be requested.
        /// </param>
        /// <param name="data">
        /// The JSON body to be sent. It can be either an object to be serialized as JSON or a JSON string.
        /// </param>
        /// <param name="contentID">
        /// Entity reference that can be used by subsequent requests to refer to a new entity created within the same change set.
        /// This is optional for OData Version 3 (b1s/v1) but mandatory for OData Version 4 (b1s/v2).
        /// </param>
        public SLBatchRequest(HttpMethod httpMethod, string resource, object data = null, int? contentID = null)
        {
            HttpMethod = httpMethod;
            Resource = resource;
            Data = data;
            ContentID = contentID;

            using (var message = new HttpRequestMessage())
            {
                Headers = message.Headers;
            }
        }
    }
}