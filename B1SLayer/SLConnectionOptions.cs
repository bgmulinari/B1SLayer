using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using Microsoft.Extensions.Caching.Distributed;

namespace B1SLayer;

/// <summary>
///     Provides the configuration options for a <see cref="SLConnection" />.
/// </summary>
public class SLConnectionOptions
{
    /// <summary>
    ///     Gets or sets the Service Layer root URI. The expected format is https://[server]:[port]/b1s/[version]
    /// </summary>
    public Uri ServiceLayerRoot { get; set; }

    /// <summary>
    ///     Gets or sets the Company database (schema) to connect to.
    /// </summary>
    public string CompanyDB { get; set; }

    /// <summary>
    ///     Gets or sets the SAP user to be used for the Service Layer authentication.
    /// </summary>
    public string UserName { get; set; }

    /// <summary>
    ///     Gets or sets the password for the provided user.
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    ///     Gets or sets the language code to be used. Specify a code if you want error messages in some specific language other than English.
    ///     A GET request to the UserLanguages resource will return all available languages and their respective codes.
    /// </summary>
    public int? Language { get; set; }

    /// <summary>
    ///     Gets or sets the number of attempts for each request in case of an HTTP response code of 401, 500, 502, 503 or 504.
    ///     If the response code is 401 (Unauthorized), a login request will be performed before the new attempt.
    /// </summary>
    public int NumberOfAttempts { get; set; } = 3;

    /// <summary>
    ///     Gets or sets the time to wait between request attempts. The default value is 200 milliseconds.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     Gets or sets the reference for the UI API method responsible for obtaining the connection context when using Single Sign-On (SSO) authentication
    ///     (SAPbouiCOM.Framework.Application.SBO_Application.Company.GetServiceLayerConnectionContext).
    /// </summary>
    public Func<string, string> GetServiceLayerConnectionContext { get; set; }

    /// <summary>
    ///     Gets or sets the timeout value in minutes for a Service Layer session when using Single Sign-On (SSO) authentication.
    ///     If the configured value differs from the default 30 minutes, specify it through this property.
    ///     Check the "SessionTimeout" property in the file "b1s.conf" on the server.
    /// </summary>
    public int SsoSessionTimeout { get; set; } = 30;

    /// <summary>
    ///     Gets or sets a custom <see cref="System.Net.Http.HttpClient" /> to be used for the requests to the Service Layer.
    /// </summary>
    /// <remarks>
    ///     The provided instance must be exclusive to this connection and unused, as its <see cref="System.Net.Http.HttpClient.Timeout" />
    ///     is set to infinite (timeouts are managed per request by B1SLayer) and it will not be disposed by B1SLayer.
    ///     The underlying handler must have cookies disabled (UseCookies = false), otherwise the session cookies managed
    ///     by B1SLayer will not be sent. Can not be combined with <see cref="HttpMessageHandler" />.
    /// </remarks>
    public HttpClient HttpClient { get; set; }

    /// <summary>
    ///     Gets or sets a custom <see cref="System.Net.Http.HttpMessageHandler" /> to be used for the requests to the Service Layer.
    /// </summary>
    /// <remarks>
    ///     The handler will be wrapped in an <see cref="System.Net.Http.HttpClient" /> managed by B1SLayer and will not be disposed by it.
    ///     B1SLayer disables cookie management (UseCookies = false) on the innermost handler when it is an
    ///     <see cref="System.Net.Http.HttpClientHandler" /> or SocketsHttpHandler, including through DelegatingHandler chains —
    ///     for any other handler type that manages cookies, cookies must be disabled manually, otherwise the session
    ///     cookies managed by B1SLayer will not be sent. A handler that has already served requests can no longer be
    ///     configured and is used as-is; in that scenario, cookie management must have been disabled beforehand.
    ///     Redirects are followed by B1SLayer itself so session cookies set on intermediate hops are captured —
    ///     a handler that follows redirects internally (AllowAutoRedirect = true) hides those hops from B1SLayer.
    ///     Can not be combined with <see cref="HttpClient" />.
    /// </remarks>
    public HttpMessageHandler HttpMessageHandler { get; set; }

    /// <summary>
    ///     Gets or sets whether the server certificate should be validated. The default value is false,
    ///     as SAP Business One servers typically use self-signed certificates.
    /// </summary>
    /// <remarks>
    ///     Only applied when B1SLayer creates its own handler, that is, when neither
    ///     <see cref="HttpClient" /> nor <see cref="HttpMessageHandler" /> is provided.
    ///     Ignored when <see cref="ServerCertificateValidationCallback" /> is set.
    /// </remarks>
    public bool ValidateServerCertificate { get; set; }

    /// <summary>
    ///     Gets or sets a callback that custom-validates the server certificate, enabling scenarios like pinning
    ///     the server's self-signed certificate without trusting it machine-wide.
    ///     When set, it takes precedence over <see cref="ValidateServerCertificate" />.
    /// </summary>
    /// <remarks>
    ///     Only applied when B1SLayer creates its own handler, that is, when neither
    ///     <see cref="HttpClient" /> nor <see cref="HttpMessageHandler" /> is provided.
    /// </remarks>
    public Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> ServerCertificateValidationCallback { get; set; }

    /// <summary>
    ///     Gets or sets the default timeout for each request. The default value is 100 seconds.
    ///     Can be overridden per request with <see cref="SLRequest.WithTimeout(TimeSpan)" />.
    /// </summary>
    public TimeSpan DefaultRequestTimeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    ///     Gets or sets the default <see cref="System.Text.Json.JsonSerializerOptions" /> used to serialize and deserialize request and response bodies.
    ///     By default, null values are ignored when serializing. Can be overridden per request with
    ///     <see cref="SLRequest.WithJsonSerializerOptions(JsonSerializerOptions)" />.
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; }

    /// <summary>
    ///     Gets or sets the <see cref="IDistributedCache" /> implementation to be used for session management.
    ///     When not provided, <see cref="B1SLayerSettings.DistributedCache" /> is used.
    /// </summary>
    public IDistributedCache DistributedCache { get; set; }
}
