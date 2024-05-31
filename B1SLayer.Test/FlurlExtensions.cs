using Flurl.Http.Content;
using Flurl.Http.Testing;

namespace B1SLayer.Test;

internal static class FlurlExtensions
{
    public static HttpCallAssertion WithRequestMultipart(this HttpCallAssertion assertion, Func<CapturedStringContent, bool> predicate)
    {
        return assertion.With(x =>
        {
            string content = x.HttpRequestMessage.Content.ReadAsStringAsync().Result;
            return predicate(new CapturedStringContent(content, "CapturedMultipartContent body"));
        });
    }
}
