using Flurl;
using Flurl.Http;
using Flurl.Http.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer;

/// <summary>
/// Represents a connection to the Service Layer.
/// </summary>
/// <remarks>
/// Only one instance per company/user should be used in the application.
/// </remarks>
public class SLConnection
{
    #region Fields

    private SLLoginResponse _loginResponse;
    private TimeSpan _batchRequestTimeout = TimeSpan.FromSeconds(300);
    private readonly Func<string, string> _getServiceLayerConnectionContext;
    private readonly int _ssoSessionTimeout;
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    #endregion

    #region Properties

    /// <summary>
    /// Gets the <see cref="IFlurlClient"/> responsible for the requests to Service Layer.
    /// </summary>
    public IFlurlClient Client { get; private set; }

    /// <summary>
    /// Gets the <see cref="IDistributedCache"/> implementation to be used for session management. By default, an in-memory implementation is used.
    /// </summary>
    public IDistributedCache DistributedCache { get; private set; } = B1SLayerSettings.DistributedCache;

    /// <summary>
    /// Gets or sets cache key to be used for the session cache.
    /// </summary>
    public string SessionCacheKey { get; set; }

    /// <summary>
    /// Gets the Service Layer root URI.
    /// </summary>
    public Uri ServiceLayerRoot { get; private set; }

    /// <summary>
    /// Gets the Company database (schema) to connect to.
    /// </summary>
    public string CompanyDB { get; private set; }

    /// <summary>
    /// Gets the username to be used for the Service Layer authentication.
    /// </summary>
    public string UserName { get; private set; }

    /// <summary>
    /// Gets the password for the provided username.
    /// </summary>
    public string Password { get; private set; }

    /// <summary>
    /// Gets the Service Layer language code provided.
    /// </summary>
    public int? Language { get; private set; }

    /// <summary>
    /// Gets or sets the number of attempts for each unsuccessful request in case of an HTTP status code contained in <see cref="HttpStatusCodesToRetry"/>.
    /// </summary>
    public int NumberOfAttempts { get; set; }

    /// <summary>
    /// Gets or sets the timespan to wait before a batch request times out. The default value is 5 minutes (300 seconds).
    /// </summary>
    public TimeSpan BatchRequestTimeout
    {
        get => _batchRequestTimeout;
        set
        {
            if (value != Timeout.InfiniteTimeSpan &&
                (value <= TimeSpan.Zero || value > TimeSpan.FromMilliseconds(int.MaxValue)))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _batchRequestTimeout = value;
        }
    }

    /// <summary>
    /// Gets whether this <see cref="SLConnection"/> instance is using Single Sign-On (SSO) authentication.
    /// </summary>
    public bool IsUsingSingleSignOn { get; private set; }

    /// <summary>
    /// Gets information about the latest Login request.
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
    /// Gets a list of <see cref="HttpStatusCode"/> to be checked before retrying an unsuccessful request.
    /// </summary>
    /// <remarks>
    /// The number of attempts is defined by <see cref="NumberOfAttempts"/>.
    /// </remarks>
    public IList<HttpStatusCode> HttpStatusCodesToRetry { get; } =
    [
        HttpStatusCode.Unauthorized,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    ];

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="SLConnection"/> class.
    /// Only one instance per company/user should be used in the application.
    /// </summary>
    /// <param name="serviceLayerRoot">
    /// The Service Layer root URI. The expected format is https://[server]:[port]/b1s/[version]
    /// </param>
    /// <param name="companyDB">
    /// The Company database (schema) to connect to.
    /// </param>
    /// <param name="userName">
    /// The SAP user to be used for the Service Layer authentication.
    /// </param>
    /// <param name="password">
    /// The password for the provided user.
    /// </param>
    /// <param name="language">
    /// The language code to be used. Specify a code if you want error messages in some specific language other than English.
    /// A GET request to the UserLanguages resource will return all available languages and their respective codes.
    /// </param>
    /// <param name="numberOfAttempts">
    /// The number of attempts for each request in case of an HTTP response code of 401, 500, 502, 503 or 504.
    /// If the response code is 401 (Unauthorized), a login request will be performed before the new attempt.
    /// </param>
    public SLConnection(Uri serviceLayerRoot, string companyDB, string userName, string password, int? language = null,
        int numberOfAttempts = 3)
    {
        if (string.IsNullOrWhiteSpace(companyDB))
            throw new ArgumentException("companyDB can not be empty.");

        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("userName can not be empty.");

        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("password can not be empty.");

        ServiceLayerRoot = serviceLayerRoot;
        CompanyDB = companyDB;
        UserName = userName;
        Password = password;
        Language = language;
        NumberOfAttempts = numberOfAttempts;
        LoginResponse = new SLLoginResponse();
        SessionCacheKey = $"B1SLayer:SessionCookies:{ServiceLayerRoot}:{CompanyDB}:{UserName}";
        Client = BuildFlurlClient();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SLConnection"/> class.
    /// Only one instance per company/user should be used in the application.
    /// </summary>
    /// <param name="serviceLayerRoot">
    /// The Service Layer root URI. The expected format is https://[server]:[port]/b1s/[version]
    /// </param>
    /// <param name="companyDB">
    /// The Company database (schema) to connect to.
    /// </param>
    /// <param name="userName">
    /// The SAP user to be used for the Service Layer authentication.
    /// </param>
    /// <param name="password">
    /// The password for the provided user.
    /// </param>
    /// <param name="language">
    /// The language code to be used. Specify a code if you want error messages in some specific language other than English.
    /// A GET request to the UserLanguages resource will return all available languages and their respective codes.
    /// </param>
    /// <param name="numberOfAttempts">
    /// The number of attempts for each request in case of an HTTP response code of 401, 500, 502, 503 or 504.
    /// If the response code is 401 (Unauthorized), a login request will be performed before the new attempt.
    /// </param>
    public SLConnection(string serviceLayerRoot, string companyDB, string userName, string password,
        int? language = null, int numberOfAttempts = 3)
        : this(new Uri(serviceLayerRoot), companyDB, userName, password, language, numberOfAttempts)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SLConnection"/> class.
    /// Only one instance per company/user should be used in the application.
    /// </summary>
    /// <param name="serviceLayerRoot">
    /// The Service Layer root URI. The expected format is https://[server]:[port]/b1s/[version]
    /// </param>
    /// <param name="companyDB">
    /// The Company database (schema) to connect to.
    /// </param>
    /// <param name="userName">
    /// The SAP user to be used for the Service Layer authentication.
    /// </param>
    /// <param name="password">
    /// The password for the provided user.
    /// </param>
    public SLConnection(string serviceLayerRoot, string companyDB, string userName, string password)
        : this(new Uri(serviceLayerRoot), companyDB, userName, password, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SLConnection"/> class.
    /// Only one instance per company/user should be used in the application.
    /// </summary>
    /// <param name="serviceLayerRoot">
    /// The Service Layer root URI. The expected format is https://[server]:[port]/b1s/[version]
    /// </param>
    /// <param name="companyDB">
    /// The Company database (schema) to connect to.
    /// </param>
    /// <param name="userName">
    /// The SAP user to be used for the Service Layer authentication.
    /// </param>
    /// <param name="password">
    /// The password for the provided user.
    /// </param>
    /// <param name="language">
    /// The language code to be used. Specify a code if you want error messages in some specific language other than English.
    /// A GET request to the UserLanguages resource will return all available languages and their respective codes.
    /// </param>
    public SLConnection(string serviceLayerRoot, string companyDB, string userName, string password, int? language)
        : this(new Uri(serviceLayerRoot), companyDB, userName, password, language)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SLConnection"/> class using Single Sign-On (SSO) authentication.
    /// </summary>
    /// <param name="serviceLayerRoot">
    /// The Service Layer root URI. The expected format is https://[server]:[port]/b1s/[version]
    /// </param>
    /// <param name="getServiceLayerConnectionContext">
    /// The reference for the UI API method responsible for obtaining the connection context
    /// (SAPbouiCOM.Framework.Application.SBO_Application.Company.GetServiceLayerConnectionContext). 
    /// </param>
    /// <param name="sessionTimeout">
    /// The timeout value in minutes for a Service Layer session. If the configured value differs from the default 30 minutes,
    /// specify it through this parameter. Check the "SessionTimeout" property in the file "b1s.conf" on the server.
    /// </param>
    /// <param name="numberOfAttempts">
    /// The number of attempts for each request in case of an HTTP response code of 401, 500, 502, 503 or 504.
    /// If the response code is 401 (Unauthorized), a login request will be performed before the new attempt.
    /// </param>
    public SLConnection(Uri serviceLayerRoot, Func<string, string> getServiceLayerConnectionContext,
        int sessionTimeout = 30, int numberOfAttempts = 3)
    {
        ServiceLayerRoot = serviceLayerRoot;
        NumberOfAttempts = numberOfAttempts;
        LoginResponse = new SLLoginResponse();
        IsUsingSingleSignOn = true;
        SessionCacheKey = $"B1SLayer:SessionCookies:{ServiceLayerRoot}:{Guid.NewGuid()}";
        Client = BuildFlurlClient();
        _getServiceLayerConnectionContext = getServiceLayerConnectionContext;
        _ssoSessionTimeout = sessionTimeout;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SLConnection"/> class using Single Sign-On (SSO) authentication.
    /// </summary>
    /// <param name="serviceLayerRoot">
    /// The Service Layer root URI. The expected format is https://[server]:[port]/b1s/[version]
    /// </param>
    /// <param name="getServiceLayerConnectionContext">
    /// The reference for the UI API method responsible for obtaining the connection context
    /// (SAPbouiCOM.Framework.Application.SBO_Application.Company.GetServiceLayerConnectionContext). 
    /// </param>
    /// <param name="sessionTimeout">
    /// The timeout value in minutes for a Service Layer session. If the configured value differs from the default 30 minutes,
    /// specify it through this parameter. Check the "SessionTimeout" property in the file "b1s.conf" on the server.
    /// </param>
    /// <param name="numberOfAttempts">
    /// The number of attempts for each request in case of an HTTP response code of 401, 500, 502, 503 or 504.
    /// If the response code is 401 (Unauthorized), a login request will be performed before the new attempt.
    /// </param>
    public SLConnection(string serviceLayerRoot, Func<string, string> getServiceLayerConnectionContext,
        int sessionTimeout = 30, int numberOfAttempts = 3)
        : this(new Uri(serviceLayerRoot), getServiceLayerConnectionContext, sessionTimeout, numberOfAttempts)
    {
    }

    #endregion

    #region Configuration Methods

    private IFlurlClient BuildFlurlClient()
    {
        return new FlurlClientBuilder(ServiceLayerRoot.ToString())
            .ConfigureHttpClient(httpClient =>
            {
                httpClient.DefaultRequestHeaders.ExpectContinue = false;
            })
            .ConfigureInnerHandler(handler =>
            {
                handler.ServerCertificateCustomValidationCallback = (a, b, c, d) => true;
            })
            .WithSettings(settings =>
            {
                settings.JsonSerializer = new SystemTextJsonSerializer(new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });
            })
            .Build();
    }

    #endregion

    #region Session Management Methods

    /// <summary>
    /// Performs a POST Login request with the provided information, regardless of the current session state.
    /// </summary>
    /// <remarks>
    /// Manually performing the Login is often unnecessary because it will be performed automatically anyway whenever needed.
    /// </remarks>
    public async Task<SLLoginResponse> LoginAsync() => await ExecuteLoginAsync(true);

    /// <summary>
    /// Performs the POST Login request to the Service Layer.
    /// </summary>
    /// <param name="expectReturn">
    /// Whether the login information should be returned.
    /// </param>
    private async Task<SLLoginResponse> ExecuteLoginAsync(bool expectReturn = false)
    {
        // Prevents multiple login requests in a multi-threaded scenario
        await _semaphoreSlim.WaitAsync();

        try
        {
            if (!IsUsingSingleSignOn)
            {
                _loginResponse = await Client
                    .Request("Login")
                    .WithCookies(out var cookieJar)
                    .PostJsonAsync(new { CompanyDB, UserName, Password, Language })
                    .ReceiveJson<SLLoginResponse>();

                _loginResponse.LastLogin = DateTime.Now;
                await SetSessionCookiesAsync(cookieJar, TimeSpan.FromMinutes(_loginResponse.SessionTimeout));
            }
            else
            {
                // Obtains session context from UI API method
                string connectionContext = _getServiceLayerConnectionContext(ServiceLayerRoot.ToString());
                var cookies = CreateCookieJarFromConnectionContext(connectionContext);
                await SetSessionCookiesAsync(cookies, TimeSpan.FromMinutes(_ssoSessionTimeout));
                _loginResponse.LastLogin = DateTime.Now;
                _loginResponse.SessionTimeout = _ssoSessionTimeout;
                _loginResponse.SessionId = cookies
                    .FirstOrDefault(x => x.Name.Equals("B1SESSION", StringComparison.OrdinalIgnoreCase))?.Value;
            }

            return expectReturn ? LoginResponse : null;
        }
        catch (FlurlHttpException ex)
        {
            try
            {
                if (ex.Call.HttpResponseMessage == null) throw;
                var response = await ex.GetResponseJsonAsync<SLResponseError>();
                throw new SLException(response.Error.Message.Value, response.Error, ex);
            }
            catch (SLException slEx)
            {
                throw slEx;
            }
            catch
            {
                throw ex;
            }
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    /// <summary>
    /// Obtains the current session cookies from the distribued cache.
    /// A login is performed if no valid cookies are retrieved from cache.
    /// </summary>
    /// <returns>
    /// The current session cookies to be used in each request.
    /// </returns>
    internal async Task<CookieJar> GetSessionCookiesAsync()
    {
        var cookiesString = await DistributedCache.GetStringAsync(SessionCacheKey);

        if (string.IsNullOrEmpty(cookiesString))
        {
            await ExecuteLoginAsync();
            cookiesString = await DistributedCache.GetStringAsync(SessionCacheKey);
        }
        else
            await DistributedCache.RefreshAsync(SessionCacheKey);

        return string.IsNullOrEmpty(cookiesString)
            ? null
            : CookieJar.LoadFromString(cookiesString);
    }

    /// <summary>
    /// Sets the given session cookies to the distributed cache.
    /// </summary>
    /// <param name="cookies">
    /// The cookies to be set to the distributed cache.
    /// </param>
    /// <param name="slidingExpiration">
    /// The sliding expiration time for the session cache.
    /// </param>
    private async Task SetSessionCookiesAsync(CookieJar cookies, TimeSpan slidingExpiration)
    {
        var cookieString = cookies?.ToString();
        if (!string.IsNullOrEmpty(cookieString))
            await DistributedCache.SetStringAsync(SessionCacheKey, cookieString, new DistributedCacheEntryOptions
            {
                SlidingExpiration = slidingExpiration
            });
    }

    /// <summary>
    /// Removes the current active session from the distributed cache.
    /// </summary>
    public async Task InvalidateSessionCacheAsync()
    {
        await DistributedCache.RemoveAsync(SessionCacheKey);
    }

    /// <summary>
    /// Creates a <see cref="CookieJar"/> instance from the context string obtained from the UI API method "GetServiceLayerConnectionContext";
    /// </summary>
    /// <param name="connectionContext">
    /// The connection context string obtained from UI API.
    /// </param>
    /// <returns>
    /// A <see cref="CookieJar"/> instance containing session cookies obtained after a successful authentication.
    /// </returns>
    private CookieJar CreateCookieJarFromConnectionContext(string connectionContext)
    {
        var cookieJar = new CookieJar();
        var cookies = connectionContext.Replace(',', ';').Split(';')
            .Where(x => !string.IsNullOrEmpty(x) && x.Contains('=') && !x.Contains("path"));

        foreach (var cookie in cookies)
        {
            var cookieKeyValue = cookie.Split('=');

            if (cookieKeyValue.Length != 2) continue;

            if (cookieKeyValue[0].Equals("B1SESSION", StringComparison.OrdinalIgnoreCase) ||
                cookieKeyValue[0].Equals("CompanyDB", StringComparison.OrdinalIgnoreCase))
            {
                cookieJar.AddOrReplace(
                    new FlurlCookie(cookieKeyValue[0], cookieKeyValue[1], ServiceLayerRoot.AppendPathSegment("Login"))
                    { HttpOnly = true, Secure = true });
            }
            else if (cookieKeyValue[0].Equals("ROUTEID", StringComparison.OrdinalIgnoreCase))
            {
                cookieJar.AddOrReplace(
                    new FlurlCookie(cookieKeyValue[0], cookieKeyValue[1], ServiceLayerRoot.AppendPathSegment("Login"))
                    { Path = "/", Secure = true });
            }
        }

        return cookieJar;
    }

    /// <summary>
    /// Performs a POST Logout request, ending the current session.
    /// </summary>
    public async Task LogoutAsync()
    {
        var currentSessionCookies = await GetSessionCookiesAsync();
        if (currentSessionCookies == null) return;

        try
        {
            await Client.Request("Logout").WithCookies(currentSessionCookies).PostAsync();
            await InvalidateSessionCacheAsync();
            _loginResponse = new SLLoginResponse();
        }
        catch (FlurlHttpException ex)
        {
            try
            {
                if (ex.Call.HttpResponseMessage == null) throw;
                var response = await ex.GetResponseJsonAsync<SLResponseError>();
                throw new SLException(response.Error.Message.Value, response.Error, ex);
            }
            catch (SLException slEx)
            {
                throw slEx;
            }
            catch
            {
                throw ex;
            }
        }
    }

    #endregion

    #region Request Methods

    /// <summary>
    /// Initializes a new instance of the <see cref="SLRequest"/> class that represents a request to the associated <see cref="SLConnection"/>. 
    /// </summary>
    /// <remarks>
    /// The request can be configured using the extension methods provided in <see cref="SLRequestExtensions"/>.
    /// </remarks>
    /// <param name="resource">
    /// The resource name to be requested.
    /// </param>
    public SLRequest Request(string resource) =>
        new SLRequest(this, Client.Request(resource));

    /// <summary>
    /// Initializes a new instance of the <see cref="SLRequest"/> class that represents a request to the associated <see cref="SLConnection"/>. 
    /// </summary>
    /// <remarks>
    /// The request can be configured using the extension methods provided in <see cref="SLRequestExtensions"/>.
    /// </remarks>
    /// <param name="resource">
    /// The resource name to be requested.
    /// </param>
    /// <param name="id">
    /// The entity ID to be requested.
    /// </param>
    public SLRequest Request(string resource, object id) =>
        new SLRequest(this, Client.Request(id is string ? $"{resource}('{id}')" : $"{resource}({id})"));

    /// <summary>
    /// Calls the Login method to ensure a valid session and then executes the provided request.
    /// If the request is unsuccessfull with any return code present in <see cref="HttpStatusCodesToRetry"/>, 
    /// it will be retried for <see cref="NumberOfAttempts"/> number of times.
    /// </summary>
    internal async Task<T> ExecuteRequest<T>(Func<Task<T>> action)
    {
        bool loginReattempted = false;
        List<Exception> exceptions = null;

        if (NumberOfAttempts < 1)
            throw new ArgumentException("The number of attempts can not be lower than 1.");

        for (int i = 0; i < NumberOfAttempts || loginReattempted; i++)
        {
            loginReattempted = false;

            try
            {
                var result = await action();
                return result;
            }
            catch (FlurlHttpException ex)
            {
                exceptions ??= new List<Exception>();

                try
                {
                    if (ex.Call.HttpResponseMessage == null)
                        throw;

                    var response = await ex.GetResponseJsonAsync<SLResponseError>();
                    exceptions.Add(new SLException(response.Error.Message.Value, response.Error, ex));
                }
                catch
                {
                    exceptions.Add(ex);
                }

                // Whether the request should be retried
                if (!HttpStatusCodesToRetry.Any(x => x == ex.Call.HttpResponseMessage?.StatusCode))
                {
                    break;
                }

                // Forces a new login request in case the response is 401 Unauthorized
                if (ex.Call.HttpResponseMessage?.StatusCode == HttpStatusCode.Unauthorized)
                {
                    if (i >= NumberOfAttempts)
                        break;

                    await ExecuteLoginAsync();
                    loginReattempted = true;
                }
            }
            catch (Exception)
            {
                throw;
            }

            await Task.Delay(200);
        }

        var uniqueExceptions = exceptions.Distinct(new ExceptionEqualityComparer());

        if (uniqueExceptions.Count() == 1)
            throw uniqueExceptions.First();

        throw new AggregateException("Could not process request", uniqueExceptions);
    }

    /// <summary>
    /// Provides a direct response from the Apache server that can be used for network testing and component monitoring.
    /// In response to a PING request, the Apache server (load balancer or node) will respond directly with a simple PONG response.
    /// </summary>
    /// <remarks>
    /// This feature is only available on version 9.3 PL10 or above. See SAP Note <see href="https://launchpad.support.sap.com/#/notes/2796799">2796799</see> for more details. 
    /// </remarks>
    /// <returns>
    /// A <see cref="SLPingResponse"/> object containing the response details.
    /// </returns>
    public async Task<SLPingResponse> PingAsync() => await ExecutePingAsync("ping/");

    /// <summary>
    /// Provides a direct response from the Apache server that can be used for network testing and component monitoring.
    /// In response to a PING request, the Apache server (load balancer or node) will respond directly with a simple PONG response.
    /// </summary>
    /// <remarks>
    /// This feature is only available on version 9.3 PL10 or above. See SAP Note <see href="https://launchpad.support.sap.com/#/notes/2796799">2796799</see> for more details. 
    /// </remarks>
    /// <param name="node">
    /// The specific node to be monitored. If not specified, the request will be directed to the load balancer.
    /// </param>
    /// <returns>
    /// A <see cref="SLPingResponse"/> object containing the response details.
    /// </returns>
    public async Task<SLPingResponse> PingNodeAsync(int? node = null) =>
        await ExecutePingAsync(node.HasValue ? $"ping/node/{node}" : "ping/load-balancer");

    /// <summary>
    /// Performs the ping request with the provided path segment.
    /// </summary>
    private async Task<SLPingResponse> ExecutePingAsync(string path)
    {
        try
        {
            var pingRequest = Client.Request();
            pingRequest.Url = pingRequest.Url.RemovePath().AppendPathSegment(path);
            var flurlResponse = await pingRequest.GetAsync();
            var pingResponse = await flurlResponse.GetJsonAsync<SLPingResponse>();
            pingResponse.IsSuccessStatusCode = flurlResponse.ResponseMessage.IsSuccessStatusCode;
            pingResponse.StatusCode = flurlResponse.ResponseMessage.StatusCode;
            return pingResponse;
        }
        catch (FlurlHttpException ex)
        {
            try
            {
                if (ex.Call.HttpResponseMessage == null) throw;
                var pingResponse = await ex.GetResponseJsonAsync<SLPingResponse>();
                pingResponse.IsSuccessStatusCode = ex.Call.HttpResponseMessage.IsSuccessStatusCode;
                pingResponse.StatusCode = ex.Call.HttpResponseMessage.StatusCode;
                return pingResponse;
            }
            catch
            {
                throw ex;
            }
        }
        catch (Exception)
        {
            throw;
        }
    }

    #endregion

    #region Call Event Handlers

    /// <summary>
    /// Sets a <see cref="Func{T, TResult}"/> delegate that is called before every Service Layer request.
    /// </summary>
    /// <remarks>
    /// The <see cref="FlurlCall"/> object provides various details about the call than can be used for logging and error handling.
    /// Response-related properties will be null in BeforeCall.
    /// </remarks>
    public SLConnection BeforeCall(Func<FlurlCall, Task> action)
    {
        Client.BeforeCall(action);
        return this;
    }

    /// <summary>
    /// Sets a <see cref="Action{T}"/> delegate that is called before every Service Layer request.
    /// </summary>
    /// <remarks>
    /// The <see cref="FlurlCall"/> object provides various details about the call than can be used for logging and error handling.
    /// Response-related properties will be null in BeforeCall.
    /// </remarks>
    public SLConnection BeforeCall(Action<FlurlCall> action)
    {
        Client.BeforeCall(action);
        return this;
    }

    /// <summary>
    /// Sets a <see cref="Func{T, TResult}"/> delegate that is called after every Service Layer request.
    /// </summary>
    /// <remarks>
    /// The <see cref="FlurlCall"/> object provides various details about the call than can be used for logging and error handling.
    /// </remarks>
    public SLConnection AfterCall(Func<FlurlCall, Task> action)
    {
        Client.AfterCall(action);
        return this;
    }

    /// <summary>
    /// Sets a <see cref="Action{T}"/> delegate that is called after every Service Layer request.
    /// </summary>
    /// <remarks>
    /// The <see cref="FlurlCall"/> object provides various details about the call than can be used for logging and error handling.
    /// </remarks>
    public SLConnection AfterCall(Action<FlurlCall> action)
    {
        Client.AfterCall(action);
        return this;
    }

    /// <summary>
    /// Sets a <see cref="Func{T, TResult}"/> delegate that is called after every unsuccessful Service Layer request.
    /// </summary>
    /// <remarks>
    /// The <see cref="FlurlCall"/> object provides various details about the call than can be used for logging and error handling.
    /// </remarks>
    public SLConnection OnError(Func<FlurlCall, Task> action)
    {
        Client.OnError(action);
        return this;
    }

    /// <summary>
    /// Sets a <see cref="Action{T}"/> delegate that is called after every unsuccessful Service Layer request.
    /// </summary>
    /// <remarks>
    /// The <see cref="FlurlCall"/> object provides various details about the call than can be used for logging and error handling.
    /// </remarks>
    public SLConnection OnError(Action<FlurlCall> action)
    {
        Client.OnError(action);
        return this;
    }

    #endregion

    #region Attachments Methods

    /// <summary>
    /// Uploads the provided file as an attachment.
    /// </summary>
    /// <remarks>
    /// An attachment folder must be defined. See section 'Setting up an Attachment Folder' in the Service Layer User Manual for more details.
    /// </remarks>
    /// <param name="path">
    /// The path to the file to be uploaded.
    /// </param>
    /// <returns>
    /// A <see cref="SLAttachment"/> object with information about the created attachment entry.
    /// </returns>
    public async Task<SLAttachment> PostAttachmentAsync(string path) =>
        await PostAttachmentAsync(Path.GetFileName(path), File.ReadAllBytes(path));

    /// <summary>
    /// Uploads the provided file as an attachment.
    /// </summary>
    /// <remarks>
    /// An attachment folder must be defined. See section 'Setting up an Attachment Folder' in the Service Layer User Manual for more details.
    /// </remarks>
    /// <param name="fileName">
    /// The file name of the file to be uploaded including the file extension.
    /// </param>
    /// <param name="file">
    /// The file to be uploaded.
    /// </param>
    /// <returns>
    /// A <see cref="SLAttachment"/> object with information about the created attachment entry.
    /// </returns>
    public async Task<SLAttachment> PostAttachmentAsync(string fileName, byte[] file) =>
        await PostAttachmentsAsync(new Dictionary<string, Stream>() { { fileName, new MemoryStream(file) } });

    /// <summary>
    /// Uploads the provided file as an attachment.
    /// </summary>
    /// <remarks>
    /// An attachment folder must be defined. See section 'Setting up an Attachment Folder' in the Service Layer User Manual for more details.
    /// </remarks>
    /// <param name="fileName">
    /// The file name of the file to be uploaded including the file extension.
    /// </param>
    /// <param name="file">
    /// The file to be uploaded.
    /// </param>
    /// <returns>
    /// A <see cref="SLAttachment"/> object with information about the created attachment entry.
    /// </returns>
    public async Task<SLAttachment> PostAttachmentAsync(string fileName, Stream file) =>
        await PostAttachmentsAsync(new Dictionary<string, Stream>() { { fileName, file } });

    /// <summary>
    /// Uploads the provided files as an attachment. All files will be posted as attachment lines in a single attachment entry.
    /// </summary>
    /// <remarks>
    /// An attachment folder must be defined. See section 'Setting up an Attachment Folder' in the Service Layer User Manual for more details.
    /// </remarks>
    /// <param name="files">
    /// A Dictionary containing the files to be uploaded, where the file name is the Key and the file is the Value.
    /// </param>
    /// <returns>
    /// A <see cref="SLAttachment"/> object with information about the created attachment entry.
    /// </returns>
    public async Task<SLAttachment> PostAttachmentsAsync(IDictionary<string, byte[]> files) =>
        await PostAttachmentsAsync(files.ToDictionary(x => x.Key, x => (Stream)new MemoryStream(x.Value)));

    /// <summary>
    /// Uploads the provided files as an attachment. All files will be posted as attachment lines in a single attachment entry.
    /// </summary>
    /// <remarks>
    /// An attachment folder must be defined. See section 'Setting up an Attachment Folder' in the Service Layer User Manual for more details.
    /// </remarks>
    /// <param name="files">
    /// A Dictionary containing the files to be uploaded, where the file name is the Key and the file is the Value.
    /// </param>
    /// <returns>
    /// A <see cref="SLAttachment"/> object with information about the created attachment entry.
    /// </returns>
    public async Task<SLAttachment> PostAttachmentsAsync(IDictionary<string, Stream> files)
    {
        return await ExecuteRequest(async () =>
        {
            if (files == null || files.Count == 0)
            {
                throw new ArgumentException("No files to be sent.");
            }

            var result = await Client
                .Request("Attachments2")
                .WithCookies(await GetSessionCookiesAsync())
                .PostMultipartAsync(mp =>
                {
                    // Removes double quotes from boundary, otherwise the request fails with error 405 Method Not Allowed
                    var boundary = mp.Headers.ContentType.Parameters.First(o =>
                        o.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase));
                    boundary.Value = boundary.Value.Replace("\"", string.Empty);

                    foreach (var file in files)
                    {
                        var content = new StreamContent(file.Value);
                        content.Headers.Add("Content-Disposition",
                            $"form-data; name=\"files\"; filename=\"{file.Key}\"");
                        content.Headers.Add("Content-Type", "application/octet-stream");
                        mp.Add(content);
                    }
                })
                .ReceiveJson<SLAttachment>();

            return result;
        });
    }

    /// <summary>
    /// Updates an existing attachment entry with the provided file. If the file already exists
    /// in the attachment entry, it will be replaced. Otherwise, a new attachment line is added.
    /// </summary>
    /// <param name="attachmentEntry">
    /// The attachment entry ID to be updated.
    /// </param>
    /// <param name="path">
    /// The file path for the file to be updated including the file extension.
    /// </param>
    public async Task PatchAttachmentAsync(int attachmentEntry, string path) =>
        await PatchAttachmentAsync(attachmentEntry, Path.GetFileName(path), File.ReadAllBytes(path));

    /// <summary>
    /// Updates an existing attachment entry with the provided file. If the file already exists
    /// in the attachment entry, it will be replaced. Otherwise, a new attachment line is added.
    /// </summary>
    /// <param name="attachmentEntry">
    /// The attachment entry ID to be updated.
    /// </param>
    /// <param name="fileName">
    /// The file name of the file to be updated including the file extension.
    /// </param>
    /// <param name="file">
    /// The file to be updated.
    /// </param>
    public async Task PatchAttachmentAsync(int attachmentEntry, string fileName, byte[] file) =>
        await PatchAttachmentsAsync(attachmentEntry,
            new Dictionary<string, Stream>() { { fileName, new MemoryStream(file) } });

    /// <summary>
    /// Updates an existing attachment entry with the provided file. If the file already exists
    /// in the attachment entry, it will be replaced. Otherwise, a new attachment line is added.
    /// </summary>
    /// <param name="attachmentEntry">
    /// The attachment entry ID to be updated.
    /// </param>
    /// <param name="fileName">
    /// The file name of the file to be updated including the file extension.
    /// </param>
    /// <param name="file">
    /// The file to be updated.
    /// </param>
    public async Task PatchAttachmentAsync(int attachmentEntry, string fileName, Stream file) =>
        await PatchAttachmentsAsync(attachmentEntry, new Dictionary<string, Stream>() { { fileName, file } });

    /// <summary>
    /// Updates an existing attachment entry with the provided files. If the file already exists
    /// in the attachment entry, it will be replaced. Otherwise, a new attachment line is added.
    /// </summary>
    /// <param name="attachmentEntry">
    /// The attachment entry ID to be updated.
    /// </param>
    /// <param name="files">
    /// A Dictionary containing the files to be updated, where the file name is the Key and the file is the Value.
    /// </param>
    public async Task PatchAttachmentsAsync(int attachmentEntry, IDictionary<string, byte[]> files) =>
        await PatchAttachmentsAsync(attachmentEntry,
            files.ToDictionary(x => x.Key, x => (Stream)new MemoryStream(x.Value)));

    /// <summary>
    /// Updates an existing attachment entry with the provided files. If the file already exists
    /// in the attachment entry, it will be replaced. Otherwise, a new attachment line is added.
    /// </summary>
    /// <param name="attachmentEntry">
    /// The attachment entry ID to be updated.
    /// </param>
    /// <param name="files">
    /// A Dictionary containing the files to be updated, where the file name is the Key and the file is the Value.
    /// </param>
    public async Task PatchAttachmentsAsync(int attachmentEntry, IDictionary<string, Stream> files)
    {
        await ExecuteRequest(async () =>
        {
            if (files == null || files.Count == 0)
            {
                throw new ArgumentException("No files to be sent.");
            }

            var result = await Client
                .Request($"Attachments2({attachmentEntry})")
                .WithCookies(await GetSessionCookiesAsync())
                .PatchMultipartAsync(mp =>
                {
                    // Removes double quotes from boundary, otherwise the request fails with error 405 Method Not Allowed
                    var boundary = mp.Headers.ContentType.Parameters.First(o =>
                        o.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase));
                    boundary.Value = boundary.Value.Replace("\"", string.Empty);

                    foreach (var file in files)
                    {
                        var content = new StreamContent(file.Value);
                        content.Headers.Add("Content-Disposition",
                            $"form-data; name=\"files\"; filename=\"{file.Key}\"");
                        content.Headers.Add("Content-Type", "application/octet-stream");
                        mp.Add(content);
                    }
                });

            return result;
        });
    }

    /// <summary>
    /// Downloads the specified attachment file as a <see cref="Stream"/>. By default, the first attachment
    /// line is downloaded if there are multiple attachment lines in one attachment.
    /// </summary>
    /// <param name="attachmentEntry">
    /// The attachment entry ID to be downloaded.
    /// </param>
    /// <param name="fileName">
    /// The file name of the attachment to be downloaded  (including the file extension). Only required if 
    /// you want to download an attachment line other than the first attachment line.
    /// </param>
    /// <returns>
    /// The downloaded attachment file as a <see cref="Stream"/>.
    /// </returns>
    public async Task<Stream> GetAttachmentAsStreamAsync(int attachmentEntry, string fileName = null) =>
        new MemoryStream(await GetAttachmentAsBytesAsync(attachmentEntry, fileName));

    /// <summary>
    /// Downloads the specified attachment file as a <see cref="byte"/> array. By default, the first attachment
    /// line is downloaded if there are multiple attachment lines in one attachment.
    /// </summary>
    /// <param name="attachmentEntry">
    /// The attachment entry ID to be downloaded.
    /// </param>
    /// <param name="fileName">
    /// The file name of the attachment to be downloaded  (including the file extension). Only required if 
    /// you want to download an attachment line other than the first attachment line.
    /// </param>
    /// <returns>
    /// The downloaded attachment file as a <see cref="byte"/> array.
    /// </returns>
    public async Task<byte[]> GetAttachmentAsBytesAsync(int attachmentEntry, string fileName = null)
    {
        return await ExecuteRequest(async () =>
        {
            var file = await Client
                .Request($"Attachments2({attachmentEntry})/$value")
                .SetQueryParam("filename", !string.IsNullOrEmpty(fileName) ? $"'{fileName}'" : null)
                .WithCookies(await GetSessionCookiesAsync())
                .GetBytesAsync();

            return file;
        });
    }

    #endregion

    #region Batch Request Methods

    /// <summary>
    /// Sends a batch request (multiple operations sent in a single HTTP request) with the provided <see cref="SLBatchRequest"/> instances.
    /// All requests are sent in a single change set.
    /// </summary>
    /// <remarks>
    /// See section 'Batch Operations' in the Service Layer User Manual for more details.
    /// </remarks>
    /// <param name="requests">
    /// <see cref="SLBatchRequest"/> instances to be sent in the batch.
    /// </param>
    /// <returns>
    /// An <see cref="HttpResponseMessage"/> array containg the response messages of the batch request. 
    /// </returns>
    public async Task<HttpResponseMessage[]> PostBatchAsync(params SLBatchRequest[] requests)
    {
        return await PostBatchAsync(requests, true);
    }

    /// <summary>
    /// Sends a batch request (multiple operations sent in a single HTTP request) with the provided <see cref="SLBatchRequest"/> collection. 
    /// </summary>
    /// <remarks>
    /// See section 'Batch Operations' in the Service Layer User Manual for more details.
    /// </remarks>
    /// <param name="requests">
    /// A collection of <see cref="SLBatchRequest"/> to be sent in the batch.</param>
    /// <param name="singleChangeSet">
    /// Whether all the requests in this batch should be sent in a single change set. This means that any unsuccessful request will cause the whole batch to be rolled back.
    /// </param>
    /// <returns>
    /// An <see cref="HttpResponseMessage"/> array containg the response messages of the batch request. 
    /// </returns>
    public async Task<HttpResponseMessage[]> PostBatchAsync(IEnumerable<SLBatchRequest> requests, bool singleChangeSet = true)
    {
        return await ExecuteRequest(async () =>
        {
            if (requests == null || !requests.Any())
                throw new ArgumentException("No requests to be sent.");

            HttpResponseMessage batchResponse;

            if (singleChangeSet)
            {
                var singleContent = await BuildMixedMultipartContentAsync(requests);
                var flurlResponse = await Client
                    .Request("$batch")
                    .WithCookies(await GetSessionCookiesAsync())
                    .WithTimeout(BatchRequestTimeout)
                    .PostMultipartAsync(mp =>
                    {
                        mp.Headers.ContentType.MediaType = "multipart/mixed";
                        mp.Add(singleContent);
                    });

                batchResponse = flurlResponse.ResponseMessage;
            }
            else
            {
                var contents = new List<MultipartContent>();
                foreach (var request in requests)
                {
                    string boundary = "changeset_" + Guid.NewGuid();
                    var content = await BuildMixedMultipartContentAsync(request, boundary);
                    contents.Add(content);
                }

                var flurlResponse = await Client
                    .Request("$batch")
                    .WithCookies(await GetSessionCookiesAsync())
                    .WithTimeout(BatchRequestTimeout)
                    .PostMultipartAsync(mp =>
                    {
                        mp.Headers.ContentType.MediaType = "multipart/mixed";
                        foreach (var content in contents)
                        {
                            mp.Add(content);
                        }
                    });

                batchResponse = flurlResponse.ResponseMessage;
            }

            if (batchResponse?.Content is null)
                throw new Exception("The batch request did not return a valid response.");

            var responses = await MultipartHelper.ReadMultipartResponseAsync(batchResponse);
            return responses;
        });
    }

    /// <summary>
    /// Builds the multipart content for a batch request using a single change set.
    /// </summary>
    private async Task<MultipartContent> BuildMixedMultipartContentAsync(IEnumerable<SLBatchRequest> requests)
    {
        string boundary = "changeset_" + Guid.NewGuid();
        var multipartContent = new MultipartContent("mixed", boundary);

        foreach (var batchRequest in requests)
        {
            await BuildRequestForMultipartContentAsync(multipartContent, batchRequest);
        }

        return multipartContent;
    }

    /// <summary>
    /// Builds the multipart content for a batch request using multiple change sets.
    /// </summary>
    private async Task<MultipartContent> BuildMixedMultipartContentAsync(SLBatchRequest batchRequest, string boundary)
    {
        var multipartContent = new MultipartContent("mixed", boundary);
        await BuildRequestForMultipartContentAsync(multipartContent, batchRequest);
        return multipartContent;
    }

    /// <summary>
    /// Builds the <see cref="HttpRequestMessage"/> from a given <see cref="SLBatchRequest"/> and adds it to the <see cref="MultipartContent"/> instance.
    /// </summary>
    private async Task BuildRequestForMultipartContentAsync(MultipartContent multipartContent,
        SLBatchRequest batchRequest)
    {
        var request = new HttpRequestMessage(batchRequest.HttpMethod,
            Url.Combine(ServiceLayerRoot.ToString(), batchRequest.Resource));

        if (batchRequest.HttpVersion != null)
            request.Version = batchRequest.HttpVersion;

        foreach (var header in batchRequest.Headers)
            request.Headers.Add(header.Key, header.Value);

        if (batchRequest.Data != null)
            request.Content = batchRequest.Data is string dataString
                ? new StringContent(dataString, batchRequest.Encoding, "application/json")
                : new StringContent(JsonSerializer.Serialize(batchRequest.Data, batchRequest.JsonSerializerOptions),
                    batchRequest.Encoding, "application/json");

        var innerContent = await MultipartHelper.CreateHttpContentAsync(request);
        innerContent.Headers.Add("content-transfer-encoding", "binary");

        if (batchRequest.ContentID.HasValue)
            innerContent.Headers.Add("Content-ID", batchRequest.ContentID.ToString());

        multipartContent.Add(innerContent);
    }

    #endregion

    #region Private Classes

    /// <summary>
    /// Used to aggregate exceptions that occur on request retries. 
    /// </summary>
    /// <remarks>
    /// In most cases, the same exception will occur multiple times, 
    /// but we don't want to return multiple copies of it. This class is used 
    /// to find exceptions that are duplicates by type and message so we can
    /// only return one of them.
    /// </remarks>
    private class ExceptionEqualityComparer : IEqualityComparer<Exception>
    {
        public bool Equals(Exception e1, Exception e2)
        {
            if (e2 == null && e1 == null)
                return true;
            else if (e1 == null | e2 == null)
                return false;
            else if (e1.GetType().Name.Equals(e2.GetType().Name) && e1.Message.Equals(e2.Message))
                return true;
            else
                return false;
        }

        public int GetHashCode(Exception e)
        {
            return (e.GetType().Name + e.Message).GetHashCode();
        }
    }

    #endregion
}