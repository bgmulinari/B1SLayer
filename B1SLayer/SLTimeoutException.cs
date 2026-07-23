using System;

namespace B1SLayer;

/// <summary>
///     Represents a Service Layer request that failed due to a timeout.
/// </summary>
/// <remarks>
///     The default request timeout is 100 seconds and can be configured through
///     <see cref="SLConnectionOptions.DefaultRequestTimeout" /> or per request with <see cref="SLRequest.WithTimeout(TimeSpan)" />.
/// </remarks>
public class SLTimeoutException : SLException
{
    internal SLTimeoutException(string message, Exception innerException)
        : base(message, null, null, null, innerException)
    {
    }
}
