using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace B1SLayer;

/// <summary>
///     Provides the configuration options for a <see cref="SLOidcClient" />.
/// </summary>
public sealed class SLOidcClientOptions
{
    /// <summary>
    ///     Gets or sets the address of the System Landscape Directory (SLD). The expected format is https://[server]:40000
    /// </summary>
    public Uri SldAddress { get; set; }

    /// <summary>
    ///     Gets or sets the client ID registered in the SAP Business One Extension Single Sign-On Manager.
    ///     Required for token requests, but not for discovery or company lookups.
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    ///     Gets or sets the credential of the client. Required for the client credentials grant, and for refreshing
    ///     the tokens of a confidential client. A public client, like a desktop app, doesn't have one.
    /// </summary>
    public SLClientCredential ClientCredential { get; set; }

    /// <summary>
    ///     Gets or sets a custom <see cref="System.Net.Http.HttpClient" /> to be used for the requests. It is not modified or
    ///     disposed by B1SLayer.
    /// </summary>
    /// <remarks>
    ///     Certificate validation and redirects are then up to the provided instance. It should
    ///     not follow redirects, as a redirect could otherwise take a client credential elsewhere.
    ///     Can not be combined with <see cref="HttpMessageHandler" />.
    /// </remarks>
    public HttpClient HttpClient { get; set; }

    /// <summary>
    ///     Gets or sets a custom <see cref="System.Net.Http.HttpMessageHandler" /> to be used for the requests. It is not
    ///     disposed by B1SLayer.
    /// </summary>
    /// <remarks>
    ///     Certificate validation and redirects are then up to the provided instance. It should
    ///     not follow redirects, as a redirect could otherwise take a client credential elsewhere.
    ///     Can not be combined with <see cref="HttpClient" />.
    /// </remarks>
    public HttpMessageHandler HttpMessageHandler { get; set; }

    /// <summary>
    ///     Gets or sets whether the server certificate is validated. The default value is true, as client credentials
    ///     and tokens are at stake (RFC 6749 section 10.9, RFC 9700) and the discovery is only as trustworthy as the SLD
    ///     connection.
    /// </summary>
    /// <remarks>
    ///     Set to false as an explicit opt-out for landscapes with self-signed certificates, or preferably use
    ///     <see cref="ServerCertificateValidationCallback" /> to pin the certificate. Only applied when B1SLayer creates its
    ///     own
    ///     handler, that is, when neither <see cref="HttpClient" /> nor <see cref="HttpMessageHandler" /> is provided.
    /// </remarks>
    public bool ValidateServerCertificate { get; set; } = true;

    /// <summary>
    ///     Gets or sets a callback that custom-validates the server certificate, enabling scenarios like pinning the
    ///     certificate
    ///     of the SLD and the authentication server without trusting them machine-wide. When set, it takes precedence over
    ///     <see cref="ValidateServerCertificate" />. Can not be combined with <see cref="HttpClient" /> or
    ///     <see cref="HttpMessageHandler" />.
    /// </summary>
    public Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> ServerCertificateValidationCallback { get; set; }

    /// <summary>
    ///     Gets or sets whether HTTP is allowed for the SLD, the issuer and its endpoints. The default value
    ///     is false, as OAuth requires TLS (RFC 6749 section 3.2). Only meant for testing.
    /// </summary>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>
    ///     Gets or sets the timeout for each request. The default value is 30 seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Gets or sets how long the discovered provider metadata is reused before being discovered again. The default value
    ///     is 1 hour, and it can't be negative or longer than 10 years. Zero discovers the metadata again on every use.
    /// </summary>
    /// <remarks>
    ///     When discovering it again fails because the SLD or the provider is unavailable, the previous metadata
    ///     keeps being used, so an SLD restart doesn't stop token renewals. Invalid metadata is never accepted.
    /// </remarks>
    public TimeSpan ProviderMetadataLifetime { get; set; } = TimeSpan.FromHours(1);
}
