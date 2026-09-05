using System.Globalization;
using System.Text;

using Xunit.Sdk;

namespace B1SLayer.Test;

/// <summary>
///     A progressive filter over the calls recorded by <see cref="MockHttp" />: each condition narrows
///     the matching calls and immediately fails the test when none remain.
/// </summary>
public sealed class CallAssertion
{
    private const int MaxDetailedCalls = 5;
    private const int MaxSummaryCalls = 10;

    private readonly IReadOnlyList<RecordedCall> _allCalls;
    private readonly List<string> _conditions = [];
    private List<RecordedCall> _calls;

    internal CallAssertion(IReadOnlyList<RecordedCall> calls, string urlPattern)
    {
        _allCalls = calls;
        _calls = calls.ToList();
        Apply(x => Wildcard.MatchesUrlPattern(x.Uri, urlPattern), $"URL matching '{urlPattern}'");
    }

    public CallAssertion WithVerb(params HttpMethod[] verbs)
    {
        return Apply(x => verbs.Contains(x.Method), $"verb {string.Join("/", verbs.AsEnumerable())}");
    }

    public CallAssertion WithQueryParam(string name, object value = null)
    {
        return Apply(x => HasQueryParam(x, name, value), $"query parameter '{name}'{DescribeValue(value)}");
    }

    public CallAssertion WithoutQueryParam(string name, object value = null)
    {
        return Apply(x => !HasQueryParam(x, name, value), $"no query parameter '{name}'{DescribeValue(value)}");
    }

    public CallAssertion WithHeader(string name, object value = null)
    {
        return Apply(x => HasHeader(x, name, value), $"header '{name}'{DescribeValue(value)}");
    }

    public CallAssertion WithoutHeader(string name, object value = null)
    {
        return Apply(x => !HasHeader(x, name, value), $"no header '{name}'{DescribeValue(value)}");
    }

    public CallAssertion WithRequestBody(string bodyPattern)
    {
        return Apply(x => Wildcard.Matches(x.RequestBody, bodyPattern), "request body matching the expected pattern");
    }

    public CallAssertion With(Func<RecordedCall, bool> predicate, string description = null) => Apply(predicate, description ?? "custom predicate");

    /// <summary>
    ///     Asserts that exactly the given number of recorded calls match all the previous conditions.
    /// </summary>
    public void Times(int expectedCount)
    {
        if (_calls.Count != expectedCount)
        {
            throw new XunitException($"Expected {expectedCount} call(s) with {DescribeConditions()}, but found {_calls.Count}."
                                     + DescribeCalls("Matching calls", _calls, true)
                                     + DescribeCalls("All recorded calls", _allCalls, false));
        }
    }

    private CallAssertion Apply(Func<RecordedCall, bool> predicate, string condition)
    {
        _conditions.Add(condition);
        var previousCalls = _calls;
        _calls = _calls.Where(predicate).ToList();

        if (_calls.Count == 0)
        {
            var message = $"No recorded call found with {DescribeConditions()}.";

            // The calls the failing condition was evaluated against are shown in full detail;
            // when they are the whole log, a single detailed section says it all
            if (previousCalls.Count == _allCalls.Count)
            {
                message += DescribeCalls("All recorded calls", _allCalls, true);
            }
            else
            {
                message += DescribeCalls("Calls matching the previous conditions", previousCalls, true)
                           + DescribeCalls("All recorded calls", _allCalls, false);
            }

            throw new XunitException(message);
        }

        return this;
    }

    /// <summary>
    ///     Renders a bounded, readable dump of the given calls for assertion failure messages —
    ///     with headers and a body preview when detailed.
    /// </summary>
    internal static string DescribeCalls(string title, IReadOnlyList<RecordedCall> calls, bool detailed)
    {
        if (calls.Count == 0)
        {
            return $"\n{title}: none.";
        }

        var limit = detailed ? MaxDetailedCalls : MaxSummaryCalls;
        var builder = new StringBuilder();
        builder.Append('\n').Append(title).Append(" (").Append(calls.Count).Append("):");

        foreach (var call in calls.Take(limit))
        {
            builder.Append("\n  ").Append(call);

            if (!detailed)
            {
                continue;
            }

            foreach (var header in call.Headers)
            {
                builder.Append("\n    ").Append(header.Key).Append(": ").Append(header.Value);
            }

            if (call.RequestBody != null)
            {
                var body = call.RequestBody.Length > 200 ? call.RequestBody.Substring(0, 200) + "…" : call.RequestBody;
                builder.Append("\n    body: ").Append(body.Replace("\r", "\\r").Replace("\n", "\\n"));
            }
        }

        if (calls.Count > limit)
        {
            builder.Append("\n  … and ").Append(calls.Count - limit).Append(" more.");
        }

        return builder.ToString();
    }

    private static bool HasQueryParam(RecordedCall call, string name, object value)
    {
        return call.QueryParams.Any(q =>
            q.Key.Equals(name, StringComparison.Ordinal) && (value == null || MatchesValue(q.Value, value)));
    }

    private static bool HasHeader(RecordedCall call, string name, object value)
    {
        return call.Headers.Any(h =>
            h.Key.Equals(name, StringComparison.OrdinalIgnoreCase) && (value == null || MatchesValue(h.Value, value)));
    }

    private static bool MatchesValue(string actualValue, object expectedValue)
    {
        var expectedString = Convert.ToString(expectedValue, CultureInfo.InvariantCulture) ?? string.Empty;

        return expectedString.Contains('*')
            ? Wildcard.Matches(actualValue, expectedString)
            : string.Equals(actualValue, expectedString, StringComparison.Ordinal);
    }

    private static string DescribeValue(object value) => value == null ? string.Empty : $" = '{Convert.ToString(value, CultureInfo.InvariantCulture)}'";

    private string DescribeConditions() => string.Join(" and ", _conditions);
}
