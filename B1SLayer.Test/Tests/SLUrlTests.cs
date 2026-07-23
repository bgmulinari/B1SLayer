using System.Globalization;

namespace B1SLayer.Test;

/// <summary>
///     Golden-string regression tests for the URL building/encoding rules.
///     The expected literals were verified byte-identical against the pre-3.0.0 URL-building
///     implementation — changes here mean a change to the bytes SAP receives.
/// </summary>
public class SLUrlTests
{
    private const string Root = "https://sapserver:50000/b1s/v1";

    [Theory]
    [InlineData("Orders(823)", Root + "/Orders(823)")]
    [InlineData("Orders('ABC-123')", Root + "/Orders('ABC-123')")]
    [InlineData("$crossjoin(Orders,Orders/DocumentLines)", Root + "/$crossjoin(Orders,Orders/DocumentLines)")]
    [InlineData("Attachments2(1)/$value", Root + "/Attachments2(1)/$value")]
    [InlineData("$batch", Root + "/$batch")]
    [InlineData("segment with spaces", Root + "/segment%20with%20spaces")]
    [InlineData("BusinessPartners('C%2020')", Root + "/BusinessPartners('C%2020')")]
    [InlineData("100%valid%20mixed % signs", Root + "/100%25valid%20mixed%20%25%20signs")]
    [InlineData("weird\"chars<>{}|\\^`and ümlaut", Root + "/weird%22chars%3C%3E%7B%7D%7C%5C%5E%60and%20%C3%BCmlaut")]
    [InlineData("ping/", Root + "/ping/")]
    public void AppendPathSegment_EncodesIllegalCharactersOnly(string segment, string expected)
    {
        Assert.Equal(expected, SLUrl.AppendPathSegment(Root, segment));
    }

    [Theory]
    [InlineData("$filter", "DocEntry eq 823", "$filter=DocEntry%20eq%20823")]
    [InlineData("$filter", "CardCode eq 'C20000' and DocDate ge '2024-01-01'", "$filter=CardCode%20eq%20%27C20000%27%20and%20DocDate%20ge%20%272024-01-01%27")]
    [InlineData("$select", "DocEntry,CardCode,DocTotal", "$select=DocEntry%2CCardCode%2CDocTotal")]
    [InlineData("$expand", "DocumentLines($select=ItemCode)", "$expand=DocumentLines%28%24select%3DItemCode%29")]
    [InlineData("$orderby", "DocEntry desc", "$orderby=DocEntry%20desc")]
    [InlineData("filename", "'my file (1) 100%.pdf'", "filename=%27my%20file%20%281%29%20100%25.pdf%27")]
    [InlineData("custom", "a=b&c=d?e#f", "custom=a%3Db%26c%3Dd%3Fe%23f")]
    [InlineData("custom", "ümlaut+plus sign%2Fslash", "custom=%C3%BCmlaut%2Bplus%20sign%252Fslash")]
    public void BuildQueryString_FullyEncodesValuesAndPreservesNames(string name, string value, string expected)
    {
        Assert.Equal(expected, SLUrl.BuildQueryString([new KeyValuePair<string, string>(name, value)]));
    }

    [Theory]
    [InlineData(Root, "Orders(823)", Root + "/Orders(823)")]
    [InlineData(Root + "/", "Orders(823)", Root + "/Orders(823)")]
    [InlineData(Root, "/Orders", Root + "/Orders")]
    [InlineData(Root, "Orders?$select=DocEntry", Root + "/Orders?$select=DocEntry")]
    [InlineData(Root, "$crossjoin(Orders,Orders/DocumentLines)?$expand=Orders($select=DocEntry)", Root + "/$crossjoin(Orders,Orders/DocumentLines)?$expand=Orders($select=DocEntry)")]
    // '#' is deliberately encoded (diverging from pre-3.0.0 behavior), as a bare '#' would truncate the batch request path at the fragment
    [InlineData(Root, "Items('A#1')", Root + "/Items('A%231')")]
    public void Combine_JoinsWithSingleSeparator(string root, string relative, string expected)
    {
        Assert.Equal(expected, SLUrl.Combine(root, relative));
    }

    [Fact]
    public void EncodeQueryValue_ValueBeyondEscapeDataStringLimit_IsEncodedInChunks()
    {
        // Uri.EscapeDataString throws on inputs over 65519 chars, so bigger values are encoded in chunks
        var value = string.Concat(Enumerable.Repeat("ab ", 30000));
        var expected = string.Concat(Enumerable.Repeat("ab%20", 30000));

        Assert.Equal(expected, SLUrl.EncodeQueryValue(value));
    }

    [Fact]
    public void EncodeQueryValue_SurrogatePairAtChunkBoundary_IsNotSplit()
    {
        // An astral character straddling the 65519-char chunk boundary must not be encoded as two
        // lone surrogates (U+FFFD replacement characters)
        var value = new string('a', 65518) + "😀" + new string('b', 100);

        var encoded = SLUrl.EncodeQueryValue(value);

        Assert.Equal(Uri.EscapeDataString(value), encoded);
        Assert.DoesNotContain("%EF%BF%BD", encoded);
    }

    [Fact]
    public void BuildUri_ProducesExpectedAbsoluteUri()
    {
        var connection = new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri(Root),
            CompanyDB = "CompanyDB",
            UserName = "manager",
            Password = "12345",
            HttpMessageHandler = new MockHttp()
        });

        var request = connection.Request("Orders")
            .Filter("CardCode eq 'C20000'")
            .Select("DocEntry,CardCode")
            .Top(1);

        Assert.Equal(
            Root + "/Orders?$filter=CardCode%20eq%20%27C20000%27&$select=DocEntry%2CCardCode&$top=1",
            request.BuildUri().AbsoluteUri);
    }

    [Fact]
    public void Request_NonStringId_IsFormattedCultureInvariantly()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            var connection = new SLConnection(new SLConnectionOptions
            {
                ServiceLayerRoot = new Uri(Root),
                CompanyDB = "CompanyDB",
                UserName = "manager",
                Password = "12345",
                HttpMessageHandler = new MockHttp()
            });

            // A decimal id must render as "1.5" regardless of the thread culture's decimal separator
            Assert.EndsWith("/SpecialPrices(1.5)", connection.Request("SpecialPrices", 1.5m).BuildUri().AbsoluteUri);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void BuildUri_WithExtraPathSegment_PlacesItBeforeTheQueryString()
    {
        var connection = new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri(Root),
            CompanyDB = "CompanyDB",
            UserName = "manager",
            Password = "12345",
            HttpMessageHandler = new MockHttp()
        });

        var request = connection.Request("Orders").Filter("DocEntry eq 823");
        request.ExtraPathSegments.Add("$count");

        Assert.Equal(
            Root + "/Orders/$count?$filter=DocEntry%20eq%20823",
            request.BuildUri().AbsoluteUri);
    }
}
