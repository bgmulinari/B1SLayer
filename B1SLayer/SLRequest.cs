using Flurl;
using Flurl.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading.Tasks;

namespace B1SLayer
{
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
        public async Task<T> PostAsync<T>(object data)
        {
            return await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).PostJsonAsync(data).ReceiveJson<T>();
            });
        }

        /// <summary>
        /// Performs a POST request with the provided parameters.
        /// </summary>
        public async Task PostAsync(object data)
        {
            await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).PostJsonAsync(data);
            });
        }

        /// <summary>
        /// Performs a POST request without a JSON body.
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
        public async Task PatchAsync(object data)
        {
            await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).PatchJsonAsync(data);
            });
        }

        /// <summary>
        /// Performs a PUT request with the provided parameters.
        /// </summary>
        public async Task PutAsync(object data)
        {
            await _slConnection.ExecuteRequest(async () =>
            {
                return await FlurlRequest.WithCookies(_slConnection.Cookies).PutJsonAsync(data);
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
