using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

using Microsoft.Extensions.Caching.Distributed;

namespace B1SLayer;

/// <summary>
///     Represents a connection to the Service Layer.
/// </summary>
/// <remarks>
///     Only one instance per company/user should be used in the application.
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

        if (!IsUsingSingleSignOn)
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
        SessionCacheKey = IsUsingSingleSignOn
            ? $"B1SLayer:SessionCookies:{ServiceLayerRoot}:{Guid.NewGuid()}"
            : $"B1SLayer:SessionCookies:{ServiceLayerRoot}:{CompanyDB}:{UserName}";
        HttpClient = BuildHttpClient(options);
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
    ///     The language code to be used. Specify a code if you want error messages in some specific language other than English.
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
    ///     The language code to be used. Specify a code if you want error messages in some specific language other than English.
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
    ///     The language code to be used. Specify a code if you want error messages in some specific language other than English.
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
    ///     The timeout value in minutes for a Service Layer session. If the configured value differs from the default 30 minutes,
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
    ///     The timeout value in minutes for a Service Layer session. If the configured value differs from the default 30 minutes,
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
    public HttpClient HttpClient { get; }

    /// <summary>
    ///     Gets the <see cref="IDistributedCache" /> implementation to be used for session management. By default, an in-memory implementation is used.
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
    ///     Gets or sets the number of attempts for each unsuccessful request in case of an HTTP status code contained in <see cref="HttpStatusCodesToRetry" />.
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
    ///     Gets or sets the <see cref="System.Text.Json.JsonSerializerOptions" /> used to serialize and deserialize request and response bodies.
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
            DisableHandlerCookieManagement(options.HttpMessageHandler);
            httpClient = new HttpClient(options.HttpMessageHandler, false);
        }
        else
        {
            httpClient = new HttpClient(CreateDefaultHandler(options));
        }

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
    internal static HttpClientHandler CreateDefaultHandler(SLConnectionOptions options)
    {
        var handler = new HttpClientHandler();

        try
        {
            handler.UseCookies = false;
        }
        catch (PlatformNotSupportedException)
        {
            // Not configurable on some platforms (e.g. Blazor WebAssembly)
        }

        if (handler.SupportsRedirectConfiguration)
        {
            // Redirects are followed by B1SLayer itself (see SendWithRedirectsAsync) rather than by the
            // handler, so Set-Cookie headers on intermediate hops (e.g. a load balancer's affinity
            // cookie) are captured and the applicable cookies re-evaluated against each hop's URL
            handler.AllowAutoRedirect = false;
        }

        if (handler.SupportsAutomaticDecompression)
        {
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        }

        if (options.ServerCertificateValidationCallback != null)
        {
            // A custom validation callback (e.g. certificate pinning) takes precedence over the validation toggle.
            // Deliberately not guarded: an explicitly requested callback that can not be applied
            // (e.g. on Blazor WebAssembly) must fail loudly rather than silently validate differently
            handler.ServerCertificateCustomValidationCallback = options.ServerCertificateValidationCallback;
        }
        else if (!options.ValidateServerCertificate)
        {
            try
            {
                handler.ServerCertificateCustomValidationCallback = (a, b, c, d) => true;
            }
            catch (PlatformNotSupportedException)
            {
                // Not configurable on some platforms (e.g. Blazor WebAssembly), where certificate
                // validation is handled by the platform itself
            }
        }

        return handler;
    }

    /// <summary>
    ///     Disables cookie management on the innermost handler of the provided handler (chain).
    ///     Session cookies are managed by B1SLayer and sent through the Cookie header,
    ///     which handler-level cookie management would silently discard or interfere with.
    /// </summary>
    private static void DisableHandlerCookieManagement(HttpMessageHandler handler)
    {
        while (handler is DelegatingHandler delegatingHandler && delegatingHandler.InnerHandler != null) handler = delegatingHandler.InnerHandler;

        if (handler is HttpClientHandler httpClientHandler)
        {
            try
            {
                httpClientHandler.UseCookies = false;
            }
            catch (PlatformNotSupportedException)
            {
                // Not configurable on some platforms (e.g. Blazor WebAssembly)
            }
            catch (InvalidOperationException)
            {
                // The handler has already served requests and can no longer be configured; it is
                // left untouched, as cookie management may have been disabled beforehand by the caller
            }
        }
#if !NETSTANDARD2_0
        else if (handler is SocketsHttpHandler socketsHttpHandler)
        {
            try
            {
                socketsHttpHandler.UseCookies = false;
            }
            catch (InvalidOperationException)
            {
                // Same as above: an already-started handler can no longer be configured
            }
        }
#endif
    }
}
