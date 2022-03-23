using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Reflection;

namespace B1SLayer
{
    [JsonConverter(typeof(SLResponseErrorJsonConverter))]
    public class SLResponseError
    {
        [JsonProperty("error")]
        public SLErrorDetails Error { get; set; } = new SLErrorDetails();
    }

    public class SLErrorDetails
    {
        [JsonProperty("code")]
        public string Code { get; set; }
        [JsonProperty("message")]
        public SLErrorMessage Message { get; set; } = new SLErrorMessage();
    }

    public class SLErrorMessage
    {
        [JsonProperty("lang")]
        public string Lang { get; set; }
        [JsonProperty("value")]
        public string Value { get; set; }
    }

    internal class SLResponseErrorJsonConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType)
        {
            return objectType.GetTypeInfo().IsClass;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            var slError = new SLResponseError();

            // JSON structure differs from OData v3 to OData v4, this is necessary for it to work on both versions
            var errorCode = jo.SelectToken("error.code");
            var message = jo.SelectToken("error.message.value") ?? jo.SelectToken("error.message");
            var lang = jo.SelectToken("error.message.lang");

            slError.Error.Code = errorCode?.ToString();
            slError.Error.Message.Lang = lang?.ToString();
            slError.Error.Message.Value = message?.ToString();

            return slError;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}