using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

using Xunit.Sdk;

namespace B1SLayer.Test;

/// <summary>
///     A mock <see cref="HttpMessageHandler" /> that serves canned responses and records every request:
///     sticky-last FIFO response queues, URL-pattern routing, exception simulation and fluent call assertions.
///     An instance is injected into the tested <see cref="SLConnection" /> through <see cref="SLConnectionOptions.HttpMessageHandler" />.
/// </summary>
public sealed class MockHttp : HttpMessageHandler
{
    private readonly ConcurrentQueue<RecordedCall> _callLog = new();
    private readonly MockHttpSetup _defaultSetup = new(null);
    private readonly List<MockHttpSetup> _filteredSetups = [];

    /// <summary>
    ///     Every request received by this handler, in order of arrival.
    /// </summary>
    public IReadOnlyList<RecordedCall> CallLog => _callLog.ToList();

    /// <summary>
    ///     Creates a response setup that only applies to requests whose URL matches any of the given
    ///     wildcard patterns (further narrowable with <see cref="MockHttpSetup.WithVerb" /> and
    ///     <see cref="MockHttpSetup.With" />). Filtered setups are checked in registration order before the default setup.
    /// </summary>
    public MockHttpSetup ForCallsTo(params string[] urlPatterns)
    {
        var setup = new MockHttpSetup(urlPatterns);
        _filteredSetups.Add(setup);
        return setup;
    }

    /// <summary>
    ///     Enqueues a response on the default setup.
    /// </summary>
    public MockHttpSetup RespondWith(string body = "", int status = 200, IDictionary<string, string> headers = null, object cookies = null) => _defaultSetup.RespondWith(body, status, headers, cookies);

    /// <summary>
    ///     Enqueues a JSON response on the default setup.
    /// </summary>
    public MockHttpSetup RespondWithJson(object body, int status = 200, IDictionary<string, string> headers = null, object cookies = null) => _defaultSetup.RespondWithJson(body, status, headers, cookies);

    /// <summary>
    ///     Enqueues an exception to be thrown from the send on the default setup.
    /// </summary>
    public MockHttpSetup SimulateException(Exception exception) => _defaultSetup.SimulateException(exception);

    /// <summary>
    ///     Starts an assertion chain over the recorded calls whose URL matches the given wildcard pattern.
    /// </summary>
    public CallAssertion ShouldHaveCalled(string urlPattern) => new(CallLog, urlPattern);

    /// <summary>
    ///     Asserts that no recorded call matches the given wildcard URL pattern.
    /// </summary>
    public void ShouldNotHaveCalled(string urlPattern)
    {
        var matchingCalls = CallLog.Where(x => Wildcard.MatchesUrlPattern(x.Uri, urlPattern)).ToList();

        if (matchingCalls.Count > 0)
        {
            throw new XunitException($"Expected no calls with URL matching '{urlPattern}', but found {matchingCalls.Count}."
                                     + CallAssertion.DescribeCalls("Matching calls", matchingCalls, false));
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The call is recorded on send entry (with the body captured eagerly) so tests can
        // observe in-flight requests and assert on content after the request completes
        var call = await RecordedCall.CaptureAsync(request);
        _callLog.Enqueue(call);

        var setup = _filteredSetups.Find(x => x.IsMatch(call)) ?? _defaultSetup;

        // Flagged so assertion-failure dumps expose requests that matched no setup —
        // usually a typo'd ForCallsTo pattern silently answered with an empty 200
        call.ServedSyntheticDefault = ReferenceEquals(setup, _defaultSetup) && !_defaultSetup.HasQueuedResponses;

        // The delay honors the passed token, so a delayed response exercises the real
        // per-request timeout and cancellation machinery end to end
        if (setup.ResponseDelay is { } responseDelay)
        {
            await Task.Delay(responseDelay, cancellationToken);
        }

        var response = setup.GetNextResponse();
        response.RequestMessage = request;
        call.Response = response;
        return response;
    }
}

/// <summary>
///     A FIFO queue of canned responses for the requests matching the setup's URL patterns and filters.
///     The last registered response repeats indefinitely, and an empty queue serves 200 OK responses.
/// </summary>
public sealed class MockHttpSetup
{
    private readonly List<Func<RecordedCall, bool>> _filters = [];
    private readonly List<Func<HttpResponseMessage>> _responseFactories = [];
    private int _nextResponseIndex = -1;

    internal MockHttpSetup(string[] urlPatterns)
    {
        UrlPatterns = urlPatterns;
    }

    internal string[] UrlPatterns { get; }

    /// <summary>
    ///     The time each response from this setup takes to arrive, honoring cancellation —
    ///     use with a request timeout to exercise the real timeout path.
    /// </summary>
    internal TimeSpan? ResponseDelay { get; private set; }

    /// <summary>
    ///     Whether any response has been queued on this setup.
    /// </summary>
    internal bool HasQueuedResponses => _responseFactories.Count > 0;

    /// <summary>
    ///     Determines whether this setup serves the given request.
    /// </summary>
    internal bool IsMatch(RecordedCall call)
    {
        return (UrlPatterns == null || UrlPatterns.Any(x => Wildcard.MatchesUrlPattern(call.Uri, x)))
               && _filters.TrueForAll(x => x(call));
    }

    /// <summary>
    ///     Narrows this setup to requests using any of the given HTTP methods.
    /// </summary>
    public MockHttpSetup WithVerb(params HttpMethod[] verbs)
    {
        _filters.Add(call => verbs.Contains(call.Method));
        return this;
    }

    /// <summary>
    ///     Narrows this setup with a custom request predicate.
    /// </summary>
    public MockHttpSetup With(Func<RecordedCall, bool> condition)
    {
        _filters.Add(condition);
        return this;
    }

    /// <summary>
    ///     Delays every response served by this setup, simulating a slow server.
    ///     The delay honors cancellation, so it exercises the real per-request timeout machinery.
    /// </summary>
    public MockHttpSetup WithDelay(TimeSpan delay)
    {
        ResponseDelay = delay;
        return this;
    }

    /// <summary>
    ///     Enqueues a response with the given body, status code, headers and cookies (an anonymous
    ///     object whose properties become Set-Cookie headers).
    /// </summary>
    public MockHttpSetup RespondWith(string body = "", int status = 200, IDictionary<string, string> headers = null, object cookies = null)
    {
        _responseFactories.Add(() => BuildResponse(body, status, headers, cookies, null));
        return this;
    }

    /// <summary>
    ///     Enqueues a JSON response serialized from the given object.
    /// </summary>
    public MockHttpSetup RespondWithJson(object body, int status = 200, IDictionary<string, string> headers = null, object cookies = null)
    {
        var json = JsonSerializer.Serialize(body);
        _responseFactories.Add(() => BuildResponse(json, status, headers, cookies, "application/json"));
        return this;
    }

    /// <summary>
    ///     Enqueues an exception to be thrown from the send, simulating a transport-level failure.
    /// </summary>
    public MockHttpSetup SimulateException(Exception exception)
    {
        _responseFactories.Add(() => throw exception);
        return this;
    }

    /// <summary>
    ///     Enqueues a simulated timeout, shaped like a real HttpClient timeout
    ///     (a <see cref="TaskCanceledException" /> with a <see cref="TimeoutException" /> inside).
    /// </summary>
    public MockHttpSetup SimulateTimeout() => SimulateException(new TaskCanceledException(null, new TimeoutException()));

    internal HttpResponseMessage GetNextResponse()
    {
        if (_responseFactories.Count == 0)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
        }

        // Responses are consumed in order; once exhausted, the last one keeps being served (retry tests rely on this)
        var index = Math.Min(Interlocked.Increment(ref _nextResponseIndex), _responseFactories.Count - 1);
        return _responseFactories[index]();
    }

    private static HttpResponseMessage BuildResponse(string body, int status, IDictionary<string, string> headers, object cookies, string contentType)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = contentType == null
                ? new StringContent(body ?? string.Empty)
                : new StringContent(body ?? string.Empty, Encoding.UTF8, contentType)
        };

        if (headers != null)
        {
            foreach (var header in headers)
            {
                // Content headers (e.g. Content-Type) are rejected by the response headers and must go on the content
                if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    response.Content.Headers.Remove(header.Key);
                    response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        if (cookies != null)
        {
            foreach (var property in cookies.GetType().GetProperties())
            {
                response.Headers.TryAddWithoutValidation("Set-Cookie", $"{property.Name}={property.GetValue(cookies)}");
            }
        }

        return response;
    }
}

/// <summary>
///     Wildcard ('*') pattern matching for URLs and request bodies.
/// </summary>
/// <remarks>
///     Matching is implemented as a linear glob (split on '*', match the literal segments in order)
///     instead of a regex, which suffers catastrophic backtracking on the large multipart body patterns.
/// </remarks>
internal static class Wildcard
{
    public static bool Matches(string text, string pattern)
    {
        if (text == null)
        {
            return false;
        }

        if (!pattern.Contains('*'))
        {
            return text.Equals(pattern, StringComparison.Ordinal);
        }

        var literals = pattern.Split('*');

        if (!text.StartsWith(literals[0], StringComparison.Ordinal))
        {
            return false;
        }

        var position = literals[0].Length;

        for (var i = 1; i < literals.Length - 1; i++)
        {
            var index = text.IndexOf(literals[i], position, StringComparison.Ordinal);

            if (index < 0)
            {
                return false;
            }

            position = index + literals[i].Length;
        }

        var lastLiteral = literals[literals.Length - 1];

        return lastLiteral.Length == 0
               || text.Length - position >= lastLiteral.Length && text.EndsWith(lastLiteral, StringComparison.Ordinal);
    }

    public static bool MatchesUrlPattern(Uri uri, string urlPattern)
    {
        if (urlPattern == null)
        {
            return true;
        }

        var url = uri.AbsoluteUri;

        // The decoded URL is also tested so patterns can be written without percent-encoding
        if (Matches(url, urlPattern) || Matches(Uri.UnescapeDataString(url), urlPattern))
        {
            return true;
        }

        if (urlPattern.EndsWith("*", StringComparison.Ordinal))
        {
            return false;
        }

        // A pattern without a query string should also match URLs that have one
        var queryPattern = urlPattern.Contains('?') ? urlPattern + "&*" : urlPattern + "?*";
        return Matches(url, queryPattern) || Matches(Uri.UnescapeDataString(url), queryPattern);
    }
}
