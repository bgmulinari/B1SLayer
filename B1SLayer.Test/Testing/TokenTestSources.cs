using System.Collections.Concurrent;

namespace B1SLayer.Test;

/// <summary>
///     A token source that serves the given tokens in order (the last one repeating) and records every request it
///     receives.
/// </summary>
public sealed class RecordingTokenSource(params string[] tokens) : ISLAccessTokenSource
{
    private int _calls;

    public ConcurrentQueue<SLAccessTokenRequest> Requests { get; } = new();

    public Task<string> GetAccessTokenAsync(SLAccessTokenRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Enqueue(request);
        var index = Math.Min(Interlocked.Increment(ref _calls) - 1, tokens.Length - 1);
        return Task.FromResult(tokens[index]);
    }
}
