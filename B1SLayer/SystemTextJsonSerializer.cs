using Flurl.Http.Configuration;
using System.IO;
using System.Text.Json;

namespace B1SLayer;

internal class SystemTextJsonSerializer : ISerializer
{
    private readonly JsonSerializerOptions _options;

    public SystemTextJsonSerializer(JsonSerializerOptions options)
    {
        _options = options ?? new JsonSerializerOptions();
    }

    public string Serialize(object obj)
    {
        return JsonSerializer.Serialize(obj, _options);
    }

    public T Deserialize<T>(string s)
    {
        return JsonSerializer.Deserialize<T>(s, _options);
    }

    public T Deserialize<T>(Stream stream)
    {
        return JsonSerializer.Deserialize<T>(stream, _options);
    }
}
