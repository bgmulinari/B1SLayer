using Newtonsoft.Json;

namespace B1SLayer
{
    public class SLResponseError
    {
        [JsonProperty("error")]
        public SLErrorDetails Error { get; set; }
    }

    public class SLErrorDetails
    {
        [JsonProperty("code")]
        public int Code { get; set; }
        [JsonProperty("message")]
        public SLErrorMessage Message { get; set; }
    }

    public class SLErrorMessage
    {
        [JsonProperty("lang")]
        public string Lang { get; set; }
        [JsonProperty("value")]
        public string Value { get; set; }
    }
}