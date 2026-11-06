using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Distributed;

namespace B1SLayer;

/// <summary>
///     Represents a connection to the Service Layer.
/// </summary>
/// <remarks>
///     Only one instance per company/user should be used in the application. With access tokens, a single
///     instance can serve many users, with the identity decided per request through
///     <see cref="SLConnectionOptions.IdentityResolver" />.
/// </remarks>
public partial class SLConnection
{
    /// <summary>
    ///     The serializer options for B1SLayer's own protocol exchanges (Login, Ping, attachments), whose fixed
    ///     wire format must not be affected by the user-configurable <see cref="JsonSerializerOptions" />.
    /// </summary>
    private static readonly JsonSerializerOptions ProtocolSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };


    /// <summary>
    ///     Initializes a new instance of the <see cref="SLConnection" /> class with the provided options.
    ///     Only one instance per company/user should be used in the application.
    /// </summary>
    /// <param name="options">
    ///     The configuration options for this connection.
    /// </param>
    public SLConnection(SLConnectionOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.ServiceLayerRoot == null)
        {
            throw new ArgumentException("serviceLayerRoot can not be empty.");
        }

        IsUsingSingleSignOn = options.GetServiceLayerConnectionContext != null;
        IsUsingAccessTokens = options.AccessTokenSource != null || options.IdentityResolver != null;
        IsUsingAmbientSession = options.UseAmbientSession;

        if (IsUsingAmbientSession)
        {
            ValidateAmbientSessionOptions(options);
        }

        if (IsUsingAccessTokens)
        {
            ValidateAccessTokenOptions(options);
        }
        else if (!string.IsNullOrEmpty(options.CompanyId))
        {
            throw new ArgumentException($"{nameof(SLConnectionOptions.CompanyId)} can only be provided along with {nameof(SLConnectionOptions.AccessTokenSource)}.");
        }

        // A token-only connection has no session account, while any other connection requires a complete one
        HasSessionCredentials = IsUsingSingleSignOn
                                || !string.IsNullOrWhiteSpace(options.CompanyDB)
                                && !string.IsNullOrWhiteSpace(options.UserName)
                                && !string.IsNullOrWhiteSpace(options.Password);

        var hasAnySessionCredential = !string.IsNullOrWhiteSpace(options.CompanyDB)
                                      || !string.IsNullOrWhiteSpace(options.UserName)
                                      || !string.IsNullOrWhiteSpace(options.Password);

        if (!IsUsingSingleSignOn && !IsUsingAmbientSession && (!IsUsingAccessTokens || hasAnySessionCredential))
        {
            if (string.IsNullOrWhiteSpace(options.CompanyDB))
            {
                throw new ArgumentException("companyDB can not be empty.");
            }

            if (string.IsNullOrWhiteSpace(options.UserName))
            {
                throw new ArgumentException("userName can not be empty.");
            }

            if (string.IsNullOrWhiteSpace(options.Password))
            {
                throw new ArgumentException("password can not be empty.");
            }
        }

        // A login through a client whose handler stores cookies would add its session to the token requests, below the point where they
        // have cookies removed, and the Service Layer honors a cookie over the token. An HttpClient doesn't expose its handler to check that
        if (IsUsingAccessTokens && HasSessionCredentials && options.HttpClient != null)
        {
            throw new ArgumentException($"A provided {nameof(SLConnectionOptions.HttpClient)} can not serve both session and token requests, as B1SLayer can't verify that it doesn't store the session's cookies. Provide an {nameof(SLConnectionOptions.HttpMessageHandler)} instead.");
        }

        ServiceLayerRoot = options.ServiceLayerRoot;
        CompanyDB = options.CompanyDB;
        UserName = options.UserName;
        Password = options.Password;
        Language = options.Language;
        NumberOfAttempts = options.NumberOfAttempts;
        RetryDelay = options.RetryDelay;
        LoginResponse = new SLLoginResponse();
        JsonSerializerOptions = options.JsonSerializerOptions
                                ?? new JsonSerializerOptions
                                {
                                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                                };
        DefaultRequestTimeout = options.DefaultRequestTimeout;
        DistributedCache = options.DistributedCache ?? B1SLayerSettings.DistributedCache;
        _getServiceLayerConnectionContext = options.GetServiceLayerConnectionContext;
        _ssoSessionTimeout = options.SsoSessionTimeout;
        CompanyId = options.CompanyId;
        SessionCacheKey = IsUsingSingleSignOn || !HasSessionCredentials
            ? $"B1SLayer:SessionCookies:{ServiceLayerRoot}:{Guid.NewGuid()}"
            : $"B1SLayer:SessionCookies:{ServiceLayerRoot}:{CompanyDB}:{UserName}";

        // The single-identity token mode is the identity resolver with a constant answer
        _identityResolver = options.IdentityResolver;

        if (options.AccessTokenSource != null)
        {
            var identity = SLRequestIdentity.FromTokenSource(options.AccessTokenSource, options.CompanyId);
            var resolvedIdentity = Task.FromResult(identity);
            _identityResolver = _ => resolvedIdentity;
        }

        if (!IsUsingAccessTokens)
        {
            HttpClient = BuildHttpClient(options);
        }
        else if (options.HttpClient != null || options.HttpMessageHandler != null)
        {
            // The provided transport is up to the caller, certificate validation included, and serves every request
            HttpClient = BuildHttpClient(options);
            _accessTokenHttpClient = HttpClient;
        }
        else
        {
            // Certificate validation only happens when a TLS connection is established, and connections are pooled per
            // handler, so the session's permissive default can only be kept from token requests through a separate handler
            _accessTokenHttpClient = ConfigureHttpClient(new HttpClient(CreateAccessTokenHandler(options)));
            HttpClient = HasSessionCredentials ? BuildHttpClient(options) : _accessTokenHttpClient;
        }
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="SLConnection" /> class.
    ///     Only one instance per company/user should be used in the application.
    /// </summary>
    /// <param name="serviceLayerRoot">
    ///     The Service Layer root URI. The expected format is https://[server]:[port]/b1s/[version]
    /// </param>
    /// <param name="companyDB">
    ///     The Company database (schema) to connect to.
    /// </param>
    /// <param name="userName">
    ///     The SAP user to be used for the Service Layer authentication.
    /// </param>
    /// <param name="password">
    ///     The password for the provided user.
    /// </param>
    /// <param name="language">
    ///     The language code to be used. Specify a code if you want error messages in some specific language other than
    ///     English.
    ///     A GET request to the UserLanguages resource will return all available languages and their respective codes.
    /// </param>
    /// <param name="numberOfAttempts">
    ///     The number of attempts for each request in case of an HTTP response code of 401, 500, 502, 503 or 504.
    ///     If the response code is 401 (Unauthorized), a login request will be performed before the new attempt.
    /// </param>
    public SLConnection(Uri serviceLayerRoot, string companyDB, string userName, string password, int? language = null,
        int numberOfAttempts = 3)
        : this(new SLConnectionOptions
        {
            ServiceLayerRoot = serviceLayerRoot,
            CompanyDB = companyDB,
            UserName = userName,
            Password = password,
            Language = language,
            NumberOfAttempts = numberOfAttempts
        })
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="SLConnection" /> class.
    ///     Only one instance per company/user should be used in the application.
    /// </summary>
    /// <param name="serviceLayerRoot">
    ///     The Service Layer root URI. The expected format is https://[server]:[port]/b1s/[version]
    /// </param>
    /// <param name="companyDB">
    ///     The Company database (schema) to connect to.
    /// </param>
    /// <param name="userName">
    ///     The SAP user to be used for the Service Layer authentication.
    /// </param>
    /// <param name="password">
    ///     The password for the provided user.
    /// </param>
    /// <param name="language">
    ///     The language code to be used. Specify a code if you want error messages in some specific language other than
    ///     English.
    ///     A GET request to the UserLanguages resource will return all available languages and their respective codes.
    /// </param>
    /// <param name="numberOfAttempts">
    ///     The number of attempts for each request in case of an HTTP response code of 401, 500, 502, 503 or 504.
    ///     If the response code is 401 (Unauthorized), a login request will be performed before the new attempt.
    /// </param>
    public SLConnection(string serviceLayerRoot, string companyDB, string userName, string password,
        int? language = null, int numberOfAttempts = 3)
        : this(new Uri(serviceLayerRoot),
            companyDB,
            userName,
            password,
            language,
            numberOfAttempts)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="SLConnection" /> class.
    ///     Only one instance per company/user should be used in the application.
    /// </summary>
    /// <param name="serviceLayerRoot">
    ///     The Service Layer root URI. The expected format is https://[server]:[port]/b1s/[version]
    /// </param>
    /// <param name="companyDB">
    ///     The Company database (schema) to connect to.
    /// </param>
    /// <param name="userName">
    ///     The SAP user to be used for the Service Layer authentication.
    /// </param>
    /// <param name="password">
    ///     The password for the provided user.
    /// </param>
    public SLConnection(string serviceLayerRoot, string companyDB, string userName, string password)
        : this(new Uri(serviceLayerRoot), companyDB, userName, password)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="SLConnection" /> class.
    ///     Only one instance per company/user should be used in the application.
    /// </summary>
    /// <param name="serviceLayerRoot">
    ///     The Service Layer root URI. The expected format is https://[server]:[port]/b1s/[version]
    /// </param>
    /// <param name="companyDB">
    ///     The Company database (schema) to connect to.
    /// </param>
    /// <param name="userName">
    ///     The SAP user to be used for the Service Layer authentication.
    /// </param>
    /// <param name="password">
    ///     The password for the provided user.
    /// </param>
    /// <param name="language">
    ///     The language code to be used. Specify a code if you want error messages in some specific language other than
    ///     English.
    ///     A GET request to the UserLanguages resource will return all available languages and their respective codes.
    /// </param>
    public SLConnection(string serviceLayerRoot, string companyDB, string userName, string password, int? language)
        : this(new Uri(serviceLayerRoot),
            companyDB,
            userName,
            password,
            language)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="SLConnection" /> class using Single Sign-On (SSO) authentication.
    /// </summary>
    /// <param name="serviceLayerRoot">
    ///     The Service Layer root URI. The expected format is https://[server]:[port]/b1s/[version]
    /// </param>
    /// <param name="getServiceLayerConnectionContext">
    ///     The reference for the UI API method responsible for obtaining the connection context
    ///     (SAPbouiCOM.Framework.Application.SBO_Application.Company.GetServiceLayerConnectionContext).
    /// </param>
    /// <param name="sessionTimeout">
    ///     The timeout value in minutes for a Service Layer session. If the configured value differs from the default 30
    ///     minutes,
    ///     specify it through this parameter. Check the "SessionTimeout" property in the file "b1s.conf" on the server.
    /// </param>
    /// <param name="numberOfAttempts">
    ///     The number of attempts for each request in case of an HTTP response code of 401, 500, 502, 503 or 504.
    ///     If the response code is 401 (Unauthorized), a login request will be performed before the new attempt.
    /// </param>
    public SLConnection(Uri serviceLayerRoot, Func<string, string> getServiceLayerConnectionContext,
        int sessionTimeout = 30, int numberOfAttempts = 3)
        : this(new SLConnectionOptions
        {
            ServiceLayerRoot = serviceLayerRoot,
            GetServiceLayerConnectionContext = getServiceLayerConnectionContext,
            SsoSessionTimeout = sessionTimeout,
            NumberOfAttempts = numberOfAttempts
        })
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="SLConnection" /> class using Single Sign-On (SSO) authentication.
    /// </summary>
    /// <param name="serviceLayerRoot">
    ///     The Service Layer root URI. The expected format is https://[server]:[port]/b1s/[version]
    /// </param>
    /// <param name="getServiceLayerConnectionContext">
    ///     The reference for the UI API method responsible for obtaining the connection context
    ///     (SAPbouiCOM.Framework.Application.SBO_Application.Company.GetServiceLayerConnectionContext).
    /// </param>
    /// <param name="sessionTimeout">
    ///     The timeout value in minutes for a Service Layer session. If the configured value differs from the default 30
    ///     minutes,
    ///     specify it through this parameter. Check the "SessionTimeout" property in the file "b1s.conf" on the server.
    /// </param>
    /// <param name="numberOfAttempts">
    ///     The number of attempts for each request in case of an HTTP response code of 401, 500, 502, 503 or 504.
    ///     If the response code is 401 (Unauthorized), a login request will be performed before the new attempt.
    /// </param>
    public SLConnection(string serviceLayerRoot, Func<string, string> getServiceLayerConnectionContext,
        int sessionTimeout = 30, int numberOfAttempts = 3)
        : this(new Uri(serviceLayerRoot), getServiceLayerConnectionContext, sessionTimeout, numberOfAttempts)
    {
    }

    /// <summary>
    ///     Gets the <see cref="System.Net.Http.HttpClient" /> responsible for the requests to Service Layer.
    /// </summary>
    /// <remarks>
    ///     A connection with both session credentials and access tokens sends token requests through a separate
    ///     client when B1SLayer creates its own handlers, see
    ///     <see cref="SLConnectionOptions.ValidateAccessTokenServerCertificate" />.
    /// </remarks>
    public HttpClient HttpClient { get; }

    /// <summary>
    ///     Gets the <see cref="IDistributedCache" /> implementation to be used for session management. By default, an
    ///     in-memory implementation is used.
    /// </summary>
    public IDistributedCache DistributedCache { get; }

    /// <summary>
    ///     Gets or sets cache key to be used for the session cache.
    /// </summary>
    public string SessionCacheKey { get; set; }

    /// <summary>
    ///     Gets the Service Layer root URI.
    /// </summary>
    public Uri ServiceLayerRoot { get; }

    /// <summary>
    ///     Gets the Company database (schema) to connect to.
    /// </summary>
    public string CompanyDB { get; }

    /// <summary>
    ///     Gets the username to be used for the Service Layer authentication.
    /// </summary>
    public string UserName { get; }

    /// <summary>
    ///     Gets the password for the provided username.
    /// </summary>
    public string Password { get; }

    /// <summary>
    ///     Gets the Service Layer language code provided.
    /// </summary>
    public int? Language { get; }

    /// <summary>
    ///     Gets or sets the number of attempts for each unsuccessful request in case of an HTTP status code contained in
    ///     <see cref="HttpStatusCodesToRetry" />.
    /// </summary>
    public int NumberOfAttempts { get; set; }

    /// <summary>
    ///     Gets or sets the time to wait between request attempts. The default value is 200 milliseconds.
    /// </summary>
    public TimeSpan RetryDelay
    {
        get;
        set
        {
            if (value < TimeSpan.Zero || value > TimeSpan.FromMilliseconds(int.MaxValue))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            field = value;
        }
    } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     Gets or sets the <see cref="System.Text.Json.JsonSerializerOptions" /> used to serialize and deserialize request
    ///     and response bodies.
    ///     By default, null values are ignored when serializing. Can be overridden per request with
    ///     <see cref="SLRequest.WithJsonSerializerOptions(JsonSerializerOptions)" />.
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    ///     Gets or sets the default timeout for each request. The default value is 100 seconds.
    ///     Can be overridden per request with <see cref="SLRequest.WithTimeout(TimeSpan)" />.
    /// </summary>
    public TimeSpan DefaultRequestTimeout
    {
        get;
        set
        {
            if (value != Timeout.InfiniteTimeSpan && (value <= TimeSpan.Zero || value > TimeSpan.FromMilliseconds(int.MaxValue)))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            field = value;
        }
    } = TimeSpan.FromSeconds(100);

    /// <summary>
    ///     Gets or sets the timespan to wait before a batch request times out. The default value is 5 minutes (300 seconds).
    /// </summary>
    public TimeSpan BatchRequestTimeout
    {
        get;
        set
        {
            if (value != Timeout.InfiniteTimeSpan && (value <= TimeSpan.Zero || value > TimeSpan.FromMilliseconds(int.MaxValue)))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            field = value;
        }
    } = TimeSpan.FromSeconds(300);

    /// <summary>
    ///     Gets whether this <see cref="SLConnection" /> instance is using Single Sign-On (SSO) authentication.
    /// </summary>
    public bool IsUsingSingleSignOn { get; }

    /// <summary>
    ///     Gets whether this <see cref="SLConnection" /> instance sends requests with access tokens,
    ///     through <see cref="SLConnectionOptions.AccessTokenSource" /> or <see cref="SLConnectionOptions.IdentityResolver" />
    ///     .
    /// </summary>
    public bool IsUsingAccessTokens { get; }

    /// <summary>
    ///     Gets whether this <see cref="SLConnection" /> instance relies on the session
    ///     the browser already holds, through <see cref="SLConnectionOptions.UseAmbientSession" />.
    /// </summary>
    public bool IsUsingAmbientSession { get; }

    /// <summary>
    ///     Gets whether this <see cref="SLConnection" /> instance has a session account to log in with, through a username and
    ///     password or Single Sign-On (SSO). Always true for a connection that uses neither access tokens nor an ambient
    ///     session.
    /// </summary>
    public bool HasSessionCredentials { get; }

    /// <summary>
    ///     Gets the SLD company ID provided for the single-identity token mode, or null otherwise.
    /// </summary>
    public string CompanyId { get; }

    /// <summary>
    ///     Gets information about the latest Login request.
    /// </summary>
    public SLLoginResponse LoginResponse
    {
        // Returns a new object so the login control can't be manipulated externally
        get => new()
        {
            LastLogin = _loginResponse.LastLogin,
            SessionId = _loginResponse.SessionId,
            SessionTimeout = _loginResponse.SessionTimeout,
            Version = _loginResponse.Version
        };

        private set => _loginResponse = value;
    }

    /// <summary>
    ///     Gets a list of <see cref="HttpStatusCode" /> to be checked before retrying an unsuccessful request.
    /// </summary>
    /// <remarks>
    ///     The number of attempts is defined by <see cref="NumberOfAttempts" />.
    /// </remarks>
    public IList<HttpStatusCode> HttpStatusCodesToRetry { get; } =
    [
        HttpStatusCode.Unauthorized,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    ];

    private static bool IsRunningInBrowser => RuntimeInformation.IsOSPlatform(OSPlatform.Create("BROWSER"));


    /// <summary>
    ///     Validates the options specific to an ambient session. Each of the credentials it rejects
    ///     would mean running a request as someone other than the user of the browser's session.
    /// </summary>
    private static void ValidateAmbientSessionOptions(SLConnectionOptions options)
    {
        if (options.AccessTokenSource != null
            || options.IdentityResolver != null
            || options.GetServiceLayerConnectionContext != null
            || !string.IsNullOrEmpty(options.CompanyId)
            || !string.IsNullOrWhiteSpace(options.CompanyDB)
            || !string.IsNullOrWhiteSpace(options.UserName)
            || !string.IsNullOrWhiteSpace(options.Password))
        {
            throw new ArgumentException($"{nameof(SLConnectionOptions.UseAmbientSession)} can not be combined with session credentials or access tokens, as the session of the browser is the only identity it runs with.");
        }

        // Silently ignoring a requested pin would validate differently than asked. A browser validates on its own, and a provided transport is configured by its owner
        if (options.ServerCertificateValidationCallback != null || options.AccessTokenServerCertificateValidationCallback != null)
        {
            throw new ArgumentException($"{nameof(SLConnectionOptions.UseAmbientSession)} can not be combined with a certificate validation callback, as it can't be applied.");
        }

        if (!options.ServiceLayerRoot.IsAbsoluteUri)
        {
            throw new ArgumentException($"{nameof(SLConnectionOptions.UseAmbientSession)} requires an absolute {nameof(SLConnectionOptions.ServiceLayerRoot)}. In Blazor, it can be built from NavigationManager.BaseUri.");
        }

        if (options.HttpClient == null && options.HttpMessageHandler == null && !IsRunningInBrowser)
        {
            throw new ArgumentException($"{nameof(SLConnectionOptions.UseAmbientSession)} requires a browser, as nothing else holds the session. Outside of one, provide an {nameof(SLConnectionOptions.HttpClient)} or {nameof(SLConnectionOptions.HttpMessageHandler)} that supplies the credential.");
        }
    }

    /// <summary>
    ///     Validates the options specific to access tokens.
    /// </summary>
    private static void ValidateAccessTokenOptions(SLConnectionOptions options)
    {
        if (options.AccessTokenSource != null && options.IdentityResolver != null)
        {
            throw new ArgumentException($"{nameof(SLConnectionOptions.AccessTokenSource)} and {nameof(SLConnectionOptions.IdentityResolver)} can not be provided together.");
        }

        if (options.AccessTokenSource != null && !SLRequestCredential.IsValidCompanyId(options.CompanyId))
        {
            throw new ArgumentException($"{nameof(SLConnectionOptions.CompanyId)} is required with {nameof(SLConnectionOptions.AccessTokenSource)}, and must only contain printable ASCII characters.");
        }

        if (options.AccessTokenSource == null && !string.IsNullOrEmpty(options.CompanyId))
        {
            throw new ArgumentException($"{nameof(SLConnectionOptions.CompanyId)} can only be provided along with {nameof(SLConnectionOptions.AccessTokenSource)}. An {nameof(SLConnectionOptions.IdentityResolver)} provides it through {nameof(SLRequestIdentity)}.");
        }

        if (options.ServiceLayerRoot.IsAbsoluteUri
            && !string.Equals(options.ServiceLayerRoot.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !options.AllowAccessTokensOverHttp)
        {
            throw new ArgumentException($"Access tokens require an HTTPS {nameof(SLConnectionOptions.ServiceLayerRoot)} (RFC 6750 section 5.3). To send them over HTTP regardless, set {nameof(SLConnectionOptions.AllowAccessTokensOverHttp)}.");
        }

        if (options.AccessTokenServerCertificateValidationCallback != null && (options.HttpClient != null || options.HttpMessageHandler != null))
        {
            // Silently ignoring a requested pin would validate differently than asked, with a credential at stake
            throw new ArgumentException($"{nameof(SLConnectionOptions.AccessTokenServerCertificateValidationCallback)} can not be combined with a provided {nameof(SLConnectionOptions.HttpClient)} or {nameof(SLConnectionOptions.HttpMessageHandler)}. Configure certificate validation on the provided instance instead.");
        }
    }

    private static HttpClient BuildHttpClient(SLConnectionOptions options)
    {
        if (options.HttpClient != null && options.HttpMessageHandler != null)
        {
            throw new ArgumentException($"{nameof(SLConnectionOptions.HttpClient)} and {nameof(SLConnectionOptions.HttpMessageHandler)} can not be provided together.");
        }

        HttpClient httpClient;

        if (options.HttpClient != null)
        {
            httpClient = options.HttpClient;
        }
        else if (options.HttpMessageHandler != null)
        {
            // The cookies of a handler are the credential of an ambient session outside a browser. Nothing else uses them
            if (!options.UseAmbientSession)
            {
                SLHttpHandlers.DisableCookieManagement(options.HttpMessageHandler);
            }

            if (options.AccessTokenSource != null || options.IdentityResolver != null)
            {
                // Redirects a handler follows by itself would skip the rule that a token request only follows them within its origin
                SLHttpHandlers.DisableAutomaticRedirects(options.HttpMessageHandler);

                // Cookies a handler stores are added below the point where a token request has them removed, and the Service Layer
                // honors a cookie over the token, so such a handler would silently run token requests as the session account
                if (SLHttpHandlers.IsCookieManagementEnabled(options.HttpMessageHandler) || SLHttpHandlers.IsAutomaticRedirectEnabled(options.HttpMessageHandler))
                {
                    throw new ArgumentException($"The provided {nameof(SLConnectionOptions.HttpMessageHandler)} has already served requests with cookies or automatic redirects enabled, so it can't be used with access tokens. Provide a new handler, or one with both disabled.");
                }
            }

            httpClient = new HttpClient(options.HttpMessageHandler, false);
        }
        else
        {
            // An ambient session gets a handler with the browser's defaults. Its redirects have to stay with the browser, which is what adds the session's
            // cookies to them: asking it not to follow them means a "manual" fetch, whose response hides both the status code and the Location header
            httpClient = new HttpClient(options.UseAmbientSession ? new HttpClientHandler() : CreateDefaultHandler(options));
        }

        return ConfigureHttpClient(httpClient);
    }

    /// <summary>
    ///     Applies the settings every client of a connection needs.
    /// </summary>
    private static HttpClient ConfigureHttpClient(HttpClient httpClient)
    {
        // Timeouts are managed per request by B1SLayer, so the HttpClient's own timeout must not interfere
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

        // Expect: 100-continue handshakes stall SAP's Apache front end; disabling it once at the
        // client level covers every request sent through this connection
        httpClient.DefaultRequestHeaders.ExpectContinue = false;
        return httpClient;
    }

    /// <summary>
    ///     Creates and configures the handler used when no custom <see cref="System.Net.Http.HttpClient" />
    ///     or <see cref="HttpMessageHandler" /> is provided.
    /// </summary>
    internal static HttpClientHandler CreateDefaultHandler(SLConnectionOptions options) => SLHttpHandlers.Create(options.ServerCertificateValidationCallback, options.ValidateServerCertificate);

    /// <summary>
    ///     Creates and configures the handler for requests sent with an access token when no custom
    ///     <see cref="System.Net.Http.HttpClient" /> or <see cref="HttpMessageHandler" /> is provided.
    /// </summary>
    internal static HttpClientHandler CreateAccessTokenHandler(SLConnectionOptions options) => SLHttpHandlers.Create(options.AccessTokenServerCertificateValidationCallback, options.ValidateAccessTokenServerCertificate);
}
