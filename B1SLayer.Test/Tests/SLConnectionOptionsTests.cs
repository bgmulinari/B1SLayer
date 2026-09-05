using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace B1SLayer.Test;

public class SLConnectionOptionsTests
{
    [Fact]
    public void CreateDefaultHandler_ByDefault_BypassesCertificateValidation()
    {
        var handler = SLConnection.CreateDefaultHandler(new SLConnectionOptions());

        Assert.NotNull(handler.ServerCertificateCustomValidationCallback);
        Assert.True(handler.ServerCertificateCustomValidationCallback(null, null, null, SslPolicyErrors.RemoteCertificateChainErrors));

        // The remaining handler contract: cookies and redirects are managed by B1SLayer itself, compression enabled
        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(DecompressionMethods.GZip | DecompressionMethods.Deflate, handler.AutomaticDecompression);
    }

    [Fact]
    public void CreateDefaultHandler_WithValidateServerCertificate_UsesStandardValidation()
    {
        var handler = SLConnection.CreateDefaultHandler(new SLConnectionOptions { ValidateServerCertificate = true });

        Assert.Null(handler.ServerCertificateCustomValidationCallback);
    }

    [Fact]
    public void CreateDefaultHandler_CustomValidationCallback_TakesPrecedenceOverToggle()
    {
        Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> pinningCallback = (_, _, _, _) => false;

        var handler = SLConnection.CreateDefaultHandler(new SLConnectionOptions
        {
            ValidateServerCertificate = false,
            ServerCertificateValidationCallback = pinningCallback
        });

        Assert.Same(pinningCallback, handler.ServerCertificateCustomValidationCallback);
        Assert.False(handler.ServerCertificateCustomValidationCallback(null, null, null, SslPolicyErrors.None));
    }
}
