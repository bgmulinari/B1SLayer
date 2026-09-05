using System;
using System.Net.Http;
using System.Text;

namespace B1SLayer;

/// <summary>
///     Provides details about a Service Layer HTTP call, exposed to the delegates
///     registered through <see cref="SLConnection.BeforeCall(Action{SLCallContext})" />,
///     <see cref="SLConnection.AfterCall(Action{SLCallContext})" /> and <see cref="SLConnection.OnError(Action{SLCallContext})" />.
/// </summary>
/// <remarks>
///     The context is only guaranteed to be valid while a call event handler is executing:
///     <see cref="RequestMessage" /> and <see cref="ResponseMessage" /> may be disposed as soon as the handlers
///     return, so handlers must not defer access to them (e.g. from a queued or fire-and-forget task).
///     Handlers should also not consume <see cref="ResponseMessage" /> content on streamed responses
///     (<see cref="SLRequest.GetStreamAsync" />), as the stream is handed to the caller afterwards.
/// </remarks>
public class SLCallContext
{
    /// <summary>
    ///     Gets the HTTP request message of this call.
    /// </summary>
    public HttpRequestMessage RequestMessage { get; internal set; }

    /// <summary>
    ///     Gets the HTTP response message of this call. Null in BeforeCall or when the call failed without a response.
    /// </summary>
    public HttpResponseMessage ResponseMessage { get; internal set; }

    /// <summary>
    ///     Gets the JSON body sent in the request, when applicable.
    ///     Null for requests without a body, binary/multipart requests and the Login request.
    /// </summary>
    public string RequestBody => RequestBodyBytes == null ? null : Encoding.UTF8.GetString(RequestBodyBytes);

    /// <summary>
    ///     Gets the exception that caused the call to fail, or null when the call succeeded.
    ///     For HTTP error responses this is the <see cref="SLException" /> carrying the parsed error details.
    /// </summary>
    public Exception Exception { get; internal set; }

    /// <summary>
    ///     Gets the 1-based number of this attempt within the connection's retry policy.
    ///     Always 1 for calls that are not retried (Login, Logout and Ping).
    /// </summary>
    public int AttemptNumber { get; internal set; }

    /// <summary>
    ///     Gets the UTC timestamp when the request was sent.
    /// </summary>
    public DateTime StartedUtc { get; internal set; }

    /// <summary>
    ///     Gets the UTC timestamp when the call completed, or null if it hasn't completed yet.
    /// </summary>
    public DateTime? EndedUtc { get; internal set; }

    /// <summary>
    ///     Gets the duration of the call, or null if it hasn't completed yet.
    /// </summary>
    public TimeSpan? Duration => EndedUtc - StartedUtc;

    /// <summary>
    ///     Gets whether a response was received, regardless of its status code.
    /// </summary>
    public bool Completed => ResponseMessage != null;

    /// <summary>
    ///     Gets whether the call completed with a successful (2XX) status code
    ///     or a status code explicitly allowed through <see cref="SLRequest.AllowHttpStatus" />.
    /// </summary>
    public bool Succeeded { get; internal set; }

    /// <summary>
    ///     The captured request body bytes backing <see cref="RequestBody" />.
    /// </summary>
    internal byte[] RequestBodyBytes { get; set; }

    /// <summary>
    ///     The cookies accumulated from the Set-Cookie headers of every response in this call's
    ///     redirect chain, or null when no response set any cookie. Seeded from the request's own
    ///     cookie jar so redirect hops send the updated cookie set.
    /// </summary>
    internal SLCookieJar ResponseCookies { get; set; }
}
