using Newtonsoft.Json;
using System;
using System.Net;

namespace B1SLayer
{
    public class SLPingResponse
    {
        [JsonProperty("message")]
        public string Message { get; set; }
        [JsonProperty("sender")]
        public string Sender { get; set; }
        [JsonProperty("timestamp")]
        public decimal Timestamp { get; set; }
        public DateTime DateTime => DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(Timestamp)).LocalDateTime;
        public bool IsSuccessStatusCode { get; internal set; }
        public HttpStatusCode StatusCode { get; internal set; }
    }
}