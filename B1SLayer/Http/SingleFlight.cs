using System;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer;

/// <summary>
///     Runs an operation at most once at a time, with concurrent callers sharing the outcome of the one in flight.
/// </summary>
/// <remarks>
///     The operation is detached from its callers, and a caller's cancellation only stops that caller from waiting.
///     That is what makes it safe for a token refresh: cancelling one could otherwise lose a rotated refresh
///     token the server has already issued, which invalidates the previous one, or fail unrelated waiting callers.
/// </remarks>
internal sealed class SingleFlight<T> where T : class
{
    private readonly object _gate = new();
    private Task<T> _inFlight;

    /// <param name="operation">
    ///     The operation to run when none is in flight.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to stop waiting for the outcome.
    /// </param>
    /// <param name="getCompleted">
    ///     Gets an outcome that makes running the operation unnecessary, or null when there is none. Called only when no
    ///     operation is in
    ///     flight, so it sees what the previous operation published, provided that operation published it before completing.
    /// </param>
    public Task<T> RunAsync(Func<Task<T>> operation, CancellationToken cancellationToken, Func<T> getCompleted = null) =>
        GetOrStart(operation, getCompleted).WithCancellation(cancellationToken);

    /// <summary>
    ///     Starts the operation unless one is in flight, without waiting for its outcome.
    /// </summary>
    /// <param name="operation">
    ///     The operation to run when none is in flight.
    /// </param>
    /// <param name="getCompleted">
    ///     Gets an outcome that makes running the operation unnecessary, or null when there is none.
    /// </param>
    public void Start(Func<Task<T>> operation, Func<T> getCompleted = null) => _ = GetOrStart(operation, getCompleted);

    private Task<T> GetOrStart(Func<Task<T>> operation, Func<T> getCompleted)
    {
        lock (_gate)
        {
            if (_inFlight != null)
            {
                return _inFlight;
            }

            if (getCompleted?.Invoke() is { } completed)
            {
                return Task.FromResult(completed);
            }

            var task = Task.Run(() => RunAndClearAsync(operation));
            _inFlight = task;

            // A failure nobody waits for, as all callers have cancelled or none was waiting, must still count as observed
            _ = task.ContinueWith(static x => _ = x.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    private async Task<T> RunAndClearAsync(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            // Cleared before the task completes, so a caller that has seen its outcome starts a new operation
            lock (_gate)
            {
                _inFlight = null;
            }
        }
    }
}
