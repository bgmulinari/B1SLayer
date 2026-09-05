using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace B1SLayer;

/// <summary>
///     Represents a single request to be sent in a batch to the Service Layer.
/// </summary>
public class SLBatchRequest
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SLBatchRequest" /> class, which represents the details of a request to be sent in a batch.
    /// </summary>
    public SLBatchRequest()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="SLBatchRequest" /> class, which represents the details of a request to be sent in a batch.
    /// </summary>
    /// <param name="httpMethod">
    ///     The HTTP method to be used for this request.
    /// </param>
    /// <param name="resource">
    ///     The Service Layer resource to be requested.
    /// </param>
    /// <param name="data">
    ///     The JSON body to be sent. It can be either an object to be serialized as JSON or a JSON string.
    /// </param>
    /// <param name="contentID">
    ///     Entity reference that can be used by subsequent requests to refer to a new entity created within the same change set.
    ///     This is optional for OData Version 3 (b1s/v1) but mandatory for OData Version 4 (b1s/v2).
    /// </param>
    public SLBatchRequest(HttpMethod httpMethod, string resource, object data = null, int? contentID = null)
    {
        HttpMethod = httpMethod;
        Resource = resource;
        Data = data;
        ContentID = contentID;
    }

    /// <summary>
    ///     Gets or sets the HTTP method to be used for this request.
    /// </summary>
    public HttpMethod HttpMethod { get; set; }

    /// <summary>
    ///     Gets or sets the Service Layer resource to be requested.
    /// </summary>
    public string Resource { get; set; }

    /// <summary>
    ///     Gets or sets the JSON body to be sent. It can be either an object to be serialized as JSON or a JSON string.
    /// </summary>
    public object Data { get; set; }

    /// <summary>
    ///     Gets or sets the Content-ID for this request, an entity reference that can be used by subsequent requests to refer to a new entity created within the same change set.
    ///     This is optional for OData Version 3 (b1s/v1) but mandatory for OData Version 4 (b1s/v2).
    /// </summary>
    public int? ContentID { get; set; }

    /// <summary>
    ///     Gets or sets the <see cref="System.Text.Encoding" /> to be used for this request. UTF8 will be used by default.
    /// </summary>
    public Encoding Encoding { get; set; } = Encoding.UTF8;

    /// <summary>
    ///     Gets or sets the <see cref="System.Text.Json.JsonSerializerOptions" /> to be used for this request.
    ///     When not set, the connection's <see cref="SLConnection.JsonSerializerOptions" /> is used.
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; }

    /// <summary>
    ///     Gets or sets the HTTP message version to be used for this request. Version 1.1 will be used by default.
    /// </summary>
    public Version HttpVersion { get; set; } = new(1, 1);

    /// <summary>
    ///     The HTTP headers to be sent in this request.
    /// </summary>
    internal HttpRequestHeaders Headers { get; } = CreateHeaders();

    /// <summary>
    ///     <see cref="HttpRequestHeaders" /> has no public constructor, so an instance is obtained from a throwaway request message.
    /// </summary>
    private static HttpRequestHeaders CreateHeaders()
    {
        using var message = new HttpRequestMessage();
        return message.Headers;
    }

    /// <summary>
    ///     Enables a case-insensitive query.
    /// </summary>
    /// <remarks>
    ///     This is only applicable to SAP HANA databases, where every query is case-sensitive by default.
    /// </remarks>
    public SLBatchRequest WithCaseInsensitive()
    {
        Headers.Add("B1S-CaseInsensitive", "true");
        return this;
    }

    /// <summary>
    ///     Allows a PATCH request to remove items in a collection.
    /// </summary>
    public SLBatchRequest WithReplaceCollectionsOnPatch()
    {
        Headers.Add("B1S-ReplaceCollectionsOnPatch", "true");
        return this;
    }

    /// <summary>
    ///     Configures a POST request to not return the created entity.
    ///     This is suitable for better performance in demanding scenarios where the return content is not needed.
    /// </summary>
    /// <remarks>
    ///     On success, <see cref="System.Net.HttpStatusCode.NoContent" /> is returned, instead of <see cref="System.Net.HttpStatusCode.Created" />.
    /// </remarks>
    public SLBatchRequest WithReturnNoContent()
    {
        Headers.Add("Prefer", "return-no-content");
        return this;
    }

    /// <summary>
    ///     Adds a custom request header to be sent.
    /// </summary>
    /// <param name="name">
    ///     The name of the header.
    /// </param>
    /// <param name="value">
    ///     The value of the header.
    /// </param>
    public SLBatchRequest WithHeader(string name, string value)
    {
        Headers.Add(name, value);
        return this;
    }
}
