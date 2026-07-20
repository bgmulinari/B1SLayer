using System.IO;
using System.Text.Json;

using Flurl.Http.Configuration;

namespace B1SLayer;

internal class SystemTextJsonSerializer : ISerializer
{
    public SystemTextJsonSerializer(JsonSerializerOptions options)
    {
        Options = options ?? new JsonSerializerOptions();
    }

    internal JsonSerializerOptions Options { get; }

    public string Serialize(object obj)
    {
        return JsonSerializer.Serialize(obj, Options);
    }

    public T Deserialize<T>(string s)
    {
        return JsonSerializer.Deserialize<T>(s, Options);
    }

    public T Deserialize<T>(Stream stream)
    {
        return JsonSerializer.Deserialize<T>(stream, Options);
    }
}