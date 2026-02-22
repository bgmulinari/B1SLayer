using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public Task<T> GetAsync<T>(bool unwrapCollection = true)
    {
        return _slConnection.ExecuteRequest(async () =>
        {
            var stringResult = await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .GetStringAsync()
                .ConfigureAwait(false);
            using var jsonDoc = JsonDocument.Parse(stringResult);
            var root = jsonDoc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return root.Deserialize<T>();
            }

            if (typeof(T) == typeof(string))
            {
                return (T)(object)root.GetRawText();
            }

            var jsonToDeserialize = unwrapCollection && root.TryGetProperty("value", out var valueCollection)
                ? valueCollection.GetRawText()
                : root.GetRawText();

            return JsonSerializer.Deserialize<T>(jsonToDeserialize);
        });
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
    public Task<(T Result, int Count)> GetWithInlineCountAsync<T>(bool unwrapCollection = true)
    {
        return _slConnection.ExecuteRequest(async () =>
        {
            var stringResult = await FlurlRequest
                .SetQueryParam("$inlinecount", "allpages")
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .GetStringAsync()
                .ConfigureAwait(false);
            using var jsonDoc = JsonDocument.Parse(stringResult);
            var root = jsonDoc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return (root.Deserialize<T>(), 0);
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

            var jsonToDeserialize =
                unwrapCollection && root.TryGetProperty("value", out var valueCollection) ? valueCollection.GetRawText() : root.GetRawText();

            var result = JsonSerializer.Deserialize<T>(jsonToDeserialize);
            return (result, inlineCount);
        });
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
    /// <returns>
    ///     An <see cref="IList{T}" /> containing all the entities in the given collection.
    /// </returns>
    public async Task<IList<T>> GetAllAsync<T>()
    {
        var allResultsList = new List<T>();
        var skip = 0;

        do
        {
            await _slConnection.ExecuteRequest(async () =>
                {
                    var currentResult = await FlurlRequest
                        .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                        .SetQueryParam("$skip", skip)
                        .GetJsonAsync<SLCollectionRoot<T>>()
                        .ConfigureAwait(false);

                    allResultsList.AddRange(currentResult.Value);
                    skip = currentResult.NextSkip;
                    return 0;
                })
                .ConfigureAwait(false);
        } while (skip > 0);

        return allResultsList;
    }

    /// <summary>
    ///     Performs a GET request with the provided parameters and returns the result in a <see cref="string" />.
    /// </summary>
    public Task<string> GetStringAsync()
    {
        return _slConnection.ExecuteRequest(async () =>
            await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .GetStringAsync()
                .ConfigureAwait(false)
        );
    }

    /// <summary>
    ///     Performs a GET request with the provided parameters and returns the result in an instance of the given anonymous type.
    /// </summary>
    /// <param name="anonymousTypeObject">
    ///     The anonymous type object.
    /// </param>
    /// <param name="jsonSerializerOptions">
    ///     The <see cref="JsonSerializerOptions" /> used to deserialize the object.
    /// </param>
    public Task<T> GetAnonymousTypeAsync<T>(T anonymousTypeObject, JsonSerializerOptions jsonSerializerOptions = null)
    {
        return _slConnection.ExecuteRequest(async () =>
        {
            var stringResult = await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .GetStringAsync()
                .ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(stringResult, jsonSerializerOptions
                                                               ?? new JsonSerializerOptions
                                                               {
                                                                   DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                                                               });
        });
    }

    /// <summary>
    ///     Performs a GET request with the provided parameters and returns the result in a <see cref="byte" /> array.
    /// </summary>
    public Task<byte[]> GetBytesAsync()
    {
        return _slConnection.ExecuteRequest(async () =>
            await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .GetBytesAsync()
                .ConfigureAwait(false)
        );
    }

    /// <summary>
    ///     Performs a GET request with the provided parameters and returns the result in a <see cref="Stream" />.
    /// </summary>
    public Task<Stream> GetStreamAsync()
    {
        return _slConnection.ExecuteRequest(async () =>
            await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .GetStreamAsync()
                .ConfigureAwait(false)
        );
    }

    /// <summary>
    ///     Performs a GET request that returns the count of an entity collection.
    /// </summary>
    public Task<long> GetCountAsync()
    {
        return _slConnection.ExecuteRequest(async () =>
        {
            var result = await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .AppendPathSegment("$count")
                .GetStringAsync()
                .ConfigureAwait(false);
            long.TryParse(result, out var quantity);
            return quantity;
        });
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
    public Task<T> PostAsync<T>(object data, bool unwrapCollection = true)
    {
        return _slConnection.ExecuteRequest(async () =>
        {
            var stringResult = await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .PostJsonAsync(data)
                .ReceiveString()
                .ConfigureAwait(false);
            using var jsonDoc = JsonDocument.Parse(stringResult);
            var root = jsonDoc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return root.Deserialize<T>();
            }

            if (typeof(T) == typeof(string))
            {
                return (T)(object)root.GetRawText();
            }

            var hasValueToken = root.TryGetProperty("value", out var valueCollection);
            var jsonToDeserialize = unwrapCollection && hasValueToken ? valueCollection.GetRawText() : root.GetRawText();
            return JsonSerializer.Deserialize<T>(jsonToDeserialize);
        });
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
    public Task<T> PostStringAsync<T>(string data, bool unwrapCollection = true)
    {
        return _slConnection.ExecuteRequest(async () =>
        {
            var stringResult = await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .PostStringAsync(data)
                .ReceiveString()
                .ConfigureAwait(false);
            using var jsonDoc = JsonDocument.Parse(stringResult);
            var root = jsonDoc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return root.Deserialize<T>();
            }

            if (typeof(T) == typeof(string))
            {
                return (T)(object)root.GetRawText();
            }

            var hasValueToken = root.TryGetProperty("value", out var valueCollection);
            var jsonToDeserialize = unwrapCollection && hasValueToken ? valueCollection.GetRawText() : root.GetRawText();
            return JsonSerializer.Deserialize<T>(jsonToDeserialize);
        });
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
    public Task<T> PostAsync<T>(bool unwrapCollection = true)
    {
        return _slConnection.ExecuteRequest(async () =>
        {
            var stringResult = await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .PostAsync()
                .ReceiveString()
                .ConfigureAwait(false);
            using var jsonDoc = JsonDocument.Parse(stringResult);
            var root = jsonDoc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return root.Deserialize<T>();
            }

            if (typeof(T) == typeof(string))
            {
                return (T)(object)root.GetRawText();
            }

            var hasValueToken = root.TryGetProperty("value", out var valueCollection);
            var jsonToDeserialize = unwrapCollection && hasValueToken ? valueCollection.GetRawText() : root.GetRawText();
            return JsonSerializer.Deserialize<T>(jsonToDeserialize);
        });
    }

    /// <summary>
    ///     Performs a POST request with the provided parameters.
    /// </summary>
    /// <param name="data">
    ///     The object to be sent as the JSON body.
    /// </param>
    public Task PostAsync(object data)
    {
        return _slConnection.ExecuteRequest(async () =>
            await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .PostJsonAsync(data)
                .ConfigureAwait(false)
        );
    }

    /// <summary>
    ///     Performs a POST request without parameters and returns the result in a <see cref="string" />.
    /// </summary>
    public Task<string> PostReceiveStringAsync()
    {
        return _slConnection.ExecuteRequest(async () =>
            await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .PostAsync()
                .ReceiveString()
                .ConfigureAwait(false)
        );
    }

    /// <summary>
    ///     Performs a POST request with the provided parameters and returns the result in a <see cref="string" />.
    /// </summary>
    /// <param name="data">
    ///     The object to be sent as the JSON body.
    /// </param>
    public Task<string> PostReceiveStringAsync(object data)
    {
        return _slConnection.ExecuteRequest(async () =>
            await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .PostJsonAsync(data)
                .ReceiveString()
                .ConfigureAwait(false)
        );
    }

    /// <summary>
    ///     Performs a POST request with the provided parameters.
    /// </summary>
    /// <param name="data">
    ///     The JSON string to be sent as the request body.
    /// </param>
    public Task PostStringAsync(string data)
    {
        return _slConnection.ExecuteRequest(async () =>
            await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .PostStringAsync(data)
                .ConfigureAwait(false)
        );
    }

    /// <summary>
    ///     Performs a POST request with the provided parameters.
    /// </summary>
    public Task PostAsync()
    {
        return _slConnection.ExecuteRequest(async () =>
            await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .PostAsync()
                .ConfigureAwait(false)
        );
    }

    /// <summary>
    ///     Performs a PATCH request with the provided parameters.
    /// </summary>
    /// <param name="data">
    ///     The object to be sent as the JSON body.
    /// </param>
    public Task PatchAsync(object data)
    {
        return _slConnection.ExecuteRequest(async () =>
            await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .PatchJsonAsync(data)
                .ConfigureAwait(false)
        );
    }

    /// <summary>
    ///     Performs a PATCH request with the provided parameters.
    /// </summary>
    /// <param name="data">
    ///     The JSON string to be sent as the request body.
    /// </param>
    public Task PatchStringAsync(string data)
    {
        return _slConnection.ExecuteRequest(async () =>
            await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .PatchStringAsync(data)
                .ConfigureAwait(false)
        );
    }

    /// <summary>
    ///     Performs a PATCH request with the provided file.
    /// </summary>
    /// <param name="path">
    ///     The path to the file to be sent.
    /// </param>
    public Task PatchWithFileAsync(string path)
    {
        return PatchWithFileAsync(Path.GetFileName(path), File.ReadAllBytes(path));
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
    public Task PatchWithFileAsync(string fileName, byte[] file)
    {
        return PatchWithFileAsync(fileName, new MemoryStream(file));
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
    public Task PatchWithFileAsync(string fileName, Stream file)
    {
        return _slConnection.ExecuteRequest(async () =>
        {
            return await FlurlRequest.WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .PatchMultipartAsync(mp =>
                {
                    // Removes double quotes from boundary, otherwise the request fails with error 405 Method Not Allowed
                    var boundary = mp.Headers.ContentType.Parameters.First(o => o.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase));
                    boundary.Value = boundary.Value.Replace("\"", string.Empty);

                    var content = new StreamContent(file);
                    content.Headers.Add("Content-Disposition", $"form-data; name=\"files\"; filename=\"{fileName}\"");
                    content.Headers.Add("Content-Type", "application/octet-stream");
                    mp.Add(content);
                })
                .ConfigureAwait(false);
        });
    }

    /// <summary>
    ///     Performs a PUT request with the provided parameters.
    /// </summary>
    /// <param name="data">
    ///     The object to be sent as the JSON body.
    /// </param>
    public Task PutAsync(object data)
    {
        return _slConnection.ExecuteRequest(async () =>
            await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .PutJsonAsync(data)
                .ConfigureAwait(false)
        );
    }

    /// <summary>
    ///     Performs a PUT request with the provided parameters.
    /// </summary>
    /// <param name="data">
    ///     The JSON string to be sent as the request body.
    /// </param>
    public Task PutStringAsync(string data)
    {
        return _slConnection.ExecuteRequest(async () =>
            await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .PutStringAsync(data)
                .ConfigureAwait(false)
        );
    }

    /// <summary>
    ///     Performs a DELETE request with the provided parameters.
    /// </summary>
    public Task DeleteAsync()
    {
        return _slConnection.ExecuteRequest(async () =>
            await FlurlRequest
                .WithCookies(await _slConnection.GetSessionCookiesAsync().ConfigureAwait(false))
                .DeleteAsync()
                .ConfigureAwait(false)
        );
    }
}