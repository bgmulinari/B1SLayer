using Flurl;
using Flurl.Http;
using Flurl.Http.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer
{
    /// <summary>
    /// Represents a connection to the Service Layer.
    /// </summary>
    /// <remarks>
    /// Only one instance per company/user should be used in the application.
    /// </remarks>
    public class SLConnection
    {
        #region Fields
        private DateTime _lastRequest = DateTime.MinValue;
        private SLLoginResponse _loginResponse;
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
        private readonly HttpStatusCode[] _returnCodesToRetry = new[]
        {
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.BadGateway,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.GatewayTimeout
        };
        #endregion

        #region Properties
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
        /// Gets or sets the number of attempts for each unsuccessfull request in case of an HTTP response code of 401, 500, 502, 503 or 504.
        /// </summary>
        public int NumberOfAttempts { get; set; }
        /// <summary>
        /// A container that keeps the session cookies returned by the Login request. 
        /// These cookies are sent in every request and overwritten whenever a new Login is performed.
        /// </summary>
        internal CookieJar Cookies { get; private set; }
        /// <summary>
        /// Gets information about the latest Login request.
        /// </summary>
        public SLLoginResponse LoginResponse
        {
            // Returns a new object so the login control can't be manipulated externally
            get => new SLLoginResponse()
            {
                LastLogin = _loginResponse.LastLogin,
                SessionId = _loginResponse.SessionId,
                SessionTimeout = _loginResponse.SessionTimeout,
                Version = _loginResponse.Version
            };

            private set => _loginResponse = value;
        }
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
        public SLConnection(Uri serviceLayerRoot, string companyDB, string userName, string password, int? language = null, int numberOfAttempts = 3)
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

            FlurlHttp.ConfigureClient(ServiceLayerRoot.RemovePath(), client =>
            {
                // Disable SSL certificate verification
                client.Settings.HttpClientFactory = new CustomHttpClientFactory();
                // Ignore null values in JSON
                client.Settings.JsonSerializer = new NewtonsoftJsonSerializer(new JsonSerializerSettings
                {
                    NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
                });
            });
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
        public SLConnection(string serviceLayerRoot, string companyDB, string userName, string password, int? language = null, int numberOfAttempts = 3)
            : this(new Uri(serviceLayerRoot), companyDB, userName, password, language, numberOfAttempts) { }

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
            : this(new Uri(serviceLayerRoot), companyDB, userName, password, null) { }

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
            : this(new Uri(serviceLayerRoot), companyDB, userName, password, language) { }
        #endregion

        #region Authentication Methods
        /// <summary>
        /// If the current session is expired or non-existent, performs a POST Login request with the provided information.
        /// Manually performing the Login is often unnecessary because it will be performed automatically anyway whenever needed.
        /// </summary>
        /// <param name="forceLogin">
        /// Whether the login request should be forced even if the current session has not expired.
        /// </param>
        public async Task<SLLoginResponse> LoginAsync(bool forceLogin = false) => await ExecuteLoginAsync(forceLogin, true);

        /// <summary>
        /// Internal login method where a return is not needed.
        /// </summary>
        private async Task LoginInternalAsync(bool forceLogin = false) => await ExecuteLoginAsync(forceLogin, false);

        /// <summary>
        /// Performs the POST Login request to the Service Layer.
        /// </summary>
        /// <param name="forceLogin">
        /// Whether the login request should be forced even if the current session has not expired.
        /// </param>
        /// <param name="expectReturn">
        /// Wheter the login information should be returned.
        /// </param>
        private async Task<SLLoginResponse> ExecuteLoginAsync(bool forceLogin = false, bool expectReturn = false)
        {
            // Prevents multiple login requests in a multi-threaded scenario
            await _semaphoreSlim.WaitAsync();

            try
            {
                if (forceLogin)
                    _lastRequest = default;

                // Session still valid, no need to login again
                if (DateTime.Now.Subtract(_lastRequest).TotalMinutes < _loginResponse.SessionTimeout)
                    return expectReturn ? LoginResponse : null;

                var loginResponse = await ServiceLayerRoot
                    .AppendPathSegment("Login")
                    .WithCookies(out var cookieJar)
                    .PostJsonAsync(new { CompanyDB, UserName, Password, Language })
                    .ReceiveJson<SLLoginResponse>();

                Cookies = cookieJar;
                _loginResponse = loginResponse;
                _loginResponse.LastLogin = DateTime.Now;

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
                catch { throw ex; }
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Performs a POST Logout request, ending the current session.
        /// </summary>
        public async Task LogoutAsync()
        {
            if (Cookies == null) return;

            try
            {
                await ServiceLayerRoot.AppendPathSegment("Logout").WithCookies(Cookies).PostAsync();
                _loginResponse = new SLLoginResponse();
                _lastRequest = default;
                Cookies = null;
            }
            catch (FlurlHttpException ex)
            {
                try
                {
                    if (ex.Call.HttpResponseMessage == null) throw;
                    var response = await ex.GetResponseJsonAsync<SLResponseError>();
                    throw new SLException(response.Error.Message.Value, response.Error, ex);
                }
                catch { throw ex; }
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
            new SLRequest(this, new FlurlRequest(ServiceLayerRoot.AppendPathSegment(resource)));

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
            new SLRequest(this, new FlurlRequest(ServiceLayerRoot.AppendPathSegment(id is string ? $"{resource}('{id}')" : $"{resource}({id})")));

        /// <summary>
        /// Calls the Login method to ensure a valid session and then executes the provided request.
        /// If the request is unsuccessfull with any return code present in <see cref="_returnCodesToRetry"/>, 
        /// it will be retried for <see cref="NumberOfAttempts"/> number of times.
        /// </summary>
        internal async Task<T> ExecuteRequest<T>(Func<Task<T>> action)
        {
            _lastRequest = DateTime.Now;

            if (NumberOfAttempts < 1)
                throw new ArgumentException("The number of attempts can not be lower than 1.");

            List<Exception> exceptions = null;
            await LoginInternalAsync();

            for (int i = 0; i < NumberOfAttempts; i++)
            {
                try
                {
                    var result = await action();
                    return result;
                }
                catch (FlurlHttpException ex)
                {
                    if (exceptions == null) exceptions = new List<Exception>();

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
                    if (!_returnCodesToRetry.Any(x => x == ex.Call.HttpResponseMessage?.StatusCode))
                    {
                        break;
                    }

                    // Forces a new login request in case the response is 401 Unauthorized
                    if (ex.Call.HttpResponseMessage?.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        await LoginInternalAsync(true);
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
                var response = await ServiceLayerRoot
                    .RemovePath()
                    .AppendPathSegment(path)
                    .GetAsync();

                var pingResponse = await response.GetJsonAsync<SLPingResponse>();
                pingResponse.IsSuccessStatusCode = response.ResponseMessage.IsSuccessStatusCode;
                pingResponse.StatusCode = response.ResponseMessage.StatusCode;
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
                catch { throw ex; }
            }
            catch (Exception) { throw; }
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
        public void BeforeCall(Func<FlurlCall, Task> action) =>
            FlurlHttp.ConfigureClient(ServiceLayerRoot.RemovePath(), client => client.BeforeCall(action));

        /// <summary>
        /// Sets a <see cref="Action{T}"/> delegate that is called before every Service Layer request.
        /// </summary>
        /// <remarks>
        /// The <see cref="FlurlCall"/> object provides various details about the call than can be used for logging and error handling.
        /// Response-related properties will be null in BeforeCall.
        /// </remarks>
        public void BeforeCall(Action<FlurlCall> action) =>
            FlurlHttp.ConfigureClient(ServiceLayerRoot.RemovePath(), client => client.BeforeCall(action));

        /// <summary>
        /// Sets a <see cref="Func{T, TResult}"/> delegate that is called after every Service Layer request.
        /// </summary>
        /// <remarks>
        /// The <see cref="FlurlCall"/> object provides various details about the call than can be used for logging and error handling.
        /// </remarks>
        public void AfterCall(Func<FlurlCall, Task> action) =>
            FlurlHttp.ConfigureClient(ServiceLayerRoot.RemovePath(), client => client.AfterCall(action));

        /// <summary>
        /// Sets a <see cref="Action{T}"/> delegate that is called after every Service Layer request.
        /// </summary>
        /// <remarks>
        /// The <see cref="FlurlCall"/> object provides various details about the call than can be used for logging and error handling.
        /// </remarks>
        public void AfterCall(Action<FlurlCall> action) =>
            FlurlHttp.ConfigureClient(ServiceLayerRoot.RemovePath(), client => client.AfterCall(action));

        /// <summary>
        /// Sets a <see cref="Func{T, TResult}"/> delegate that is called after every unsuccessful Service Layer request.
        /// </summary>
        /// <remarks>
        /// The <see cref="FlurlCall"/> object provides various details about the call than can be used for logging and error handling.
        /// </remarks>
        public void OnError(Func<FlurlCall, Task> action) =>
            FlurlHttp.ConfigureClient(ServiceLayerRoot.RemovePath(), client => client.OnError(action));

        /// <summary>
        /// Sets a <see cref="Action{T}"/> delegate that is called after every unsuccessful Service Layer request.
        /// </summary>
        /// <remarks>
        /// The <see cref="FlurlCall"/> object provides various details about the call than can be used for logging and error handling.
        /// </remarks>
        public void OnError(Action<FlurlCall> action) =>
            FlurlHttp.ConfigureClient(ServiceLayerRoot.RemovePath(), client => client.OnError(action));
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

                var result = await ServiceLayerRoot
                    .AppendPathSegment("Attachments2")
                    .WithCookies(Cookies)
                    .PostMultipartAsync(mp =>
                    {
                        // Removes double quotes from boundary, otherwise the request fails with error 405 Method Not Allowed
                        var boundary = mp.Headers.ContentType.Parameters.First(o => o.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase));
                        boundary.Value = boundary.Value.Replace("\"", string.Empty);

                        foreach (var file in files)
                        {
                            var content = new StreamContent(file.Value);
                            content.Headers.Add("Content-Disposition", $"form-data; name=\"files\"; filename=\"{file.Key}\"");
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
            await PatchAttachmentsAsync(attachmentEntry, new Dictionary<string, Stream>() { { fileName, new MemoryStream(file) } });

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
            await PatchAttachmentsAsync(attachmentEntry, files.ToDictionary(x => x.Key, x => (Stream)new MemoryStream(x.Value)));

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

                var result = await ServiceLayerRoot
                    .AppendPathSegment($"Attachments2({attachmentEntry})")
                    .WithCookies(Cookies)
                    .PatchMultipartAsync(mp =>
                    {
                        // Removes double quotes from boundary, otherwise the request fails with error 405 Method Not Allowed
                        var boundary = mp.Headers.ContentType.Parameters.First(o => o.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase));
                        boundary.Value = boundary.Value.Replace("\"", string.Empty);

                        foreach (var file in files)
                        {
                            var content = new StreamContent(file.Value);
                            content.Headers.Add("Content-Disposition", $"form-data; name=\"files\"; filename=\"{file.Key}\"");
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
                var file = await ServiceLayerRoot
                    .AppendPathSegment($"Attachments2({attachmentEntry})/$value")
                    .SetQueryParam("filename", !string.IsNullOrEmpty(fileName) ? $"'{fileName}'" : null)
                    .WithCookies(Cookies)
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
            // Adds required "msgtype" parameter to the HttpContent in order for the ReadAsHttpResponseMessageAsync method to work as expected
            void AddMsgTypeToHttpContent(HttpContent httpContent)
            {
                if (httpContent.Headers.ContentType.MediaType.Equals("application/http", StringComparison.OrdinalIgnoreCase)
                    && !httpContent.Headers.ContentType.Parameters.Any(p => p.Name.Equals("msgtype", StringComparison.OrdinalIgnoreCase)
                    && p.Value.Equals("response", StringComparison.OrdinalIgnoreCase)))
                {
                    httpContent.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("msgtype", "response"));
                }
            }

            return await ExecuteRequest(async () =>
            {
                if (requests == null || requests.Count() == 0)
                {
                    throw new ArgumentException("No requests to be sent.");
                }

                var response = singleChangeSet
                    ? await ServiceLayerRoot
                        .AppendPathSegment("$batch")
                        .WithCookies(Cookies)
                        .PostMultipartAsync(mp =>
                        {
                            mp.Headers.ContentType.MediaType = "multipart/mixed";
                            mp.Add(BuildMixedMultipartContent(requests));
                        })
                    : await ServiceLayerRoot
                        .AppendPathSegment("$batch")
                        .WithCookies(Cookies)
                        .PostMultipartAsync(mp =>
                        {
                            mp.Headers.ContentType.MediaType = "multipart/mixed";
                            foreach (var request in requests)
                            {
                                string boundary = "changeset_" + Guid.NewGuid();
                                mp.Add(BuildMixedMultipartContent(request, boundary));
                            }
                        });

                var responseList = new List<HttpResponseMessage>();
                var multipart = await response.ResponseMessage.Content.ReadAsMultipartAsync();

                foreach (HttpContent httpContent in multipart.Contents)
                {
                    if (httpContent.Headers.ContentType.MediaType.Equals("application/http", StringComparison.OrdinalIgnoreCase))
                    {
                        AddMsgTypeToHttpContent(httpContent);
                        var innerResponse = await httpContent.ReadAsHttpResponseMessageAsync();
                        responseList.Add(innerResponse);
                    }
                    else if (httpContent.Headers.ContentType.MediaType.Equals("multipart/mixed", StringComparison.OrdinalIgnoreCase))
                    {
                        var innerMultipart = await httpContent.ReadAsMultipartAsync();

                        foreach (HttpContent innerHttpContent in innerMultipart.Contents)
                        {
                            AddMsgTypeToHttpContent(innerHttpContent);
                            var innerResponse = await innerHttpContent.ReadAsHttpResponseMessageAsync();
                            responseList.Add(innerResponse);
                        }
                    }
                }

                return responseList.ToArray();
            });
        }

        /// <summary>
        /// Builds the multipart content for a batch request using a single change set.
        /// </summary>
        private MultipartContent BuildMixedMultipartContent(IEnumerable<SLBatchRequest> requests)
        {
            string boundary = "changeset_" + Guid.NewGuid();
            var multipartContent = new MultipartContent("mixed", boundary);

            foreach (var batchRequest in requests)
            {
                var request = new HttpRequestMessage(batchRequest.HttpMethod, Url.Combine(ServiceLayerRoot.ToString(), batchRequest.Resource));

                if (batchRequest.Data != null)
                    request.Content = new StringContent(JsonConvert.SerializeObject(batchRequest.Data, batchRequest.JsonSerializerSettings), batchRequest.Encoding, "application/json");

                var innerContent = new HttpMessageContent(request);
                innerContent.Headers.Add("content-transfer-encoding", "binary");

                if (batchRequest.ContentID.HasValue)
                    innerContent.Headers.Add("Content-ID", batchRequest.ContentID.ToString());

                multipartContent.Add(innerContent);
            }

            return multipartContent;
        }

        /// <summary>
        /// Builds the multipart content for a batch request using multiple change sets.
        /// </summary>
        private MultipartContent BuildMixedMultipartContent(SLBatchRequest batchRequest, string boundary)
        {
            var multipartContent = new MultipartContent("mixed", boundary);
            var request = new HttpRequestMessage(batchRequest.HttpMethod, Url.Combine(ServiceLayerRoot.ToString(), batchRequest.Resource));

            if (batchRequest.Data != null)
                request.Content = new StringContent(JsonConvert.SerializeObject(batchRequest.Data, batchRequest.JsonSerializerSettings), batchRequest.Encoding, "application/json");

            var innerContent = new HttpMessageContent(request);
            innerContent.Headers.Add("content-transfer-encoding", "binary");

            if (batchRequest.ContentID.HasValue)
                innerContent.Headers.Add("Content-ID", batchRequest.ContentID.ToString());

            multipartContent.Add(innerContent);

            return multipartContent;
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

        /// <summary>
        /// Custom HttpClientFactory implementation for Service Layer.
        /// </summary>
        private class CustomHttpClientFactory : DefaultHttpClientFactory
        {
            public override HttpMessageHandler CreateMessageHandler()
            {
                var handler = (HttpClientHandler)base.CreateMessageHandler();
                handler.ServerCertificateCustomValidationCallback = (a, b, c, d) => true;
                return handler;
            }

            public override HttpClient CreateHttpClient(HttpMessageHandler handler)
            {
                var httpClient = base.CreateHttpClient(handler);
                httpClient.DefaultRequestHeaders.ExpectContinue = false;
                return httpClient;
            }
        }
        #endregion
    }
}