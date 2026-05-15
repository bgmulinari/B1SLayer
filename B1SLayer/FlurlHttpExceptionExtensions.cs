using System;
using System.Threading;

using Flurl.Http;

namespace B1SLayer;

internal static class FlurlHttpExceptionExtensions
{
    /// <summary>
    ///     Converts Flurl's wrapped cancellation exception back to the standard top-level
    ///     <see cref="OperationCanceledException" /> when the caller's token requested cancellation.
    /// </summary>
    /// <remarks>
    ///     Flurl wraps send failures in <see cref="FlurlHttpException" />. For caller-requested cancellations,
    ///     B1SLayer should preserve normal .NET cancellation semantics instead of treating the call as an API error.
    ///     Flurl timeout exceptions are intentionally excluded because they represent timeout failures, not caller cancellation.
    /// </remarks>
    internal static void ThrowIfCallerCancellationRequested(this FlurlHttpException exception, CancellationToken cancellationToken)
    {
        if (exception is FlurlHttpTimeoutException)
        {
            return;
        }

        if (!cancellationToken.IsCancellationRequested || !ContainsCancellationException(exception))
        {
            return;
        }

        throw new OperationCanceledException(exception.Message, exception, cancellationToken);
    }

    /// <summary>
    ///     Checks whether Flurl's exception chain contains the cancellation exception from the underlying send operation.
    /// </summary>
    private static bool ContainsCancellationException(Exception exception)
    {
        for (var currentException = exception; currentException != null; currentException = currentException.InnerException)
            if (currentException is OperationCanceledException)
            {
                return true;
            }

        return false;
    }
}