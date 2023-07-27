using B1SLayer.Models;
using Flurl;
using Flurl.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace B1SLayer
{
    /// <summary>
    /// Represents a request to the Service Layer.
    /// </summary>
    /// <remarks>
    /// The request can be configured using the extension methods provided in <see cref="SLRequestExtensions"/>.
    /// </remarks>
    public class SLRequest
    {
        private readonly SLConnection _slConnection;

        internal IFlurlRequest FlurlRequest { get; }

        internal SLRequest(SLConnection connection, IFlurlRequest flurlRequest)
        {
            _slConnection = connection;
            this.FlurlRequest = flurlRequest;
        }

        /// <summary>
        /// Performs a GET request with the provided parameters and returns the result in a new instance of the specified type.
        /// </summary>
        /// <typeparam name="T">
        /// The object type for the result to be deserialized into.
        /// </typeparam>
        /// <param name="unwrapCollection">
        /// Whether the result should be unwrapped from the 'value' JSON array in case it is a collection.
        /// </param>
        public async Task<T> GetAsync<T>(bool unwrapCollection = true)
        {
            return await _slConnection.ExecuteRequest(async () =>
            {
                string stringResult = await FlurlRequest.WithCookies(_slConnection.Cookies).GetStringAsync();
                var jObject = JObject.Parse(stringResult);

                if (unwrapCollection)
                {
                    // Checks if the result is a collection by selecting the "value" token
                    var valueCollection = jObject.SelectToken("value");
                    return valueCollection == null ? jObject.ToObject<T>() : valueCollection.ToObject<T>();
                }
                else
                {
                    return jObject.ToObject<T>();
                }
            });
        }

        /// <summary>
        /// Performs a GET request with the provided parameters and returns the result in a value tuple containing the deserialized result and the count of matching resources.
        /// </summary>
        /// <typeparam name="T">
        /// The object type for the result to be deserialized into.
        /// </typeparam>
        /// <param name="unwrapCollection">
        /// Whether the result should be unwrapped from the 'value' JSON array in case it is a collection.
        /// </param>
        public async Task<(T Result, int Count)> GetWithInlineCountAsync<T>(bool unwrapCollection = true)
        {
            return await _slConnection.ExecuteRequest(async () =>
            {
                string stringResult = await FlurlRequest.SetQueryParam("$inlinecount", "allpages").WithCookies(_slConnection.Cookies).GetStringAsync();
                var jObject = JObject.Parse(stringResult);
                var inlineCountToken = jObject.GetValue("odata.count") ?? jObject.GetValue("@odata.count");
                int inlineCount = inlineCountToken == null ? 0 : int.Parse(inlineCountToken.ToString());

                if (unwrapCollection)
                {
                    // Checks if the result is a collection by selecting the "value" token
                    var valueCollection = jObject.SelectToken("value");
                    return valueCollection == null ? (jObject.ToObject<T>(), inlineCount) : (valueCollection.ToObject<T>(), inlineCount);
                }
                else
                {
                    return (jObject.ToObject<T>(), inlineCount);
                }
            });
        }

        /// <summary>
        /// Performs multiple GET requests until all entities in a collection are obtained. The result will always be unwrapped from the 'value' array.
        /// </summary>
        /// <remarks>
        /// This can be very slow depending on the total amount of entities in the company database.
        /// </remarks>
        /// <typeparam name="T">
        /// The object type for the result to be deserialized into.
        /// </typeparam>
        /// <returns>
        /// An <see cref="IList{T}"/> containing all the entities in the given collection.
        /// </returns>
        public async Task<IList<T>> GetAllAsync<T>()
        {
            var allResultsList = new List<T>();
            int skip = 0;

            do
            {
                await _slConnection.ExecuteRequest(async () =>
                {
                    var currentResult = await FlurlRequest
                        .WithCookies(_slConnection.Cookies)
                        .SetQueryParam("$skip", skip)
                        .GetJsonAsync<SLCollectionRoot<T>>();

                    allResultsList.AddRange(currentResult.Value);
                    skip = currentResult.NextSkip;
                    return 0;
                });
            }
            while (skip > 0);

            return allResultsList;
        }

        /// <summary>
        /// Performs a GET request with the provided parameters and returns the result in a dynamic object.
        /// </summary>
        public async Task<dynamic> GetAsync()
        {
            return await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).GetJsonAsync();
            });
        }

        /// <summary>
        /// Performs a GET request with the provided parameters and returns the result in a <see cref="string"/>.
        /// </summary>
        public async Task<string> GetStringAsync()
        {
            return await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).GetStringAsync();
            });
        }

        /// <summary>
        /// Performs a GET request with the provided parameters and returns the result in an instance of the given anonymous type.
        /// </summary>
        /// <param name="anonymousTypeObject">
        /// The anonymous type object.
        /// </param>
        /// <param name="jsonSerializerSettings">
        /// The <see cref="JsonSerializerSettings"/> used to deserialize the object. If this is null, 
        /// default serialization settings will be used.
        /// </param>
        public async Task<T> GetAnonymousTypeAsync<T>(T anonymousTypeObject, JsonSerializerSettings jsonSerializerSettings = null)
        {
            return await _slConnection.ExecuteRequest(async () =>
            {
                string stringResult = await FlurlRequest.WithCookies(_slConnection.Cookies).GetStringAsync();
                return JsonConvert.DeserializeAnonymousType(stringResult, anonymousTypeObject, jsonSerializerSettings);
            });
        }

        /// <summary>
        /// Performs a GET request with the provided parameters and returns the result in a <see cref="byte"/> array.
        /// </summary>
        public async Task<byte[]> GetBytesAsync()
        {
            return await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).GetBytesAsync();
            });
        }

        /// <summary>
        /// Performs a GET request with the provided parameters and returns the result in a <see cref="Stream"/>.
        /// </summary>
        public async Task<Stream> GetStreamAsync()
        {
            return await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).GetStreamAsync();
            });
        }

        /// <summary>
        /// Performs a GET request that returns the count of an entity collection.
        /// </summary>
        public async Task<long> GetCountAsync()
        {
            return await _slConnection.ExecuteRequest(async () =>
            {
                string result = await FlurlRequest.WithCookies(_slConnection.Cookies).AppendPathSegment("$count").GetStringAsync();
                long.TryParse(result, out long quantity);
                return quantity;
            });
        }

        /// <summary>
        /// Performs a POST request with the provided parameters and returns the result in the specified <see cref="Type"/>.
        /// </summary>
        /// <param name="data">
        /// The object to be sent as the JSON body.
        /// </param>
        /// <typeparam name="T">
        /// The object type for the result to be deserialized into.
        /// </typeparam>
        /// <param name="unwrapCollection">
        /// Whether the result should be unwrapped from the 'value' JSON array in case it is a collection.
        /// </param>
        public async Task<T> PostAsync<T>(object data, bool unwrapCollection = true)
        {
            return await _slConnection.ExecuteRequest(async () =>
            {
                string stringResult = await FlurlRequest.WithCookies(_slConnection.Cookies).PostJsonAsync(data).ReceiveString();
                var jObject = JObject.Parse(stringResult);

                if (unwrapCollection)
                {
                    // Checks if the result is a collection by selecting the "value" token
                    var valueCollection = jObject.SelectToken("value");
                    return valueCollection == null ? jObject.ToObject<T>() : valueCollection.ToObject<T>();
                }
                else
                {
                    return jObject.ToObject<T>();
                }
            });
        }

        /// <summary>
        /// Performs a POST request with the provided parameters and returns the result in the specified <see cref="Type"/>.
        /// </summary>
        /// <param name="data">
        /// The JSON string to be sent as the request body.
        /// </param>
        /// <typeparam name="T">
        /// The object type for the result to be deserialized into.
        /// </typeparam>
        /// <param name="unwrapCollection">
        /// Whether the result should be unwrapped from the 'value' JSON array in case it is a collection.
        /// </param>
        public async Task<T> PostStringAsync<T>(string data, bool unwrapCollection = true)
        {
            return await _slConnection.ExecuteRequest(async () =>
            {
                string stringResult = await FlurlRequest.WithCookies(_slConnection.Cookies).PostStringAsync(data).ReceiveString();
                var jObject = JObject.Parse(stringResult);

                if (unwrapCollection)
                {
                    // Checks if the result is a collection by selecting the "value" token
                    var valueCollection = jObject.SelectToken("value");
                    return valueCollection == null ? jObject.ToObject<T>() : valueCollection.ToObject<T>();
                }
                else
                {
                    return jObject.ToObject<T>();
                }
            });
        }

        /// <summary>
        /// Performs a POST request with the provided parameters and returns the result in the specified <see cref="Type"/>.
        /// </summary>
        /// <typeparam name="T">
        /// The object type for the result to be deserialized into.
        /// </typeparam>
        /// <param name="unwrapCollection">
        /// Whether the result should be unwrapped from the 'value' JSON array in case it is a collection.
        /// </param>
        public async Task<T> PostAsync<T>(bool unwrapCollection = true)
        {
            return await _slConnection.ExecuteRequest(async () =>
            {
                string stringResult = await FlurlRequest.WithCookies(_slConnection.Cookies).PostAsync().ReceiveString();
                var jObject = JObject.Parse(stringResult);

                if (unwrapCollection)
                {
                    // Checks if the result is a collection by selecting the "value" token
                    var valueCollection = jObject.SelectToken("value");
                    return valueCollection == null ? jObject.ToObject<T>() : valueCollection.ToObject<T>();
                }
                else
                {
                    return jObject.ToObject<T>();
                }
            });
        }

        /// <summary>
        /// Performs a POST request with the provided parameters.
        /// </summary>
        /// <param name="data">
        /// The object to be sent as the JSON body.
        /// </param>
        public async Task PostAsync(object data)
        {
            await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).PostJsonAsync(data);
            });
        }

        /// <summary>
        /// Performs a POST request without parameters and returns the result in a <see cref="string"/>.
        /// </summary>
        public async Task<string> PostReceiveStringAsync()
        {
            return await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).PostAsync().ReceiveString();
            });
        }

        /// <summary>
        /// Performs a POST request with the provided parameters.
        /// </summary>
        /// <param name="data">
        /// The JSON string to be sent as the request body.
        /// </param>
        public async Task PostStringAsync(string data)
        {
            await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).PostStringAsync(data);
            });
        }

        /// <summary>
        /// Performs a POST request with the provided parameters.
        /// </summary>
        public async Task PostAsync()
        {
            await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).PostAsync();
            });
        }

        /// <summary>
        /// Performs a PATCH request with the provided parameters.
        /// </summary>
        /// <param name="data">
        /// The object to be sent as the JSON body.
        /// </param>
        public async Task PatchAsync(object data)
        {
            await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).PatchJsonAsync(data);
            });
        }

        /// <summary>
        /// Performs a PATCH request with the provided parameters.
        /// </summary>
        /// <param name="data">
        /// The JSON string to be sent as the request body.
        /// </param>
        public async Task PatchStringAsync(string data)
        {
            await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).PatchStringAsync(data);
            });
        }

        /// <summary>
        /// Performs a PATCH request with the provided file.
        /// </summary>
        /// <param name="path">
        /// The path to the file to be sent.
        /// </param>
        public async Task PatchWithFileAsync(string path) =>
            await PatchWithFileAsync(Path.GetFileName(path), File.ReadAllBytes(path));

        /// <summary>
        /// Performs a PATCH request with the provided file.
        /// </summary>
        /// <param name="fileName">
        /// The file name of the file including the file extension.
        /// </param>
        /// <param name="file">
        /// The file to be sent.
        /// </param>
        public async Task PatchWithFileAsync(string fileName, byte[] file) =>
            await PatchWithFileAsync(fileName, new MemoryStream(file));

        /// <summary>
        /// Performs a PATCH request with the provided file.
        /// </summary>
        /// <param name="fileName">
        /// The file name of the file including the file extension.
        /// </param>
        /// <param name="file">
        /// The file to be sent.
        /// </param>
        public async Task PatchWithFileAsync(string fileName, Stream file)
        {
            await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).PatchMultipartAsync(mp =>
                {
                    // Removes double quotes from boundary, otherwise the request fails with error 405 Method Not Allowed
                    var boundary = mp.Headers.ContentType.Parameters.First(o => o.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase));
                    boundary.Value = boundary.Value.Replace("\"", string.Empty);

                    var content = new StreamContent(file);
                    content.Headers.Add("Content-Disposition", $"form-data; name=\"files\"; filename=\"{fileName}\"");
                    content.Headers.Add("Content-Type", "application/octet-stream");
                    mp.Add(content);
                });
            });
        }

        /// <summary>
        /// Performs a PUT request with the provided parameters.
        /// </summary>
        /// <param name="data">
        /// The object to be sent as the JSON body.
        /// </param>
        public async Task PutAsync(object data)
        {
            await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).PutJsonAsync(data);
            });
        }

        /// <summary>
        /// Performs a PUT request with the provided parameters.
        /// </summary>
        /// <param name="data">
        /// The JSON string to be sent as the request body.
        /// </param>
        public async Task PutStringAsync(string data)
        {
            await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).PutStringAsync(data);
            });
        }

        /// <summary>
        /// Performs a DELETE request with the provided parameters.
        /// </summary>
        public async Task DeleteAsync()
        {
            await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).DeleteAsync();
            });
        }
    }
}
