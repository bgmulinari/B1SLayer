using System.Text;
using System.Text.Json;

namespace B1SLayer.Test;

public class HttpExtensionsTests
{
    [Fact]
    public async Task ReadJsonStreamAsync_NonUtf8Charset_TranscodesBody()
    {
        var latin1 = Encoding.GetEncoding("iso-8859-1");
        var content = new ByteArrayContent(latin1.GetBytes("{\"CardName\":\"José\"}"));
        content.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=iso-8859-1");
        using var response = new HttpResponseMessage { Content = content };

        using var jsonDocument = await JsonDocument.ParseAsync(await response.ReadJsonStreamAsync(CancellationToken.None));

        Assert.Equal("José", jsonDocument.RootElement.GetProperty("CardName").GetString());
    }

    [Fact]
    public async Task ReadJsonStreamAsync_Utf8Charset_ReadsStreamDirectly()
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("{\"CardName\":\"José\"}"));
        content.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=utf-8");
        using var response = new HttpResponseMessage { Content = content };

        using var jsonDocument = await JsonDocument.ParseAsync(await response.ReadJsonStreamAsync(CancellationToken.None));

        Assert.Equal("José", jsonDocument.RootElement.GetProperty("CardName").GetString());
    }
}
