using Flurl.Http.Content;
using Flurl.Http.Testing;

namespace B1SLayer.Test
{
    internal static class FlurlExtensions
    {
        public static HttpCallAssertion WithRequestMultipart(this HttpCallAssertion assertion, Func<CapturedStringContent, bool> predicate)
        {
            return assertion.With(x => predicate(new CapturedStringContent(x.HttpRequestMessage.Content.ReadAsStringAsync().Result)), "CapturedMultipartContent body");
        }
    }
}
