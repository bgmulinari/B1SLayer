using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using B1SLayer.Models;

namespace B1SLayer;

public partial class SLRequest
{
    /// <summary>
    ///     Performs a GET request with the provided parameters and returns the result in a new instance of the specified type.
    /// </summary>
    /// <typeparam name="T">
    ///     The object type for the result to be deserialized into.
    /// </typeparam>
    /// <param name="unwrapCollection">
    ///     Whether the result should be unwrapped from the 'value' JSON array in case it is a collection.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<T> GetAsync<T>(bool unwrapCollection = true, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Get,
            null,
            null,
            response => DeserializeResponseAsync<T>(response, unwrapCollection, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a GET request with the provided parameters and returns the result in a value tuple containing the deserialized result and the count of matching resources.
    /// </summary>
    /// <typeparam name="T">
    ///     The object type for the result to be deserialized into.
    /// </typeparam>
    /// <param name="unwrapCollection">
    ///     Whether the result should be unwrapped from the 'value' JSON array in case it is a collection.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<(T Result, int Count)> GetWithInlineCountAsync<T>(bool unwrapCollection = true, CancellationToken cancellationToken = default)
    {
        SetQueryParamValue("$inlinecount", "allpages");

        return SendAsync(HttpMethod.Get,
            null,
            null,
            async response =>
            {
                using var jsonDocument = await ParseResponseAsync(response, cancellationToken).ConfigureAwait(false);
                var root = jsonDocument.RootElement;
                var inlineCount = 0;

                // The count is extracted before deserialization so it is returned for every result type, including string
                if (root.ValueKind == JsonValueKind.Object
                    && (root.TryGetProperty("odata.count", out var inlineCountElement) || root.TryGetProperty("@odata.count", out inlineCountElement)))
                {
                    switch (inlineCountElement.ValueKind)
                    {
                        case JsonValueKind.Number:
                            inlineCount = inlineCountElement.GetInt32();
                            break;
                        case JsonValueKind.String:
                            inlineCount = int.TryParse(inlineCountElement.GetString(), out var inlineCountElementIntValue) ? inlineCountElementIntValue : 0;
                            break;
                        default:
                            throw new Exception("Inline count is not a number or string");
                    }
                }

                return (DeserializeRoot<T>(root, unwrapCollection), inlineCount);
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs multiple GET requests until all entities in a collection are obtained. The result will always be unwrapped from the 'value' array.
    /// </summary>
    /// <remarks>
    ///     This can be very slow depending on the total amount of entities in the company database.
    /// </remarks>
    /// <typeparam name="T">
    ///     The object type for the result to be deserialized into.
    /// </typeparam>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    ///     An <see cref="IList{T}" /> containing all the entities in the given collection.
    /// </returns>
    public async Task<IList<T>> GetAllAsync<T>(CancellationToken cancellationToken = default)
    {
        var allResultsList = new List<T>();
        var skip = 0;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetQueryParamValue("$skip", skip.ToString(CultureInfo.InvariantCulture));

            await SendAsync(HttpMethod.Get,
                    null,
                    null,
                    async response =>
                    {
                        var responseStream = await response.ReadJsonStreamAsync(cancellationToken).ConfigureAwait(false);
                        var currentResult = await JsonSerializer.DeserializeAsync<SLCollectionRoot<T>>(responseStream, EffectiveSerializerOptions, cancellationToken).ConfigureAwait(false);

                        allResultsList.AddRange(currentResult.Value);
                        skip = currentResult.NextSkip;
                        return 0;
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        } while (skip > 0);

        return allResultsList;
    }

    /// <summary>
    ///     Performs a GET request with the provided parameters and returns the result in a <see cref="string" />.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<string> GetStringAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Get,
            null,
            null,
            response => response.ReadStringAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a GET request with the provided parameters and returns the result in an instance of the given anonymous type.
    /// </summary>
    /// <param name="anonymousTypeObject">
    ///     The anonymous type object.
    /// </param>
    /// <param name="jsonSerializerOptions">
    ///     The <see cref="JsonSerializerOptions" /> used to deserialize the object. When not provided,
    ///     the serializer configured at the request or connection level is used.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<T> GetAnonymousTypeAsync<T>(T anonymousTypeObject, JsonSerializerOptions jsonSerializerOptions = null, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Get,
            null,
            null,
            async response =>
            {
                var responseStream = await response.ReadJsonStreamAsync(cancellationToken).ConfigureAwait(false);
                return await JsonSerializer.DeserializeAsync<T>(responseStream, jsonSerializerOptions ?? EffectiveSerializerOptions, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a GET request with the provided parameters and returns the result in a <see cref="byte" /> array.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<byte[]> GetBytesAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Get,
            null,
            null,
            response => response.ReadBytesAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a GET request with the provided parameters and returns the result in a <see cref="Stream" />.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<Stream> GetStreamAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Get,
            null,
            null,
            response => response.ReadStreamAsync(cancellationToken),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    /// <summary>
    ///     Performs a GET request that returns the count of an entity collection.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<long> GetCountAsync(CancellationToken cancellationToken = default)
    {
        ExtraPathSegments.Add("$count");

        return SendAsync(HttpMethod.Get,
            null,
            null,
            async response =>
            {
                var result = await response.ReadStringAsync(cancellationToken).ConfigureAwait(false);
                long.TryParse(result, out var quantity);
                return quantity;
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a POST request with the provided parameters and returns the result in the specified <see cref="Type" />.
    /// </summary>
    /// <param name="data">
    ///     The object to be sent as the JSON body.
    /// </param>
    /// <typeparam name="T">
    ///     The object type for the result to be deserialized into.
    /// </typeparam>
    /// <param name="unwrapCollection">
    ///     Whether the result should be unwrapped from the 'value' JSON array in case it is a collection.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<T> PostAsync<T>(object data, bool unwrapCollection = true, CancellationToken cancellationToken = default)
    {
        var body = SerializeBody(data);

        return SendAsync(HttpMethod.Post,
            () => HttpExtensions.CreateJsonContent(body),
            body,
            response => DeserializeResponseAsync<T>(response, unwrapCollection, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a POST request with the provided parameters and returns the result in the specified <see cref="Type" />.
    /// </summary>
    /// <param name="data">
    ///     The JSON string to be sent as the request body.
    /// </param>
    /// <typeparam name="T">
    ///     The object type for the result to be deserialized into.
    /// </typeparam>
    /// <param name="unwrapCollection">
    ///     Whether the result should be unwrapped from the 'value' JSON array in case it is a collection.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<T> PostStringAsync<T>(string data, bool unwrapCollection = true, CancellationToken cancellationToken = default)
    {
        var body = GetBodyBytes(data);

        return SendAsync(HttpMethod.Post,
            () => HttpExtensions.CreateJsonContent(body),
            body,
            response => DeserializeResponseAsync<T>(response, unwrapCollection, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a POST request with the provided parameters and returns the result in the specified <see cref="Type" />.
    /// </summary>
    /// <typeparam name="T">
    ///     The object type for the result to be deserialized into.
    /// </typeparam>
    /// <param name="unwrapCollection">
    ///     Whether the result should be unwrapped from the 'value' JSON array in case it is a collection.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<T> PostAsync<T>(bool unwrapCollection = true, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Post,
            null,
            null,
            response => DeserializeResponseAsync<T>(response, unwrapCollection, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a POST request with the provided parameters.
    /// </summary>
    /// <param name="data">
    ///     The object to be sent as the JSON body.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task PostAsync(object data, CancellationToken cancellationToken = default)
    {
        var body = SerializeBody(data);
        return SendAsync(HttpMethod.Post, () => HttpExtensions.CreateJsonContent(body), body, IgnoreResponse, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a POST request without parameters and returns the result in a <see cref="string" />.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<string> PostReceiveStringAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Post,
            null,
            null,
            response => response.ReadStringAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a POST request with the provided parameters and returns the result in a <see cref="string" />.
    /// </summary>
    /// <param name="data">
    ///     The object to be sent as the JSON body.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<string> PostReceiveStringAsync(object data, CancellationToken cancellationToken = default)
    {
        var body = SerializeBody(data);

        return SendAsync(HttpMethod.Post,
            () => HttpExtensions.CreateJsonContent(body),
            body,
            response => response.ReadStringAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a POST request with the provided parameters.
    /// </summary>
    /// <param name="data">
    ///     The JSON string to be sent as the request body.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task PostStringAsync(string data, CancellationToken cancellationToken = default)
    {
        var body = GetBodyBytes(data);
        return SendAsync(HttpMethod.Post, () => HttpExtensions.CreateJsonContent(body), body, IgnoreResponse, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a POST request with the provided parameters.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task PostAsync(CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, null, null, IgnoreResponse, cancellationToken: cancellationToken);

    /// <summary>
    ///     Performs a PATCH request with the provided parameters.
    /// </summary>
    /// <param name="data">
    ///     The object to be sent as the JSON body.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task PatchAsync(object data, CancellationToken cancellationToken = default)
    {
        var body = SerializeBody(data);
        return SendAsync(PatchMethod, () => HttpExtensions.CreateJsonContent(body), body, IgnoreResponse, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a PATCH request with the provided parameters.
    /// </summary>
    /// <param name="data">
    ///     The JSON string to be sent as the request body.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task PatchStringAsync(string data, CancellationToken cancellationToken = default)
    {
        var body = GetBodyBytes(data);
        return SendAsync(PatchMethod, () => HttpExtensions.CreateJsonContent(body), body, IgnoreResponse, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a PATCH request with the provided file.
    /// </summary>
    /// <param name="path">
    ///     The path to the file to be sent.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task PatchWithFileAsync(string path, CancellationToken cancellationToken = default) => PatchWithFileAsync(Path.GetFileName(path), File.ReadAllBytes(path), cancellationToken);

    /// <summary>
    ///     Performs a PATCH request with the provided file.
    /// </summary>
    /// <param name="fileName">
    ///     The file name of the file including the file extension.
    /// </param>
    /// <param name="file">
    ///     The file to be sent.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task PatchWithFileAsync(string fileName, byte[] file, CancellationToken cancellationToken = default) => SendFilesAsync(PatchMethod, [new KeyValuePair<string, byte[]>(fileName, file)], IgnoreResponse, cancellationToken);

    /// <summary>
    ///     Performs a PATCH request with the provided file.
    /// </summary>
    /// <param name="fileName">
    ///     The file name of the file including the file extension.
    /// </param>
    /// <param name="file">
    ///     The file to be sent.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public async Task PatchWithFileAsync(string fileName, Stream file, CancellationToken cancellationToken = default)
    {
        // The stream is buffered upfront so the multipart content can be rebuilt in case the request is retried
        var fileBytes = await file.ToByteArrayAsync(cancellationToken).ConfigureAwait(false);
        await PatchWithFileAsync(fileName, fileBytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Performs a PUT request with the provided parameters.
    /// </summary>
    /// <param name="data">
    ///     The object to be sent as the JSON body.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task PutAsync(object data, CancellationToken cancellationToken = default)
    {
        var body = SerializeBody(data);
        return SendAsync(HttpMethod.Put, () => HttpExtensions.CreateJsonContent(body), body, IgnoreResponse, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a PUT request with the provided parameters.
    /// </summary>
    /// <param name="data">
    ///     The JSON string to be sent as the request body.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task PutStringAsync(string data, CancellationToken cancellationToken = default)
    {
        var body = GetBodyBytes(data);
        return SendAsync(HttpMethod.Put, () => HttpExtensions.CreateJsonContent(body), body, IgnoreResponse, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Performs a DELETE request with the provided parameters.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task DeleteAsync(CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, null, null, IgnoreResponse, cancellationToken: cancellationToken);


    /// <summary>
    ///     Sets a query parameter, replacing an existing one with the same name. A null value removes the parameter.
    /// </summary>
    private void SetQueryParamValue(string name, string value)
    {
        var index = QueryParams.FindIndex(x => string.Equals(x.Key, name, StringComparison.Ordinal));

        if (value == null)
        {
            if (index >= 0)
            {
                QueryParams.RemoveAt(index);
            }
        }
        else if (index >= 0)
        {
            QueryParams[index] = new KeyValuePair<string, string>(name, value);
        }
        else
        {
            QueryParams.Add(new KeyValuePair<string, string>(name, value));
        }
    }

    /// <summary>
    ///     Sets a header, replacing an existing one with the same name. A null value removes the header.
    /// </summary>
    private void SetHeaderValue(string name, string value)
    {
        var index = Headers.FindIndex(x => string.Equals(x.Key, name, StringComparison.OrdinalIgnoreCase));

        if (value == null)
        {
            if (index >= 0)
            {
                Headers.RemoveAt(index);
            }
        }
        else if (index >= 0)
        {
            Headers[index] = new KeyValuePair<string, string>(name, value);
        }
        else
        {
            Headers.Add(new KeyValuePair<string, string>(name, value));
        }
    }

    /// <summary>
    ///     Determines whether the given unsuccessful status code was explicitly allowed for this request.
    /// </summary>
    internal bool IsAllowedStatus(HttpStatusCode statusCode) => AllowAnyStatusCode || AllowedStatusCodes.Contains((int)statusCode);

    /// <summary>
    ///     Builds the request URI from the connection root, resource, extra path segments and query parameters.
    /// </summary>
    internal Uri BuildUri()
    {
        var url = SLUrl.AppendPathSegment(_slConnection.ServiceLayerRoot.ToString(), Resource);

        foreach (var pathSegment in ExtraPathSegments)
        {
            url = SLUrl.AppendPathSegment(url, pathSegment);
        }

        if (QueryParams.Count > 0)
        {
            url += "?" + SLUrl.BuildQueryString(QueryParams);
        }

        return new Uri(url);
    }

    /// <summary>
    ///     Builds a fresh <see cref="HttpRequestMessage" /> for this request.
    ///     A new message must be built for each attempt, as they can not be reused.
    /// </summary>
    internal HttpRequestMessage BuildRequestMessage(HttpMethod method, HttpContent content, SLCookieJar sessionCookies)
    {
        var requestMessage = new HttpRequestMessage(method, BuildUri()) { Content = content };

        foreach (var header in Headers)
        {
            if (content != null && header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                content.Headers.Remove(header.Key);
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            else
            {
                requestMessage.Headers.Remove(header.Key);
                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // A user-set Cookie header is merged with the session cookies, with the user's cookies taking precedence
        SLConnection.AttachSessionCookies(requestMessage, sessionCookies);

        return requestMessage;
    }

    /// <summary>
    ///     Performs a multipart form-data request with the provided files through the retry pipeline.
    /// </summary>
    internal Task<T> SendFilesAsync<T>(HttpMethod method, IReadOnlyCollection<KeyValuePair<string, byte[]>> files,
        Func<HttpResponseMessage, Task<T>> responseHandler, CancellationToken cancellationToken)
    {
        return SendAsync(method,
            () => MultipartHelper.CreateMultipartContent("form-data", files.Select(x => MultipartHelper.CreateFilePart(x.Key, x.Value))),
            null,
            responseHandler,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Executes this request through the connection's retry pipeline, building a fresh request message for each attempt.
    /// </summary>
    private Task<T> SendAsync<T>(HttpMethod method, Func<HttpContent> contentFactory, byte[] capturedRequestBody,
        Func<HttpResponseMessage, Task<T>> responseHandler, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancellationToken = default)
    {
        return _slConnection.ExecuteRequestAsync(
            (sessionCookies, _) => Task.FromResult(BuildRequestMessage(method, contentFactory?.Invoke(), sessionCookies)),
            responseHandler,
            IsAllowedStatus,
            completionOption,
            RequestTimeout,
            capturedRequestBody,
            cancellationToken);
    }

    /// <summary>
    ///     Serializes the given object as a UTF-8 JSON body through the effective serializer.
    /// </summary>
    private byte[] SerializeBody(object data) => JsonSerializer.SerializeToUtf8Bytes(data, EffectiveSerializerOptions);

    private static byte[] GetBodyBytes(string data) => Encoding.UTF8.GetBytes(data ?? string.Empty);

    private static Task<int> IgnoreResponse(HttpResponseMessage response) => Task.FromResult(0);

    /// <summary>
    ///     Parses the response body directly from the response stream, avoiding an intermediate string.
    /// </summary>
    private async Task<JsonDocument> ParseResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var responseStream = await response.ReadJsonStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(responseStream, GetResponseDocumentOptions(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Parses and deserializes the response body through the effective serializer,
    ///     unwrapping the OData 'value' collection when requested.
    /// </summary>
    private async Task<T> DeserializeResponseAsync<T>(HttpResponseMessage response, bool unwrapCollection, CancellationToken cancellationToken)
    {
        using var jsonDocument = await ParseResponseAsync(response, cancellationToken).ConfigureAwait(false);
        return DeserializeRoot<T>(jsonDocument.RootElement, unwrapCollection);
    }

    private T DeserializeRoot<T>(JsonElement root, bool unwrapCollection)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return DeserializeElement<T>(root);
        }

        if (typeof(T) == typeof(string))
        {
            return (T)(object)root.GetRawText();
        }

        var element = unwrapCollection && root.TryGetProperty("value", out var valueCollection)
            ? valueCollection
            : root;

        return DeserializeElement<T>(element);
    }

    private T DeserializeElement<T>(JsonElement element) => element.Deserialize<T>(EffectiveSerializerOptions);

    /// <summary>
    ///     Carries the reader-affecting serializer options into the initial JsonDocument parse,
    ///     so options like AllowTrailingCommas are honored before deserialization runs.
    /// </summary>
    private JsonDocumentOptions GetResponseDocumentOptions()
    {
        var serializerOptions = EffectiveSerializerOptions;

        return new JsonDocumentOptions
        {
            AllowTrailingCommas = serializerOptions.AllowTrailingCommas,
            CommentHandling = serializerOptions.ReadCommentHandling,
            MaxDepth = serializerOptions.MaxDepth
        };
    }
}
