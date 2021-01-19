using Flurl.Http;
using Flurl.Http.Configuration;
using Newtonsoft.Json;
using System;
using System.Net;

namespace B1SLayer
{
    public static class SLRequestExtensions
    {
        /// <summary>
        /// Sets the clause to be used to filter records.
        /// </summary>
        public static SLRequest Filter(this SLRequest request, string filter)
        {
            request.FlurlRequest.SetQueryParam("$filter", filter);
            return request;
        }

        /// <summary>
        /// Sets the explicit properties that should be returned.
        /// </summary>
        public static SLRequest Select(this SLRequest request, string select)
        {
            request.FlurlRequest.SetQueryParam("$select", select);
            return request;
        }

        /// <summary>
        /// Sets the order in which entities should be returned.
        /// </summary>
        public static SLRequest OrderBy(this SLRequest request, string orderBy)
        {
            request.FlurlRequest.SetQueryParam("$orderby", orderBy);
            return request;
        }

        /// <summary>
        /// Sets the maximum number of first records to be included in the result.
        /// </summary>
        public static SLRequest Top(this SLRequest request, int top)
        {
            request.FlurlRequest.SetQueryParam("$top", top);
            return request;
        }

        /// <summary>
        /// Sets the number of first results to be excluded from the result.
        /// </summary>
        /// <remarks>
        /// Where $top and $skip are used together, the $skip is applied before 
        /// the $top, regardless of the order of appearance in the request.
        /// This can be used when implementing a pagination mechanism.
        /// </remarks>
        public static SLRequest Skip(this SLRequest request, int skip)
        {
            request.FlurlRequest.SetQueryParam("$skip", skip);
            return request;
        }

        /// <summary>
        /// Sets the aggregation expression.
        /// </summary>
        public static SLRequest Apply(this SLRequest request, string apply)
        {
            request.FlurlRequest.SetQueryParam("$apply", apply);
            return request;
        }

        /// <summary>
        /// Sets the navigation properties to be retrieved.
        /// </summary>
        public static SLRequest Expand(this SLRequest request, string expand)
        {
            request.FlurlRequest.SetQueryParam("$expand", expand);
            return request;
        }

        /// <summary>
        /// Sets a custom query parameter to be sent.
        /// </summary>
        public static SLRequest SetQueryParam(this SLRequest request, string name, string value)
        {
            request.FlurlRequest.SetQueryParam(name, value);
            return request;
        }

        /// <summary>
        /// Sets the page size when paging is applied for a query. The default value is 20.
        /// </summary>
        /// <param name="pageSize">
        /// The page size to be defined for this request.
        /// </param>
        public static SLRequest WithPageSize(this SLRequest request, int pageSize)
        {
            request.FlurlRequest.WithHeader("B1S-PageSize", pageSize);
            return request;
        }

        /// <summary>
        /// Enables a case-insensitive query.
        /// </summary>
        /// <remarks>
        /// This is only applicable to SAP HANA databases, where every query is case-sensitive by default.
        /// </remarks>
        public static SLRequest WithCaseInsensitive(this SLRequest request)
        {
            request.FlurlRequest.WithHeader("B1S-CaseInsensitive", "true");
            return request;
        }

        /// <summary>
        /// Allows a PATCH request to remove items in a collection.
        /// </summary>
        public static SLRequest WithReplaceCollectionsOnPatch(this SLRequest request)
        {
            request.FlurlRequest.WithHeader("B1S-ReplaceCollectionsOnPatch", "true");
            return request;
        }

        /// <summary>
        /// Configures a POST request to not return the created entity.
        /// This is suitable for better performance in demanding scenarios where the return content is not needed.
        /// </summary>
        /// <remarks>
        /// On success, <see cref="HttpStatusCode.NoContent"/> is returned, instead of <see cref="HttpStatusCode.Created"/>.
        /// </remarks>
        public static SLRequest WithReturnNoContent(this SLRequest request)
        {
            request.FlurlRequest.WithHeader("Prefer", "return-no-content");
            return request;
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
        public static SLRequest WithHeader(this SLRequest request, string name, object value)
        {
            request.FlurlRequest.WithHeader(name, value);
            return request;
        }

        /// <summary>
        /// Configures the request to not throw an exception when the response has any of the provided <see cref="HttpStatusCode"/>.
        /// </summary>
        /// <remarks>
        /// By default, every reponse with an unsuccessful <see cref="HttpStatusCode"/> (non-2XX) will result in a throw.
        /// </remarks>
        /// <param name="statusCodes">
        /// The <see cref="HttpStatusCode"/> to be allowed.
        /// </param>
        public static SLRequest AllowHttpStatus(this SLRequest request, params HttpStatusCode[] statusCodes)
        {
            request.FlurlRequest.AllowHttpStatus(statusCodes);
            return request;
        }

        /// <summary>
        /// Configures the request to allow a response with any <see cref="HttpStatusCode"/> without resulting in a throw.
        /// </summary>
        /// <remarks>
        /// By default, every reponse with an unsuccessful <see cref="HttpStatusCode"/> (non-2XX) will result in a throw.
        /// </remarks>
        public static SLRequest AllowAnyHttpStatus(this SLRequest request)
        {
            request.FlurlRequest.AllowAnyHttpStatus();
            return request;
        }

        /// <summary>
        /// Configures the JSON serializer to include null values (<see cref="NullValueHandling.Include"/>) for this request.
        /// The default value is <see cref="NullValueHandling.Ignore"/>.
        /// </summary>
        public static SLRequest IncludeNullValues(this SLRequest request)
        {
            request.FlurlRequest.ConfigureRequest(settings =>
            {
                settings.JsonSerializer = new NewtonsoftJsonSerializer(new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Include
                });
            });

            return request;
        }

        /// <summary>
        /// Sets a custom <see cref="JsonSerializerSettings"/> to be used for this request.
        /// </summary>
        public static SLRequest WithJsonSerializerSettings(this SLRequest request, JsonSerializerSettings jsonSerializerSettings)
        {
            request.FlurlRequest.ConfigureRequest(settings =>
            {
                settings.JsonSerializer = new NewtonsoftJsonSerializer(jsonSerializerSettings);
            });

            return request;
        }

        /// <summary>
        /// Configures a custom timeout value for this request. The default timeout is 100 seconds.
        /// </summary>
        /// <param name="timeout">
        /// A <see cref="TimeSpan"/> representing the timeout value to be configured.
        /// </param>
        public static SLRequest WithTimeout(this SLRequest request, TimeSpan timeout)
        {
            request.FlurlRequest.WithTimeout(timeout);
            return request;
        }

        /// <summary>
        /// Configures a custom timeout value for this request. The default timeout is 100 seconds.
        /// </summary>
        /// <param name="timeout">
        /// An <see cref="int"/> representing the timeout in seconds to be configured.
        /// </param>
        public static SLRequest WithTimeout(this SLRequest request, int timeout)
        {
            request.FlurlRequest.WithTimeout(timeout);
            return request;
        }
    }
}