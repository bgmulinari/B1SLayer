using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using B1SLayer.Models;

using Flurl.Http;

namespace B1SLayer;

/// <summary>
///     Represents a request to the Service Layer.
/// </summary>
/// <remarks>
///     The request can be configured using the extension methods provided in <see cref="SLRequestExtensions" />.
/// </remarks>
public class SLRequest
{
    private readonly SLConnection _slConnection;

    internal SLRequest(SLConnection connection, IFlurlRequest flurlRequest)
    {
        _slConnection = connection;
        FlurlRequest = flurlRequest;
    }

    internal IFlurlRequest FlurlRequest { get; }

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
        return _slConnection.ExecuteRequest(async () =>
        {
            var stringResult = await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                .GetStringAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return ParseAndDeserialize<T>(stringResult, unwrapCollection);
        }, cancellationToken);
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
        return _slConnection.ExecuteRequest(async () =>
        {
            var stringResult = await FlurlRequest
                .SetQueryParam("$inlinecount", "allpages")
                .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                .GetStringAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            using var jsonDoc = JsonDocument.Parse(stringResult, GetResponseDocumentOptions());
            var root = jsonDoc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return (DeserializeElement<T>(root), 0);
            }

            if (typeof(T) == typeof(string))
            {
                return ((T)(object)root.GetRawText(), 0);
            }

            var inlineCount = 0;
            JsonElement? inlineCountElement = root.TryGetProperty("odata.count", out var inlineCountElement1) ? inlineCountElement1 : null;
            inlineCountElement ??= root.TryGetProperty("@odata.count", out var inlineCountElement2) ? inlineCountElement2 : null;

            if (inlineCountElement is not null)
            {
                switch (inlineCountElement.Value.ValueKind)
                {
                    case JsonValueKind.Number:
                        inlineCount = inlineCountElement.Value.GetInt32();
                        break;
                    case JsonValueKind.String:
                        inlineCount = int.TryParse(inlineCountElement.Value.GetString(), out var inlineCountElementIntValue) ? inlineCountElementIntValue : 0;
                        break;
                    default:
                        throw new Exception("Inline count is not a number or string");
                }
            }

            var result = DeserializeRoot<T>(root, unwrapCollection);
            return (result, inlineCount);
        }, cancellationToken);
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

            await _slConnection.ExecuteRequest(async () =>
                {
                    var currentResult = await FlurlRequest
                        .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                        .SetQueryParam("$skip", skip)
                        .GetJsonAsync<SLCollectionRoot<T>>(cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    allResultsList.AddRange(currentResult.Value);
                    skip = currentResult.NextSkip;
                    return 0;
                }, cancellationToken)
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
        return _slConnection.ExecuteRequest(async () =>
                await FlurlRequest
                    .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                    .GetStringAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            , cancellationToken);
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
        return _slConnection.ExecuteRequest(async () =>
        {
            var stringResult = await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                .GetStringAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return jsonSerializerOptions is null
                ? DeserializeResponse<T>(stringResult)
                : JsonSerializer.Deserialize<T>(stringResult, jsonSerializerOptions);
        }, cancellationToken);
    }

    /// <summary>
    ///     Performs a GET request with the provided parameters and returns the result in a <see cref="byte" /> array.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<byte[]> GetBytesAsync(CancellationToken cancellationToken = default)
    {
        return _slConnection.ExecuteRequest(async () =>
                await FlurlRequest
                    .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                    .GetBytesAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            , cancellationToken);
    }

    /// <summary>
    ///     Performs a GET request with the provided parameters and returns the result in a <see cref="Stream" />.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<Stream> GetStreamAsync(CancellationToken cancellationToken = default)
    {
        return _slConnection.ExecuteRequest(async () =>
                await FlurlRequest
                    .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                    .GetStreamAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            , cancellationToken);
    }

    /// <summary>
    ///     Performs a GET request that returns the count of an entity collection.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<long> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return _slConnection.ExecuteRequest(async () =>
        {
            var result = await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                .AppendPathSegment("$count")
                .GetStringAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            long.TryParse(result, out var quantity);
            return quantity;
        }, cancellationToken);
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
        return _slConnection.ExecuteRequest(async () =>
        {
            var stringResult = await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                .PostJsonAsync(data, cancellationToken: cancellationToken)
                .ReceiveString()
                .ConfigureAwait(false);
            return ParseAndDeserialize<T>(stringResult, unwrapCollection);
        }, cancellationToken);
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
        return _slConnection.ExecuteRequest(async () =>
        {
            var stringResult = await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                .PostStringAsync(data, cancellationToken: cancellationToken)
                .ReceiveString()
                .ConfigureAwait(false);
            return ParseAndDeserialize<T>(stringResult, unwrapCollection);
        }, cancellationToken);
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
        return _slConnection.ExecuteRequest(async () =>
        {
            var stringResult = await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                .PostAsync(cancellationToken: cancellationToken)
                .ReceiveString()
                .ConfigureAwait(false);
            return ParseAndDeserialize<T>(stringResult, unwrapCollection);
        }, cancellationToken);
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
        return _slConnection.ExecuteRequest(async () =>
                await FlurlRequest
                    .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                    .PostJsonAsync(data, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            , cancellationToken);
    }

    /// <summary>
    ///     Performs a POST request without parameters and returns the result in a <see cref="string" />.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<string> PostReceiveStringAsync(CancellationToken cancellationToken = default)
    {
        return _slConnection.ExecuteRequest(async () =>
                await FlurlRequest
                    .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                    .PostAsync(cancellationToken: cancellationToken)
                    .ReceiveString()
                    .ConfigureAwait(false)
            , cancellationToken);
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
        return _slConnection.ExecuteRequest(async () =>
                await FlurlRequest
                    .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                    .PostJsonAsync(data, cancellationToken: cancellationToken)
                    .ReceiveString()
                    .ConfigureAwait(false)
            , cancellationToken);
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
        return _slConnection.ExecuteRequest(async () =>
                await FlurlRequest
                    .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                    .PostStringAsync(data, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            , cancellationToken);
    }

    /// <summary>
    ///     Performs a POST request with the provided parameters.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task PostAsync(CancellationToken cancellationToken = default)
    {
        return _slConnection.ExecuteRequest(async () =>
                await FlurlRequest
                    .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                    .PostAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            , cancellationToken);
    }

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
        return _slConnection.ExecuteRequest(async () =>
                await FlurlRequest
                    .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                    .PatchJsonAsync(data, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            , cancellationToken);
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
        return _slConnection.ExecuteRequest(async () =>
                await FlurlRequest
                    .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                    .PatchStringAsync(data, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            , cancellationToken);
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
    public Task PatchWithFileAsync(string path, CancellationToken cancellationToken = default)
    {
        return PatchWithFileAsync(Path.GetFileName(path), File.ReadAllBytes(path), cancellationToken);
    }

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
    public Task PatchWithFileAsync(string fileName, byte[] file, CancellationToken cancellationToken = default)
    {
        return PatchWithFileAsync(fileName, new MemoryStream(file), cancellationToken);
    }

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
    public Task PatchWithFileAsync(string fileName, Stream file, CancellationToken cancellationToken = default)
    {
        return _slConnection.ExecuteRequest(async () =>
        {
            return await FlurlRequest.WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                .PatchMultipartAsync(mp =>
                {
                    // Removes double quotes from boundary, otherwise the request fails with error 405 Method Not Allowed
                    var boundary = mp.Headers.ContentType.Parameters.First(o => o.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase));
                    boundary.Value = boundary.Value.Replace("\"", string.Empty);

                    var content = new StreamContent(file);
                    content.Headers.Add("Content-Disposition", $"form-data; name=\"files\"; filename=\"{fileName}\"");
                    content.Headers.Add("Content-Type", "application/octet-stream");
                    mp.Add(content);
                }, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken);
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
        return _slConnection.ExecuteRequest(async () =>
                await FlurlRequest
                    .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                    .PutJsonAsync(data, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            , cancellationToken);
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
        return _slConnection.ExecuteRequest(async () =>
                await FlurlRequest
                    .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                    .PutStringAsync(data, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            , cancellationToken);
    }

    /// <summary>
    ///     Performs a DELETE request with the provided parameters.
    /// </summary>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        return _slConnection.ExecuteRequest(async () =>
                await FlurlRequest
                    .WithCookies(await _slConnection.GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false))
                    .DeleteAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
            , cancellationToken);
    }

    /// <summary>
    ///     Deserializes a JSON response through the effective Flurl serializer, honoring any
    ///     custom serializer configured at the request or connection level.
    /// </summary>
    private T DeserializeResponse<T>(string json)
    {
        return FlurlRequest.Settings.JsonSerializer.Deserialize<T>(json);
    }

    /// <summary>
    ///     Parses a JSON response and deserializes it through the effective serializer,
    ///     unwrapping the OData 'value' collection when requested.
    /// </summary>
    private T ParseAndDeserialize<T>(string json, bool unwrapCollection)
    {
        using var jsonDoc = JsonDocument.Parse(json, GetResponseDocumentOptions());
        return DeserializeRoot<T>(jsonDoc.RootElement, unwrapCollection);
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

    private T DeserializeElement<T>(JsonElement element)
    {
        return FlurlRequest.Settings.JsonSerializer is SystemTextJsonSerializer stjSerializer
            ? element.Deserialize<T>(stjSerializer.Options)
            : FlurlRequest.Settings.JsonSerializer.Deserialize<T>(element.GetRawText());
    }

    /// <summary>
    ///     Carries the reader-affecting serializer options into the initial JsonDocument parse,
    ///     so options like AllowTrailingCommas are honored before deserialization runs.
    /// </summary>
    private JsonDocumentOptions GetResponseDocumentOptions()
    {
        return FlurlRequest.Settings.JsonSerializer is SystemTextJsonSerializer stjSerializer
            ? new JsonDocumentOptions
            {
                AllowTrailingCommas = stjSerializer.Options.AllowTrailingCommas,
                CommentHandling = stjSerializer.Options.ReadCommentHandling,
                MaxDepth = stjSerializer.Options.MaxDepth
            }
            : default;
    }
}