using Flurl.Http.Configuration;
using Flurl.Http.Content;
using Flurl.Http.Testing;
using System.Text.Json;

namespace B1SLayer.Test
{
    public abstract class TestBase : IDisposable
    {
        protected HttpTest HttpTest { get; private set; }
        protected static SLConnection SLConnectionV1 { get; } = new SLConnection("https://sapserver:50000/b1s/v1", "CompanyDB", "manager", "12345");
        protected static SLConnection SLConnectionV2 { get; } = new SLConnection("https://sapserver:50000/b1s/v2", "CompanyDB", "manager", "12345");
        protected static SLLoginResponse LoginResponse { get; } = new() { SessionId = "00000000-0000-0000-0000-000000000000", Version = "1000000", SessionTimeout = 30 };

        public TestBase()
        {
            HttpTest = new HttpTest();

            // custom classes as a workaround for testing multipart requests
            HttpTest.Settings.HttpClientFactory = new CustomHttpClientFactory(new CustomDelegatingHandler(HttpTest.Settings.HttpClientFactory.CreateMessageHandler()));

            // standard Login response
            HttpTest.ForCallsTo("*/b1s/v*/Login").RespondWith(JsonSerializer.Serialize(LoginResponse));
        }

        public static IEnumerable<object[]> SLConnections()
        {
            yield return new SLConnection[] { SLConnectionV1 };
            yield return new SLConnection[] { SLConnectionV2 };
        }

        public void Dispose()
        {
            HttpTest.Dispose();
        }

        private class CustomDelegatingHandler : DelegatingHandler
        {
            public CustomDelegatingHandler(HttpMessageHandler innerHandler) => this.InnerHandler = innerHandler;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var stringContent = request.Content is CapturedMultipartContent ? await request.Content.ReadAsStringAsync() : null;
                var result = await base.SendAsync(request, cancellationToken);
                request.Content = stringContent != null ? new CapturedStringContent(stringContent) : request.Content;
                return result;
            }
        }

        private class CustomHttpClientFactory : DefaultHttpClientFactory
        {
            private readonly CustomDelegatingHandler _interceptor;

            public CustomHttpClientFactory(CustomDelegatingHandler interceptor) => _interceptor = interceptor;

            public override HttpMessageHandler CreateMessageHandler() => _interceptor;
        }
    }
}
