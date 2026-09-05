using System.Text.Json;
using System.Text.Json.Serialization;

using B1SLayer.Test.Models;

namespace B1SLayer.Test;

public class SLSerializerOptionsTests : TestBase
{
    private static readonly JsonSerializerOptions EnumAsStringOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task GetAsync_WithJsonSerializerOptions_UsesOptionsForDeserialization(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("""{"value":[{"DocEntry":1,"DocumentStatus":"bost_Close"}]}""");

        var result = await slConnection
            .Request("Orders")
            .WithJsonSerializerOptions(EnumAsStringOptions)
            .GetAsync<List<DocumentWithStatus>>();

        Assert.Single(result);
        Assert.Equal(1, result[0].DocEntry);
        Assert.Equal(BoStatus.bost_Close, result[0].DocumentStatus);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task GetAsync_BareStringResponse_WithJsonSerializerOptions_UsesOptionsForDeserialization(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("\"bost_Close\"");

        var result = await slConnection
            .Request("Orders(1)/DocumentStatus")
            .WithJsonSerializerOptions(EnumAsStringOptions)
            .GetAsync<BoStatus>();

        Assert.Equal(BoStatus.bost_Close, result);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task GetAsync_ReaderOptions_AreHonoredWhenParsing(string version)
    {
        var slConnection = GetConnection(version);

        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        HttpTest.RespondWith("""{"value":[{"DocEntry":1,"DocumentStatus":"bost_Open",/*comment*/}],}""");

        var result = await slConnection
            .Request("Orders")
            .WithJsonSerializerOptions(options)
            .GetAsync<List<DocumentWithStatus>>();

        Assert.Single(result);
        Assert.Equal(BoStatus.bost_Open, result[0].DocumentStatus);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task GetWithInlineCountAsync_WithJsonSerializerOptions_UsesOptionsForDeserialization(string version)
    {
        var slConnection = GetConnection(version);

        // v1 (OData v3) responses carry "odata.count"; v2 (OData v4) responses carry "@odata.count"
        var isV2 = slConnection.ServiceLayerRoot.ToString().Contains("/b1s/v2");
        var countProperty = isV2 ? "@odata.count" : "odata.count";
        HttpTest.RespondWith($$"""{"{{countProperty}}":1,"value":[{"DocEntry":1,"DocumentStatus":"bost_Close"}]}""");

        var (result, count) = await slConnection
            .Request("Orders")
            .WithJsonSerializerOptions(EnumAsStringOptions)
            .GetWithInlineCountAsync<List<DocumentWithStatus>>();

        Assert.Equal(1, count);
        Assert.Single(result);
        Assert.Equal(BoStatus.bost_Close, result[0].DocumentStatus);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task PostAsync_WithJsonSerializerOptions_UsesOptionsForSerializationAndDeserialization(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("""{"DocEntry":5,"DocumentStatus":"bost_Close"}""");

        var result = await slConnection
            .Request("Orders")
            .WithJsonSerializerOptions(EnumAsStringOptions)
            .PostAsync<DocumentWithStatus>(new DocumentWithStatus { DocEntry = 5, DocumentStatus = BoStatus.bost_Open });

        HttpTest.ShouldHaveCalled(slConnection.ServiceLayerRoot.AppendPathSegment("Orders"))
            .WithVerb(HttpMethod.Post)
            .WithRequestBody("""*"DocumentStatus":"bost_Open"*""")
            .Times(1);

        Assert.Equal(5, result.DocEntry);
        Assert.Equal(BoStatus.bost_Close, result.DocumentStatus);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task PostStringAsync_WithJsonSerializerOptions_UsesOptionsForDeserialization(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("""{"DocEntry":7,"DocumentStatus":"bost_Paid"}""");

        var result = await slConnection
            .Request("Orders")
            .WithJsonSerializerOptions(EnumAsStringOptions)
            .PostStringAsync<DocumentWithStatus>("""{"DocEntry":7}""");

        Assert.Equal(7, result.DocEntry);
        Assert.Equal(BoStatus.bost_Paid, result.DocumentStatus);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task PostAsync_Parameterless_WithJsonSerializerOptions_UsesOptionsForDeserialization(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("""{"DocEntry":9,"DocumentStatus":"bost_Delivered"}""");

        var result = await slConnection
            .Request("SBOBobService_GetDueDate")
            .WithJsonSerializerOptions(EnumAsStringOptions)
            .PostAsync<DocumentWithStatus>();

        Assert.Equal(9, result.DocEntry);
        Assert.Equal(BoStatus.bost_Delivered, result.DocumentStatus);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task IncludeNullValues_PreservesOtherSerializerSettings(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("{}");

        await slConnection.Request("Orders")
            .WithJsonSerializerOptions(EnumAsStringOptions)
            .IncludeNullValues()
            .PostAsync(new { CardCode = (string)null, DocumentStatus = BoStatus.bost_Open });

        // Null values are included while the enum converter from the underlying options is preserved
        HttpTest.ShouldHaveCalled(slConnection.ServiceLayerRoot.AppendPathSegment("Orders"))
            .WithVerb(HttpMethod.Post)
            .WithRequestBody("""*"CardCode":null*""")
            .WithRequestBody("""*"DocumentStatus":"bost_Open"*""")
            .Times(1);
    }

    [Fact]
    public async Task GetAsync_ConnectionLevelSerializer_IsUsedForDeserialization()
    {
        var connection = CreateConnection("v1");
        connection.JsonSerializerOptions = EnumAsStringOptions;

        HttpTest.RespondWith("""{"value":[{"DocEntry":1,"DocumentStatus":"bost_Close"}]}""");

        var result = await connection.Request("Orders").GetAsync<List<DocumentWithStatus>>();

        Assert.Single(result);
        Assert.Equal(BoStatus.bost_Close, result[0].DocumentStatus);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task GetAnonymousTypeAsync_WithoutOptions_UsesRequestSerializer(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("""{"DocumentStatus":"bost_Close"}""");

        var result = await slConnection
            .Request("Orders(1)")
            .WithJsonSerializerOptions(EnumAsStringOptions)
            .GetAnonymousTypeAsync(new { DocumentStatus = BoStatus.bost_Open });

        Assert.Equal(BoStatus.bost_Close, result.DocumentStatus);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task GetAnonymousTypeAsync_ExplicitOptions_TakePrecedenceOverRequestSerializer(string version)
    {
        var slConnection = GetConnection(version);
        HttpTest.RespondWith("""{"DocumentStatus":"bost_Close"}""");

        // request-level serializer has no enum converter; the explicit options parameter must win
        var result = await slConnection
            .Request("Orders(1)")
            .WithJsonSerializerOptions(new JsonSerializerOptions())
            .GetAnonymousTypeAsync(new { DocumentStatus = BoStatus.bost_Open }, EnumAsStringOptions);

        Assert.Equal(BoStatus.bost_Close, result.DocumentStatus);
    }
}
