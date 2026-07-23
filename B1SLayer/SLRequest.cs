using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace B1SLayer;

/// <summary>
///     Represents a request to the Service Layer, configured through its fluent methods
///     and executed through one of its HTTP methods (e.g. <see cref="GetAsync{T}" />, <see cref="PostAsync(object, CancellationToken)" />).
/// </summary>
public partial class SLRequest
{
    internal static readonly HttpMethod PatchMethod = new("PATCH");

    private readonly SLConnection _slConnection;

    internal SLRequest(SLConnection connection, string resource)
    {
        _slConnection = connection;
        Resource = resource;
    }


    /// <summary>
    ///     The resource this request is directed to, including the entity ID when applicable.
    /// </summary>
    internal string Resource { get; }

    /// <summary>
    ///     Extra path segments appended after the resource, such as "$count".
    /// </summary>
    internal List<string> ExtraPathSegments { get; } = [];

    /// <summary>
    ///     The query parameters to be sent, in insertion order.
    /// </summary>
    internal List<KeyValuePair<string, string>> QueryParams { get; } = [];

    /// <summary>
    ///     The headers to be sent, in insertion order.
    /// </summary>
    internal List<KeyValuePair<string, string>> Headers { get; } = [];

    /// <summary>
    ///     The timeout for this request, or null to use the connection's <see cref="SLConnection.DefaultRequestTimeout" />.
    /// </summary>
    internal TimeSpan? RequestTimeout { get; set; }

    /// <summary>
    ///     The unsuccessful HTTP status codes that should not result in a throw.
    /// </summary>
    internal HashSet<int> AllowedStatusCodes { get; } = [];

    /// <summary>
    ///     Whether any HTTP status code is allowed without resulting in a throw.
    /// </summary>
    internal bool AllowAnyStatusCode { get; set; }

    /// <summary>
    ///     The serializer options for this request, or null to use the connection's <see cref="SLConnection.JsonSerializerOptions" />.
    /// </summary>
    internal JsonSerializerOptions SerializerOptions { get; set; }

    /// <summary>
    ///     The effective serializer options for this request, honoring the request-level override.
    /// </summary>
    internal JsonSerializerOptions EffectiveSerializerOptions => SerializerOptions ?? _slConnection.JsonSerializerOptions;


    /// <summary>
    ///     Sets the clause to be used to filter records.
    /// </summary>
    public SLRequest Filter(string filter)
    {
        SetQueryParamValue("$filter", filter);
        return this;
    }

    /// <summary>
    ///     Sets the explicit properties that should be returned.
    /// </summary>
    public SLRequest Select(string select)
    {
        SetQueryParamValue("$select", select);
        return this;
    }

    /// <summary>
    ///     Sets the order in which entities should be returned.
    /// </summary>
    public SLRequest OrderBy(string orderBy)
    {
        SetQueryParamValue("$orderby", orderBy);
        return this;
    }

    /// <summary>
    ///     Sets the maximum number of first records to be included in the result.
    /// </summary>
    public SLRequest Top(int top)
    {
        SetQueryParamValue("$top", top.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    /// <summary>
    ///     Sets the number of first results to be excluded from the result.
    /// </summary>
    /// <remarks>
    ///     Where $top and $skip are used together, the $skip is applied before
    ///     the $top, regardless of the order of appearance in the request.
    ///     This can be used when implementing a pagination mechanism.
    /// </remarks>
    public SLRequest Skip(int skip)
    {
        SetQueryParamValue("$skip", skip.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    /// <summary>
    ///     Sets the aggregation expression.
    /// </summary>
    public SLRequest Apply(string apply)
    {
        SetQueryParamValue("$apply", apply);
        return this;
    }

    /// <summary>
    ///     Sets the navigation properties to be retrieved.
    /// </summary>
    public SLRequest Expand(string expand)
    {
        SetQueryParamValue("$expand", expand);
        return this;
    }

    /// <summary>
    ///     Sets a custom query parameter to be sent. A null value removes the parameter.
    /// </summary>
    public SLRequest SetQueryParam(string name, string value)
    {
        SetQueryParamValue(name, value);
        return this;
    }

    /// <summary>
    ///     Sets the page size when paging is applied for a query. The default value is 20.
    /// </summary>
    /// <param name="pageSize">
    ///     The page size to be defined for this request.
    /// </param>
    public SLRequest WithPageSize(int pageSize)
    {
        SetHeaderValue("B1S-PageSize", pageSize.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    /// <summary>
    ///     Enables a case-insensitive query.
    /// </summary>
    /// <remarks>
    ///     This is only applicable to SAP HANA databases, where every query is case-sensitive by default.
    /// </remarks>
    public SLRequest WithCaseInsensitive()
    {
        SetHeaderValue("B1S-CaseInsensitive", "true");
        return this;
    }

    /// <summary>
    ///     Allows a PATCH request to remove items in a collection.
    /// </summary>
    public SLRequest WithReplaceCollectionsOnPatch()
    {
        SetHeaderValue("B1S-ReplaceCollectionsOnPatch", "true");
        return this;
    }

    /// <summary>
    ///     Configures a POST request to not return the created entity.
    ///     This is suitable for better performance in demanding scenarios where the return content is not needed.
    /// </summary>
    /// <remarks>
    ///     On success, <see cref="HttpStatusCode.NoContent" /> is returned, instead of <see cref="HttpStatusCode.Created" />.
    /// </remarks>
    public SLRequest WithReturnNoContent()
    {
        SetHeaderValue("Prefer", "return-no-content");
        return this;
    }

    /// <summary>
    ///     Adds a custom request header to be sent. A null value removes the header.
    /// </summary>
    /// <param name="name">
    ///     The name of the header.
    /// </param>
    /// <param name="value">
    ///     The value of the header.
    /// </param>
    public SLRequest WithHeader(string name, object value)
    {
        // Convert.ToString(null) returns an empty string, which would defeat the removal-by-null contract
        SetHeaderValue(name, value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim());
        return this;
    }

    /// <summary>
    ///     Configures the request to not throw an exception when the response has any of the provided <see cref="HttpStatusCode" />.
    /// </summary>
    /// <remarks>
    ///     By default, every response with an unsuccessful <see cref="HttpStatusCode" /> (non-2XX) will result in a throw.
    /// </remarks>
    /// <param name="statusCodes">
    ///     The <see cref="HttpStatusCode" /> to be allowed.
    /// </param>
    public SLRequest AllowHttpStatus(params HttpStatusCode[] statusCodes)
    {
        foreach (var statusCode in statusCodes)
        {
            AllowedStatusCodes.Add((int)statusCode);
        }

        return this;
    }

    /// <summary>
    ///     Configures the request to allow a response with any <see cref="HttpStatusCode" /> without resulting in a throw.
    /// </summary>
    /// <remarks>
    ///     By default, every response with an unsuccessful <see cref="HttpStatusCode" /> (non-2XX) will result in a throw.
    /// </remarks>
    public SLRequest AllowAnyHttpStatus()
    {
        AllowAnyStatusCode = true;
        return this;
    }

    /// <summary>
    ///     Configures the JSON serializer to include null values for this request,
    ///     preserving any other settings configured at the request or connection level.
    /// </summary>
    public SLRequest IncludeNullValues()
    {
        SerializerOptions = new JsonSerializerOptions(EffectiveSerializerOptions)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        return this;
    }

    /// <summary>
    ///     Sets a custom <see cref="JsonSerializerOptions" /> to be used for this request.
    /// </summary>
    public SLRequest WithJsonSerializerOptions(JsonSerializerOptions jsonSerializerOptions)
    {
        SerializerOptions = jsonSerializerOptions;
        return this;
    }

    /// <summary>
    ///     Configures a custom timeout value for this request. The default timeout is 100 seconds.
    /// </summary>
    /// <param name="timeout">
    ///     A <see cref="TimeSpan" /> representing the timeout value to be configured.
    /// </param>
    public SLRequest WithTimeout(TimeSpan timeout)
    {
        if (timeout != Timeout.InfiniteTimeSpan && (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMilliseconds(int.MaxValue)))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        RequestTimeout = timeout;
        return this;
    }

    /// <summary>
    ///     Configures a custom timeout value for this request. The default timeout is 100 seconds.
    /// </summary>
    /// <param name="timeout">
    ///     An <see cref="int" /> representing the timeout in seconds to be configured.
    /// </param>
    public SLRequest WithTimeout(int timeout) => WithTimeout(TimeSpan.FromSeconds(timeout));
}
