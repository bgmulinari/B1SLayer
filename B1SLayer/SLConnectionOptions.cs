using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
    ///     Requests sent with an access token select the company through <see cref="CompanyId" /> or the identity instead.
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
    ///     Gets or sets the language code to be used. Specify a code if you want error messages in some specific language
    ///     other than English.
    ///     A GET request to the UserLanguages resource will return all available languages and their respective codes.
    ///     This is a parameter of the session login, so it doesn't affect requests sent with an access token.
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
    ///     Gets or sets the reference for the UI API method responsible for obtaining the connection context when using Single
    ///     Sign-On (SSO) authentication
    ///     (SAPbouiCOM.Framework.Application.SBO_Application.Company.GetServiceLayerConnectionContext).
    /// </summary>
    public Func<string, string> GetServiceLayerConnectionContext { get; set; }

    /// <summary>
    ///     Gets or sets the timeout value in minutes for a Service Layer session when using Single Sign-On (SSO)
    ///     authentication.
    ///     If the configured value differs from the default 30 minutes, specify it through this property.
    ///     Check the "SessionTimeout" property in the file "b1s.conf" on the server.
    /// </summary>
    public int SsoSessionTimeout { get; set; } = 30;

    /// <summary>
    ///     Gets or sets the source of the access token that every request of this connection is sent with, along with
    ///     <see cref="CompanyId" />. This is the single-identity token mode: for one token per user on the same connection,
    ///     use <see cref="IdentityResolver" /> instead.
    /// </summary>
    /// <remarks>
    ///     <see cref="CompanyDB" />, <see cref="UserName" /> and <see cref="Password" /> become optional, and even when
    ///     provided, a request sent with an access token never falls back to a login. Can not be combined with
    ///     <see cref="IdentityResolver" />.
    /// </remarks>
    public ISLAccessTokenSource AccessTokenSource { get; set; }

    /// <summary>
    ///     Gets or sets the SLD company ID sent with <see cref="AccessTokenSource" /> in the X-b1-companyid header,
    ///     as obtained from <see cref="SLOidcClient.GetCompanyIdAsync" />. This is not the company database name.
    /// </summary>
    public string CompanyId { get; set; }

    /// <summary>
    ///     Gets or sets the delegate that decides which identity each request runs with, when it is sent. It lets a single
    ///     cached connection serve many users: return <see cref="SLRequestIdentity.Session" /> to use the session cookies, or
    ///     <see cref="SLRequestIdentity.FromTokenSource" /> to use a user's token, typically based on an ambient user context.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Called once per request, whose retries keep that identity, across every request of this connection, so it
    ///         must be thread-safe and should be fast. An exception thrown by it propagates to the caller of the request.
    ///         It must not return null, which fails the request rather than being read as the session identity.
    ///     </para>
    ///     <para>
    ///         Session credentials are optional, as they are only needed for the identities that use the session.
    ///         Can not be combined with <see cref="AccessTokenSource" />.
    ///     </para>
    /// </remarks>
    public Func<CancellationToken, Task<SLRequestIdentity>> IdentityResolver { get; set; }

    /// <summary>
    ///     Gets or sets whether the server certificate is validated for requests sent with an access token. The default
    ///     value is true: unlike <see cref="ValidateServerCertificate" />, which keeps its default of false
    ///     for session requests, a bearer token is at stake here (RFC 6749 section 10.9, RFC 9700).
    /// </summary>
    /// <remarks>
    ///     Set to false as an explicit opt-out for landscapes with self-signed certificates, or preferably use
    ///     <see cref="AccessTokenServerCertificateValidationCallback" /> to pin the certificate. Session requests have
    ///     a separate connection pool when B1SLayer creates the handlers, so they can't weaken this.
    ///     Only applied when B1SLayer creates its own handlers, that is, when neither
    ///     <see cref="HttpClient" /> nor <see cref="HttpMessageHandler" /> is provided, in which case certificate validation
    ///     is
    ///     up to the provided instance, which also serves both session and token requests through a single connection pool.
    /// </remarks>
    public bool ValidateAccessTokenServerCertificate { get; set; } = true;

    /// <summary>
    ///     Gets or sets a callback that custom-validates the server certificate for requests sent with an access token,
    ///     enabling scenarios like pinning the server's self-signed certificate without trusting it machine-wide.
    ///     When set, it takes precedence over <see cref="ValidateAccessTokenServerCertificate" />.
    /// </summary>
    /// <remarks>
    ///     <see cref="ServerCertificateValidationCallback" /> only applies to session requests and doesn't apply here.
    ///     Can not be combined with <see cref="HttpClient" /> or <see cref="HttpMessageHandler" />, where it couldn't be
    ///     honored.
    /// </remarks>
    public Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> AccessTokenServerCertificateValidationCallback { get; set; }

    /// <summary>
    ///     Gets or sets whether access tokens may be sent to a <see cref="ServiceLayerRoot" /> that isn't HTTPS. The default
    ///     value
    ///     is false, as RFC 6750 section 5.3 requires TLS. Only meant for a local TLS-terminating proxy or for testing.
    /// </summary>
    public bool AllowAccessTokensOverHttp { get; set; }

    /// <summary>
    ///     Gets or sets a custom <see cref="System.Net.Http.HttpClient" /> to be used for the requests to the Service Layer.
    /// </summary>
    /// <remarks>
    ///     The provided instance must be exclusive to this connection and unused, as its
    ///     <see cref="System.Net.Http.HttpClient.Timeout" />
    ///     is set to infinite (timeouts are managed per request by B1SLayer) and it will not be disposed by B1SLayer.
    ///     The underlying handler must have cookies disabled (UseCookies = false), otherwise the session cookies managed
    ///     by B1SLayer will not be sent and, when using access tokens, a cookie stored by the handler could override
    ///     the token. Can not be combined with <see cref="HttpMessageHandler" />.
    /// </remarks>
    public HttpClient HttpClient { get; set; }

    /// <summary>
    ///     Gets or sets whether requests rely on the session the browser already holds, as in a tile extension
    ///     (for example, a Blazor WebAssembly app) hosted by the SAP Business One Web Client.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         SAP designed these extensions to reach the Service Layer without logging in to it, so B1SLayer sends no
    ///         credential and never
    ///         logs in. A 401 means the Web Client's session has ended, so it surfaces right away, and the user has to log in
    ///         again.
    ///     </para>
    ///     <para>
    ///         The Content Security Policy of the Web Client 10.0 FP 2608 doesn't allow WebAssembly, so a Blazor tile doesn't
    ///         start there.
    ///     </para>
    ///     <para>
    ///         <see cref="ServiceLayerRoot" /> must be the absolute Service Layer address on the origin of the app, for
    ///         example
    ///         new Uri(new Uri(navigationManager.BaseUri), "/b1s/v2"), as a browser only adds its cookies to same-origin
    ///         requests by default.
    ///         Can not be combined with session credentials or access tokens. Outside a browser, it
    ///         requires a provided <see cref="HttpClient" /> or <see cref="HttpMessageHandler" /> to supply the credential.
    ///     </para>
    /// </remarks>
    public bool UseAmbientSession { get; set; }

    /// <summary>
    ///     Gets or sets a custom <see cref="System.Net.Http.HttpMessageHandler" /> to be used for the requests to the Service
    ///     Layer.
    /// </summary>
    /// <remarks>
    ///     The handler will be wrapped in an <see cref="System.Net.Http.HttpClient" /> managed by B1SLayer and will not be
    ///     disposed by it.
    ///     B1SLayer disables cookie management (UseCookies = false) on the innermost handler when it is an
    ///     <see cref="System.Net.Http.HttpClientHandler" /> or SocketsHttpHandler, including through DelegatingHandler chains
    ///     —
    ///     for any other handler type that manages cookies, cookies must be disabled manually, otherwise the session
    ///     cookies managed by B1SLayer will not be sent. A handler that has already served requests can no longer be
    ///     configured and is used as-is; in that scenario, cookie management must have been disabled beforehand.
    ///     Redirects are followed by B1SLayer itself so session cookies set on intermediate hops are captured —
    ///     a handler that follows redirects internally (AllowAutoRedirect = true) hides those hops from B1SLayer.
    ///     Can not be combined with <see cref="HttpClient" />.
    /// </remarks>
    public HttpMessageHandler HttpMessageHandler { get; set; }

    /// <summary>
    ///     Gets or sets whether the server certificate should be validated for session requests. The default value is false,
    ///     as SAP Business One servers typically use self-signed certificates.
    /// </summary>
    /// <remarks>
    ///     Doesn't apply to requests sent with an access token, see <see cref="ValidateAccessTokenServerCertificate" />.
    ///     Only applied when B1SLayer creates its own handler, that is, when neither
    ///     <see cref="HttpClient" /> nor <see cref="HttpMessageHandler" /> is provided.
    ///     Ignored when <see cref="ServerCertificateValidationCallback" /> is set.
    /// </remarks>
    public bool ValidateServerCertificate { get; set; }

    /// <summary>
    ///     Gets or sets a callback that custom-validates the server certificate for session requests, enabling scenarios
    ///     like pinning the server's self-signed certificate without trusting it machine-wide.
    ///     When set, it takes precedence over <see cref="ValidateServerCertificate" />.
    /// </summary>
    /// <remarks>
    ///     Doesn't apply to requests sent with an access token, see
    ///     <see cref="AccessTokenServerCertificateValidationCallback" />.
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
    ///     Gets or sets the default <see cref="System.Text.Json.JsonSerializerOptions" /> used to serialize and deserialize
    ///     request and response bodies.
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
