using System;
using System.Net;

namespace B1SLayer;

/// <summary>
///     Represents a Service Layer exception.
/// </summary>
public class SLException : Exception
{
    internal SLException(string message, SLErrorDetails errorDetails, Exception innerException) : base(message, innerException)
    {
        ErrorDetails = errorDetails;
    }

    internal SLException(string message, SLErrorDetails errorDetails, HttpStatusCode? statusCode, string responseContent, Exception innerException = null)
        : base(message, innerException)
    {
        ErrorDetails = errorDetails;
        StatusCode = statusCode;
        ResponseContent = responseContent;
    }

    /// <summary>
    ///     Gets the error details of a Service Layer exception.
    ///     Null when the response body was not a parseable Service Layer error.
    /// </summary>
    public SLErrorDetails ErrorDetails { get; }

    /// <summary>
    ///     Gets the HTTP status code of the response that caused this exception, or null when no response was received.
    /// </summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    ///     Gets the raw content of the response that caused this exception, or null when no response was received.
    /// </summary>
    public string ResponseContent { get; }
}
