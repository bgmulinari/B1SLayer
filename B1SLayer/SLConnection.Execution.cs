using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer;

public partial class SLConnection
{
    /// <summary>
    ///     The maximum number of redirects followed for a single request before
    ///     the last redirect response is handed back as the final response.
    /// </summary>
    private const int MaxRedirects = 10;

    private readonly List<Func<SLCallContext, Task>> _afterCallHandlers = [];
    private readonly List<Func<SLCallContext, Task>> _beforeCallHandlers = [];
    private readonly List<Func<SLCallContext, Task>> _onErrorHandlers = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="SLRequest" /> class that represents a request to the associated <see cref="SLConnection" />.
    /// </summary>
    /// <remarks>
    ///     The request can be configured through the fluent methods of <see cref="SLRequest" />.
    /// </remarks>
    /// <param name="resource">
    ///     The resource name to be requested.
    /// </param>
    public SLRequest Request(string resource) => new(this, resource);

    /// <summary>
    ///     Initializes a new instance of the <see cref="SLRequest" /> class that represents a request to the associated <see cref="SLConnection" />.
    /// </summary>
    /// <remarks>
    ///     The request can be configured through the fluent methods of <see cref="SLRequest" />.
    /// </remarks>
    /// <param name="resource">
    ///     The resource name to be requested.
    /// </param>
    /// <param name="id">
    ///     The entity ID to be requested.
    /// </param>
    public SLRequest Request(string resource, object id) =>
        // Numeric ids must render culture-invariantly (e.g. "1.5", never "1,5") for the URL to be valid
        new(this,
            id is string
                ? $"{resource}('{id}')"
                : $"{resource}({Convert.ToString(id, CultureInfo.InvariantCulture)})");

    /// <summary>
    ///     Ensures a valid session and then executes the request built by the provided factory,
    ///     handing the successful response to the provided handler.
    ///     If the request is unsuccessfull with any return code present in <see cref="HttpStatusCodesToRetry" />,
    ///     it will be retried for <see cref="NumberOfAttempts" /> number of times, waiting <see cref="RetryDelay" /> between attempts.
    /// </summary>
    /// <remarks>
    ///     The factory is invoked once per attempt, as an <see cref="HttpRequestMessage" /> and its content can not be reused.
    /// </remarks>
    internal async Task<T> ExecuteRequestAsync<T>(
        Func<SLCookieJar, CancellationToken, Task<HttpRequestMessage>> requestFactory,
        Func<HttpResponseMessage, Task<T>> responseHandler,
        Func<HttpStatusCode, bool> isAllowedStatus = null,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        TimeSpan? requestTimeout = null,
        byte[] capturedRequestBody = null,
        CancellationToken cancellationToken = default)
    {
        var loginReattempted = false;
        List<Exception> exceptions = null;

        if (NumberOfAttempts < 1)
        {
            throw new ArgumentException("The number of attempts can not be lower than 1.");
        }

        for (var i = 0; i < NumberOfAttempts || loginReattempted; i++)
        {
            loginReattempted = false;

            SLCookieJar sessionCookies;

            try
            {
                sessionCookies = await GetSessionCookiesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SLException ex) when (IsTransientLoginFailure(ex))
            {
                // A transient infrastructure failure on the automatic login (e.g. a 502 from a load
                // balancer) participates in the retry policy; authentication failures propagate immediately
                exceptions ??= [];
                exceptions.Add(ex);
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            using var requestMessage = await requestFactory(sessionCookies, cancellationToken).ConfigureAwait(false);

            SLCallContext callContext;

            try
            {
                callContext = await SendCoreAsync(requestMessage, sessionCookies, isAllowedStatus, completionOption, requestTimeout, capturedRequestBody, i + 1, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is SLTimeoutException or HttpRequestException)
            {
                // No response was received, so a retry can't help
                exceptions ??= [];
                exceptions.Add(ex);
                break;
            }
            catch (SLException ex)
            {
                exceptions ??= [];
                exceptions.Add(ex);

                // Whether the request should be retried
                if (ex.StatusCode is not { } statusCode || !HttpStatusCodesToRetry.Contains(statusCode))
                {
                    break;
                }

                // Forces a new login request in case the response is 401 Unauthorized
                if (statusCode == HttpStatusCode.Unauthorized)
                {
                    if (i >= NumberOfAttempts)
                    {
                        break;
                    }

                    try
                    {
                        await ExecuteLoginAsync(skipIfSessionRefreshed: true,
                                staleSessionValue: GetSerializedSessionValue(sessionCookies),
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                        loginReattempted = true;
                    }
                    catch (SLException loginException) when (IsTransientLoginFailure(loginException))
                    {
                        // A transient failure of the forced re-login participates in the retry policy
                        // like the request itself: the next attempt triggers the login again, and the
                        // collected exception context is preserved instead of being discarded
                        exceptions.Add(loginException);
                    }
                }

                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var response = callContext.ResponseMessage;

            try
            {
                return await responseHandler(response).ConfigureAwait(false);
            }
            finally
            {
                // On the headers-read path the content is still being streamed to the caller and can't be disposed here
                if (completionOption == HttpCompletionOption.ResponseContentRead)
                {
                    response.Dispose();
                }
            }
        }

        var uniqueExceptions = exceptions.Distinct(new ExceptionEqualityComparer());

        if (uniqueExceptions.Count() == 1)
        {
            throw uniqueExceptions.First();
        }

        throw new AggregateException("Could not process request", uniqueExceptions);
    }

    /// <summary>
    ///     Determines whether an automatic login failure is a transient infrastructure failure (e.g. a 502
    ///     from a load balancer) that should participate in the retry policy. Authentication failures
    ///     and timeouts are excluded, propagating immediately.
    /// </summary>
    private bool IsTransientLoginFailure(SLException exception) =>
        exception is not SLTimeoutException
        && exception.StatusCode is { } statusCode
        && statusCode != HttpStatusCode.Unauthorized
        && HttpStatusCodesToRetry.Contains(statusCode);

    /// <summary>
    ///     Sends the given request message, invoking the registered call event handlers and enforcing the request timeout.
    /// </summary>
    /// <remarks>
    ///     A response with an unsuccessful status code (unless explicitly allowed) results in a
    ///     <see cref="SLException" /> carrying the error details parsed from the response body.
    ///     Timeouts throw <see cref="SLTimeoutException" />, caller cancellations throw
    ///     <see cref="OperationCanceledException" /> and transport failures surface as <see cref="HttpRequestException" />.
    /// </remarks>
    private async Task<SLCallContext> SendCoreAsync(
        HttpRequestMessage requestMessage,
        SLCookieJar cookieJar,
        Func<HttpStatusCode, bool> isAllowedStatus,
        HttpCompletionOption completionOption,
        TimeSpan? requestTimeout,
        byte[] capturedRequestBody,
        int attemptNumber,
        CancellationToken cancellationToken)
    {
        var callContext = new SLCallContext
        {
            RequestMessage = requestMessage,
            RequestBodyBytes = capturedRequestBody,
            AttemptNumber = attemptNumber
        };

        // BeforeCall handler exceptions deliberately propagate to the caller
        await RaiseEventsAsync(_beforeCallHandlers, callContext).ConfigureAwait(false);

        var effectiveTimeout = requestTimeout ?? DefaultRequestTimeout;
        CancellationTokenSource timeoutTokenSource = null;
        var sendToken = cancellationToken;

        if (effectiveTimeout != Timeout.InfiniteTimeSpan)
        {
            timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutTokenSource.CancelAfter(effectiveTimeout);
            sendToken = timeoutTokenSource.Token;
        }

        callContext.StartedUtc = DateTime.UtcNow;
        var raisingOnError = false;

        try
        {
            callContext.ResponseMessage = await SendWithRedirectsAsync(requestMessage, cookieJar, callContext, completionOption, sendToken).ConfigureAwait(false);
            callContext.Succeeded = callContext.ResponseMessage.IsSuccessStatusCode
                                    || (isAllowedStatus?.Invoke(callContext.ResponseMessage.StatusCode) ?? false);

            if (callContext.Succeeded)
            {
                return callContext;
            }

            // The body read is bounded by the same linked timeout token as the send, as on the
            // headers-read path the body may still be streaming in from the wire at this point
            var responseContent = await callContext.ResponseMessage.ReadStringAsync(sendToken).ConfigureAwait(false);
            callContext.Exception = CreateExceptionFromResponse(callContext.ResponseMessage.StatusCode, responseContent);

            raisingOnError = true;
            await RaiseEventsAsync(_onErrorHandlers, callContext).ConfigureAwait(false);
            raisingOnError = false;

            throw callContext.Exception;
        }
        catch (Exception ex) when (!raisingOnError && !ReferenceEquals(ex, callContext.Exception))
        {
            callContext.Exception = ex;
            await RaiseEventsAsync(_onErrorHandlers, callContext).ConfigureAwait(false);

            // The cancellation is only classified after the OnError handlers ran, as they may have requested it themselves
            if (ex is OperationCanceledException cancelException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    callContext.Exception = new OperationCanceledException(cancelException.Message, cancelException, cancellationToken);
                }
                else if (timeoutTokenSource != null)
                {
                    // The caller's token wasn't cancelled, so the cancellation is attributed to the request timeout
                    callContext.Exception = new SLTimeoutException($"The request timed out: {requestMessage.Method} {requestMessage.RequestUri}", cancelException);
                }
                else
                {
                    // With no timeout configured this cancellation originated elsewhere (e.g. HttpClient disposal)
                    throw;
                }

                throw callContext.Exception;
            }

            throw;
        }
        finally
        {
            timeoutTokenSource?.Dispose();
            callContext.EndedUtc = DateTime.UtcNow;

            try
            {
                await RaiseEventsAsync(_afterCallHandlers, callContext).ConfigureAwait(false);
            }
            finally
            {
                // Failed responses are owned by this method and disposed once the handlers had their
                // chance to inspect them, even when an AfterCall handler throws
                if (!callContext.Succeeded)
                {
                    callContext.ResponseMessage?.Dispose();
                }
            }
        }
    }

    /// <summary>
    ///     Sends the request, following redirects itself — the handler has redirects disabled — so
    ///     Set-Cookie headers on every hop are captured and the applicable cookies re-evaluated per hop URL.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRedirectsAsync(HttpRequestMessage requestMessage, SLCookieJar cookieJar,
        SLCallContext callContext, HttpCompletionOption completionOption, CancellationToken sendToken)
    {
        var currentRequest = requestMessage;
        SLCookieJar chainCookies = null;
        var redirectCount = 0;

        try
        {
            while (true)
            {
                var response = await HttpClient.SendAsync(currentRequest, completionOption, sendToken).ConfigureAwait(false);

                // Cookies set by any response in the chain are merged into a per-call jar seeded from the
                // request's own cookies, so intermediate hops (e.g. a load balancer issuing its affinity
                // cookie on a redirect) are not lost and later hops send the updated cookie set
                if (response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
                {
                    chainCookies ??= cookieJar?.Clone() ?? new SLCookieJar();
                    chainCookies.MergeSetCookieHeaders(setCookieHeaders, currentRequest.RequestUri);
                    callContext.ResponseCookies = chainCookies;
                }

                var redirectRequest = redirectCount < MaxRedirects
                    ? BuildRedirectRequestMessage(currentRequest, response, chainCookies ?? cookieJar)
                    : null;

                if (redirectRequest == null)
                {
                    return response;
                }

                redirectCount++;
                response.Dispose();

                if (!ReferenceEquals(currentRequest, requestMessage))
                {
                    // Reused content is owned by the original request message, which the caller disposes
                    currentRequest.Content = null;
                    currentRequest.Dispose();
                }

                currentRequest = redirectRequest;
            }
        }
        finally
        {
            if (!ReferenceEquals(currentRequest, requestMessage))
            {
                currentRequest.Content = null;
                currentRequest.Dispose();
            }
        }
    }

    /// <summary>
    ///     Builds the request message for a redirect hop, or null when the response is not a followable
    ///     redirect. The verb-conversion rules match HttpClient and browsers: 303 always converts to GET,
    ///     301/302 convert only POST, 307/308 preserve the method and body.
    /// </summary>
    private static HttpRequestMessage BuildRedirectRequestMessage(HttpRequestMessage previousRequest, HttpResponseMessage redirectResponse, SLCookieJar cookieJar)
    {
        var statusCode = (int)redirectResponse.StatusCode;

        if (statusCode is not (301 or 302 or 303 or 307 or 308) || redirectResponse.Headers.Location is not { } location)
        {
            return null;
        }

        var targetUri = location.IsAbsoluteUri ? location : new Uri(previousRequest.RequestUri, location);

        // A downgrade from https to http would leak the session cookies over an insecure channel
        if (string.Equals(previousRequest.RequestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(targetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var changeToGet = statusCode == 303 || statusCode is 301 or 302 && previousRequest.Method == HttpMethod.Post;

        var redirectRequest = new HttpRequestMessage(changeToGet ? HttpMethod.Get : previousRequest.Method, targetUri)
        {
            Version = previousRequest.Version
        };

        foreach (var header in previousRequest.Headers)
        {
            // The Cookie header is never blindly forwarded — the jar re-decides which cookies are valid
            // for the target URL; Authorization is not forwarded either, and Transfer-Encoding
            // doesn't survive a verb change
            if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || changeToGet && header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            redirectRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (!changeToGet)
        {
            // B1SLayer request bodies are always buffered (byte arrays or seekable memory streams),
            // so the content instance can be re-sent; ownership stays with the original message
            redirectRequest.Content = previousRequest.Content;
        }

        AttachSessionCookies(redirectRequest, cookieJar);
        return redirectRequest;
    }

    /// <summary>
    ///     Creates an <see cref="HttpRequestMessage" /> for the given resource under the Service Layer root.
    /// </summary>
    private HttpRequestMessage CreateResourceRequestMessage(HttpMethod method, string resource, SLCookieJar sessionCookies = null, HttpContent content = null)
    {
        var requestMessage = new HttpRequestMessage(method, SLUrl.AppendPathSegment(ServiceLayerRoot.ToString(), resource)) { Content = content };
        AttachSessionCookies(requestMessage, sessionCookies);
        return requestMessage;
    }

    /// <summary>
    ///     Attaches the given session cookies to the request message as a single Cookie header,
    ///     the only shape in which the manually managed cookies reach the wire.
    /// </summary>
    internal static void AttachSessionCookies(HttpRequestMessage requestMessage, SLCookieJar sessionCookies)
    {
        var cookieHeader = sessionCookies?.GetCookieHeaderFor(requestMessage.RequestUri);

        if (cookieHeader == null)
        {
            return;
        }

        // Multiple Cookie header fields are not allowed, so an already-set Cookie header (e.g. a
        // user-defined one) is merged with the session cookies, with the existing cookies taking precedence
        if (requestMessage.Headers.TryGetValues("Cookie", out var existingCookieValues))
        {
            requestMessage.Headers.Remove("Cookie");
            cookieHeader = string.Join("; ", existingCookieValues) + "; " + cookieHeader;
        }

        requestMessage.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
    }

    /// <summary>
    ///     Creates the <see cref="SLException" /> for an unsuccessful response, parsing the
    ///     Service Layer error details from the response body whenever possible.
    /// </summary>
    private static SLException CreateExceptionFromResponse(HttpStatusCode statusCode, string responseContent)
    {
        if (!string.IsNullOrWhiteSpace(responseContent))
        {
            try
            {
                var responseError = JsonSerializer.Deserialize<SLResponseError>(responseContent);

                if (responseError?.Error != null)
                {
                    return new SLException(responseError.Error.Message.Value, responseError.Error, statusCode, responseContent);
                }
            }
            catch (Exception)
            {
                // Not a parseable Service Layer error; fall through to the generic exception
            }
        }

        return new SLException($"The request failed with status code {(int)statusCode} ({statusCode}).", null, statusCode, responseContent);
    }

    /// <summary>
    ///     Provides a direct response from the Apache server that can be used for network testing and component monitoring.
    ///     In response to a PING request, the Apache server (load balancer or node) will respond directly with a simple PONG response.
    /// </summary>
    /// <remarks>
    ///     This feature is only available on version 9.3 PL10 or above. See SAP Note <see href="https://launchpad.support.sap.com/#/notes/2796799">2796799</see> for more details.
    /// </remarks>
    /// <returns>
    ///     A <see cref="SLPingResponse" /> object containing the response details.
    /// </returns>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public async Task<SLPingResponse> PingAsync(CancellationToken cancellationToken = default) => await ExecutePingAsync("ping/", cancellationToken).ConfigureAwait(false);

    /// <summary>
    ///     Provides a direct response from the Apache server that can be used for network testing and component monitoring.
    ///     In response to a PING request, the Apache server (load balancer or node) will respond directly with a simple PONG response.
    /// </summary>
    /// <remarks>
    ///     This feature is only available on version 9.3 PL10 or above. See SAP Note <see href="https://launchpad.support.sap.com/#/notes/2796799">2796799</see> for more details.
    /// </remarks>
    /// <param name="node">
    ///     The specific node to be monitored. If not specified, the request will be directed to the load balancer.
    /// </param>
    /// <returns>
    ///     A <see cref="SLPingResponse" /> object containing the response details.
    /// </returns>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public async Task<SLPingResponse> PingNodeAsync(int? node = null, CancellationToken cancellationToken = default) => await ExecutePingAsync(node.HasValue ? $"ping/node/{node}" : "ping/load-balancer", cancellationToken).ConfigureAwait(false);

    /// <summary>
    ///     Performs the ping request with the provided path segment.
    /// </summary>
    /// <remarks>
    ///     Any status code is allowed, as the ping endpoints report failures through a parseable response
    ///     body rather than exceptions — accordingly, ping failures don't invoke the OnError handlers.
    /// </remarks>
    private async Task<SLPingResponse> ExecutePingAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The ping endpoints live at the server root, outside the versioned Service Layer path
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri(ServiceLayerRoot, "/" + path));

        var callContext = await SendCoreAsync(requestMessage, null, _ => true, HttpCompletionOption.ResponseContentRead, null, null, 1, cancellationToken).ConfigureAwait(false);
        using var response = callContext.ResponseMessage;
        var responseContent = await response.ReadStringAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var pingResponse = JsonSerializer.Deserialize<SLPingResponse>(responseContent, ProtocolSerializerOptions) ?? new SLPingResponse();
            pingResponse.IsSuccessStatusCode = response.IsSuccessStatusCode;
            pingResponse.StatusCode = response.StatusCode;
            return pingResponse;
        }
        catch (JsonException jsonException)
        {
            throw new SLException($"The ping response could not be parsed. Status code {(int)response.StatusCode} ({response.StatusCode}).",
                null,
                response.StatusCode,
                responseContent,
                jsonException);
        }
    }


    /// <summary>
    ///     Sets a <see cref="Func{T, TResult}" /> delegate that is called before every Service Layer request.
    /// </summary>
    /// <remarks>
    ///     The <see cref="SLCallContext" /> object provides various details about the call than can be used for logging and error handling.
    ///     Response-related properties will be null in BeforeCall.
    /// </remarks>
    public SLConnection BeforeCall(Func<SLCallContext, Task> action)
    {
        _beforeCallHandlers.Add(action);
        return this;
    }

    /// <summary>
    ///     Sets a <see cref="Action{T}" /> delegate that is called before every Service Layer request.
    /// </summary>
    /// <remarks>
    ///     The <see cref="SLCallContext" /> object provides various details about the call than can be used for logging and error handling.
    ///     Response-related properties will be null in BeforeCall.
    /// </remarks>
    public SLConnection BeforeCall(Action<SLCallContext> action)
    {
        _beforeCallHandlers.Add(WrapSyncHandler(action));
        return this;
    }

    /// <summary>
    ///     Sets a <see cref="Func{T, TResult}" /> delegate that is called after every Service Layer request.
    /// </summary>
    /// <remarks>
    ///     The <see cref="SLCallContext" /> object provides various details about the call than can be used for logging and error handling.
    /// </remarks>
    public SLConnection AfterCall(Func<SLCallContext, Task> action)
    {
        _afterCallHandlers.Add(action);
        return this;
    }

    /// <summary>
    ///     Sets a <see cref="Action{T}" /> delegate that is called after every Service Layer request.
    /// </summary>
    /// <remarks>
    ///     The <see cref="SLCallContext" /> object provides various details about the call than can be used for logging and error handling.
    /// </remarks>
    public SLConnection AfterCall(Action<SLCallContext> action)
    {
        _afterCallHandlers.Add(WrapSyncHandler(action));
        return this;
    }

    /// <summary>
    ///     Sets a <see cref="Func{T, TResult}" /> delegate that is called after every unsuccessful Service Layer request.
    /// </summary>
    /// <remarks>
    ///     The <see cref="SLCallContext" /> object provides various details about the call than can be used for logging and error handling.
    /// </remarks>
    public SLConnection OnError(Func<SLCallContext, Task> action)
    {
        _onErrorHandlers.Add(action);
        return this;
    }

    /// <summary>
    ///     Sets a <see cref="Action{T}" /> delegate that is called after every unsuccessful Service Layer request.
    /// </summary>
    /// <remarks>
    ///     The <see cref="SLCallContext" /> object provides various details about the call than can be used for logging and error handling.
    /// </remarks>
    public SLConnection OnError(Action<SLCallContext> action)
    {
        _onErrorHandlers.Add(WrapSyncHandler(action));
        return this;
    }

    private static Func<SLCallContext, Task> WrapSyncHandler(Action<SLCallContext> action)
    {
        return callContext =>
        {
            action(callContext);
            return Task.CompletedTask;
        };
    }

    private static async Task RaiseEventsAsync(List<Func<SLCallContext, Task>> handlers, SLCallContext callContext)
    {
        foreach (var handler in handlers)
        {
            await handler(callContext).ConfigureAwait(false);
        }
    }


    /// <summary>
    ///     Used to aggregate exceptions that occur on request retries.
    /// </summary>
    /// <remarks>
    ///     In most cases, the same exception will occur multiple times,
    ///     but we don't want to return multiple copies of it. This class is used
    ///     to find exceptions that are duplicates by type and message so we can
    ///     only return one of them.
    /// </remarks>
    private class ExceptionEqualityComparer : IEqualityComparer<Exception>
    {
        public bool Equals(Exception e1, Exception e2)
        {
            if (e2 == null && e1 == null)
            {
                return true;
            }

            if (e1 == null | e2 == null)
            {
                return false;
            }

            if (e1.GetType().Name.Equals(e2.GetType().Name) && e1.Message.Equals(e2.Message))
            {
                return true;
            }

            return false;
        }

        public int GetHashCode(Exception e) => (e.GetType().Name + e.Message).GetHashCode();
    }
}
