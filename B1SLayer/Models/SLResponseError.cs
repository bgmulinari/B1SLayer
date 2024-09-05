using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace B1SLayer;

/// <summary>
/// The root object that represents a Service Layer error.
/// </summary>
[JsonConverter(typeof(SLResponseErrorJsonConverter))]
public class SLResponseError
{
    /// <summary>
    /// Gets or sets the details of a Service Layer error.
    /// </summary>
    [JsonPropertyName("error")]
    public SLErrorDetails Error { get; set; } = new SLErrorDetails();
}

/// <summary>
/// Represents the details of a Service Layer error.
/// </summary>
public class SLErrorDetails
{
    /// <summary>
    /// Gets or sets the error code of a Service Layer error.
    /// </summary>
    [JsonPropertyName("code")]
    public string Code { get; set; }

    /// <summary>
    /// Gets or sets the error message of a Service Layer error.
    /// </summary>
    [JsonPropertyName("message")]
    public SLErrorMessage Message { get; set; } = new SLErrorMessage();
}

/// <summary>
/// Represents the message of a Service Layer error.
/// </summary>
public class SLErrorMessage
{
    /// <summary>
    /// Gets or sets the message language of a Service Layer error.
    /// </summary>
    [JsonPropertyName("lang")]
    public string Lang { get; set; }

    /// <summary>
    /// Gets or sets the message text of a Service Layer error.
    /// </summary>
    [JsonPropertyName("value")]
    public string Value { get; set; }
}

internal class SLResponseErrorJsonConverter : JsonConverter<SLResponseError>
{
    public override SLResponseError Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var slError = new SLResponseError();

        if (root.GetProperty("error").TryGetProperty("code", out var codeElem))
        {
            if (codeElem.ValueKind == JsonValueKind.String)
            {
                slError.Error.Code = codeElem.GetString();
            }
            else if (codeElem.ValueKind == JsonValueKind.Number)
            {
                slError.Error.Code = codeElem.GetInt32().ToString();
            }
        }

        if (root.GetProperty("error").TryGetProperty("message", out var messageElem))
        {
            if (messageElem.ValueKind == JsonValueKind.Object)
            {
                slError.Error.Message.Value = messageElem.TryGetProperty("value", out var valueElem) ? valueElem.GetString() : null;
                slError.Error.Message.Lang = messageElem.TryGetProperty("lang", out var langElem) ? langElem.GetString() : null;
            }
            else if (messageElem.ValueKind == JsonValueKind.String)
            {
                slError.Error.Message.Value = messageElem.GetString();
                slError.Error.Message.Lang = null;
            }
        }

        return slError;
    }

    public override void Write(Utf8JsonWriter writer, SLResponseError value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}