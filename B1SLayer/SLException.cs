using System;

namespace B1SLayer;

/// <summary>
/// Represents a Service Layer exception.
/// </summary>
public class SLException : Exception
{
    /// <summary>
    /// Gets or sets the error details of a Service Layer exception.
    /// </summary>
    public SLErrorDetails ErrorDetails { get; set; }

    internal SLException(string message, SLErrorDetails errorDetails, Exception innerException) : base(message, innerException)
    {
        ErrorDetails = errorDetails;
    }
}