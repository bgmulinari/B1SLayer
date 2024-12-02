using System;
using System.Net;
using System.Text.Json.Serialization;

namespace B1SLayer;

/// <summary>
/// Represents the response of a ping request.
/// </summary>
public class SLPingResponse
{
    /// <summary>
    /// Gets or sets the message of a ping response.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; }

    /// <summary>
    /// Gets or sets the sender of a ping response.
    /// </summary>
    [JsonPropertyName("sender")]
    public string Sender { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of a ping response.
    /// </summary>
    [JsonPropertyName("timestamp")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Timestamp { get; set; }

    /// <summary>
    /// Gets the corresponding <see cref="System.DateTime"/> of a ping response based on the <see cref="Timestamp"/>.
    /// </summary>
    public DateTime DateTime => DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(Timestamp)).LocalDateTime;

    /// <summary>
    /// Whether the ping response was successful.
    /// </summary>
    public bool IsSuccessStatusCode { get; internal set; }

    /// <summary>
    /// Gets the <see cref="System.Net.HttpStatusCode"/> of a ping response.
    /// </summary>
    public HttpStatusCode StatusCode { get; internal set; }
}