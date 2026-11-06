using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace B1SLayer.Test;

/// <summary>
///     Certificate validation only happens when a TLS connection is established, so these tests go over a real
///     TLS connection to a local server with a self-signed certificate: a mock handler couldn't catch a token
///     request reusing a pooled connection that a session request established without validation.
/// </summary>
public class SLTransportSecurityTests : IAsyncDisposable
{
    private readonly SelfSignedTlsServer _server = new();

    private Uri ServiceLayerRoot => new($"https://localhost:{_server.Port}/b1s/v2");

    private static ISLAccessTokenSource TokenSource => new RecordingTokenSource("user-token");

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    private SLConnection CreateMixedConnection(Func<bool> useToken, Action<SLConnectionOptions> configure = null)
    {
        var options = new SLConnectionOptions
        {
            ServiceLayerRoot = ServiceLayerRoot,
            CompanyDB = "CompanyDB",
            UserName = $"manager-{Guid.NewGuid():N}",
            Password = "12345",
            IdentityResolver = _ => Task.FromResult(useToken() ? SLRequestIdentity.FromTokenSource(TokenSource, "1875") : SLRequestIdentity.Session),
            RetryDelay = TimeSpan.Zero
        };

        configure?.Invoke(options);
        return new SLConnection(options);
    }

    [Fact]
    public async Task SessionMode_KeepsAcceptingSelfSignedCertificates()
    {
        var connection = new SLConnection(ServiceLayerRoot, "CompanyDB", $"manager-{Guid.NewGuid():N}", "12345");

        await connection.Request("Items").GetStringAsync();

        Assert.Contains(_server.RequestHeaders, x => x.Contains("GET /b1s/v2/Items"));
    }

    [Fact]
    public async Task TokenOnlyConnection_RejectsSelfSignedCertificate_ByDefault_WithoutSendingTheToken()
    {
        var connection = new SLConnection(new SLConnectionOptions { ServiceLayerRoot = ServiceLayerRoot, AccessTokenSource = TokenSource, CompanyId = "1875" });

        await Assert.ThrowsAsync<HttpRequestException>(() => connection.Request("Items").GetStringAsync());

        Assert.DoesNotContain(_server.RequestHeaders, x => x.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MixedConnection_TokenRequest_IsValidatedEvenWithAnEstablishedSessionConnection()
    {
        var useToken = false;
        var connection = CreateMixedConnection(() => useToken);

        // Session requests establish a keep-alive connection without validating the certificate
        await connection.Request("Items").GetStringAsync();
        await connection.Request("Items").GetStringAsync();
        Assert.Equal(1, _server.ConnectionCount);

        // A token request must not get to reuse it, as nothing would validate the certificate then
        useToken = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => connection.Request("Items").GetStringAsync());
        Assert.DoesNotContain(_server.RequestHeaders, x => x.Contains("Authorization", StringComparison.OrdinalIgnoreCase));

        // And the session stays in business
        useToken = false;
        await connection.Request("Items").GetStringAsync();
    }

    [Fact]
    public async Task ExplicitOptOut_AllowsSelfSignedCertificate_AndSendsOnlyTheToken()
    {
        var connection = new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = ServiceLayerRoot,
            AccessTokenSource = TokenSource,
            CompanyId = "1875",
            ValidateAccessTokenServerCertificate = false
        });

        await connection.Request("Items").GetStringAsync();

        var request = Assert.Single(_server.RequestHeaders);
        Assert.Contains("Authorization: Bearer user-token", request, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("X-b1-companyid: 1875", request, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cookie:", request, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task PinningCallback_DecidesForTokenRequests(bool pinMatches, bool expectSuccess)
    {
        string observedThumbprint = null;

        var connection = new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = ServiceLayerRoot,
            AccessTokenSource = TokenSource,
            CompanyId = "1875",
            AccessTokenServerCertificateValidationCallback = (_, certificate, _, _) =>
            {
                observedThumbprint = certificate?.Thumbprint;
                return pinMatches && certificate?.Thumbprint == _server.Thumbprint;
            }
        });

        if (expectSuccess)
        {
            await connection.Request("Items").GetStringAsync();
        }
        else
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => connection.Request("Items").GetStringAsync());
        }

        Assert.Equal(_server.Thumbprint, observedThumbprint);
    }

    [Fact]
    public async Task SessionOptions_DoNotWeakenTokenRequests()
    {
        // A permissive session callback or the session's default says nothing about what is allowed for a bearer token
        var useToken = true;
        var connection = CreateMixedConnection(() => useToken, x => x.ServerCertificateValidationCallback = (_, _, _, _) => true);

        await Assert.ThrowsAsync<HttpRequestException>(() => connection.Request("Items").GetStringAsync());

        useToken = false;
        await connection.Request("Items").GetStringAsync();
    }

    [Fact]
    public async Task OidcClient_ValidatesByDefault_AndHonorsTheOptOut()
    {
        var sld = new Uri($"https://localhost:{_server.Port}");

        using (var secure = new SLOidcClient(new SLOidcClientOptions { SldAddress = sld }))
        {
            var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => secure.GetCurrentUserInfoAsync("user-token"));
            Assert.IsType<HttpRequestException>(exception.InnerException);
        }

        Assert.DoesNotContain(_server.RequestHeaders, x => x.Contains("Authorization", StringComparison.OrdinalIgnoreCase));

        using var optedOut = new SLOidcClient(new SLOidcClientOptions { SldAddress = sld, ValidateServerCertificate = false });

        // Gets through the TLS handshake and receives the fixed JSON of the test server, which isn't valid for this API
        await Assert.ThrowsAsync<SLAuthenticationException>(() => optedOut.GetCurrentUserInfoAsync("user-token"));
        Assert.Contains(_server.RequestHeaders, x => x.Contains("Authorization: Bearer user-token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Handlers_FollowTheContractOfTheirKind()
    {
        // Token traffic validates by default, while the session's handler remains permissive
        var options = new SLConnectionOptions();
        var tokenHandler = SLConnection.CreateAccessTokenHandler(options);
        var sessionHandler = SLConnection.CreateDefaultHandler(options);

        Assert.Null(tokenHandler.ServerCertificateCustomValidationCallback);
        Assert.NotNull(sessionHandler.ServerCertificateCustomValidationCallback);

        // Token traffic has its cookies and redirects managed by the library as well
        Assert.False(tokenHandler.UseCookies);
        Assert.False(tokenHandler.AllowAutoRedirect);

        var optedOut = SLConnection.CreateAccessTokenHandler(new SLConnectionOptions { ValidateAccessTokenServerCertificate = false });
        Assert.True(optedOut.ServerCertificateCustomValidationCallback(null, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    /// <summary>
    ///     A minimal HTTPS server with a self-signed certificate, keeping connections alive and answering every request with
    ///     an empty JSON object.
    /// </summary>
    private sealed class SelfSignedTlsServer : IAsyncDisposable
    {
        private readonly Task _acceptLoop;
        private readonly X509Certificate2 _certificate;
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private int _connectionCount;

        public SelfSignedTlsServer()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            request.CertificateExtensions.Add(names.Build());

            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));
            _certificate = X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);

            _listener.Start();
            _acceptLoop = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string Thumbprint => _certificate.Thumbprint;

        public ConcurrentQueue<string> RequestHeaders { get; } = new();

        public int ConnectionCount => Volatile.Read(ref _connectionCount);

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _acceptLoop;
            _certificate.Dispose();
            _stop.Dispose();
        }

        private async Task AcceptAsync()
        {
            var connections = new List<Task>();

            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    connections.Add(ServeAsync(client));
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // The server is stopping
            }

            await Task.WhenAll(connections);
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    await using var stream = new SslStream(client.GetStream());
                    await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate }, _stop.Token);
                    Interlocked.Increment(ref _connectionCount);

                    var buffer = new byte[8192];
                    var received = new StringBuilder();

                    while (true)
                    {
                        var read = await stream.ReadAsync(buffer, _stop.Token);

                        if (read == 0)
                        {
                            return;
                        }

                        received.Append(Encoding.ASCII.GetString(buffer, 0, read));

                        var text = received.ToString();
                        var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);

                        if (end < 0)
                        {
                            continue;
                        }

                        RequestHeaders.Enqueue(text.Substring(0, end));
                        received.Clear();

                        const string body = "{}";
                        var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: keep-alive\r\n\r\n{body}";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stop.Token);
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or AuthenticationException or SocketException)
                {
                    // A client that refuses the certificate, or the server stopping
                }
            }
        }
    }
}
