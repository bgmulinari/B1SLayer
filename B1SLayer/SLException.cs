using System;

namespace B1SLayer
{
    public class SLException : Exception
    {
        public SLErrorDetails ErrorDetails { get; set; }

        public SLException(string message, SLErrorDetails errorDetails, Exception innerException) : base(message, innerException)
        {
            ErrorDetails = errorDetails;
        }
    }
}