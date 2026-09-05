namespace B1SLayer.Test;

/// <summary>
///     An immutable snapshot of a request received by <see cref="MockHttp" />, taken at send time
///     so it can be safely inspected after the request completes or its content is disposed.
/// </summary>
public sealed class RecordedCall
{
    private RecordedCall()
    {
    }

    public HttpMethod Method { get; private set; }

    public Uri Uri { get; private set; }

    /// <summary>
    ///     The request body captured as a string, or null when the request had no content.
    /// </summary>
    public string RequestBody { get; private set; }

    /// <summary>
    ///     The request and content headers, merged.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; private set; }

    /// <summary>
    ///     The query parameters with percent-decoded names and values.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> QueryParams { get; private set; }

    public HttpResponseMessage Response { get; internal set; }

    /// <summary>
    ///     Whether this call matched no setup and was served the synthetic default 200 — usually
    ///     a sign of a typo in a <see cref="MockHttp.ForCallsTo" /> pattern.
    /// </summary>
    internal bool ServedSyntheticDefault { get; set; }

    public override string ToString()
    {
        var status = Response == null ? "(no response)" : ((int)Response.StatusCode).ToString();
        var unmatched = ServedSyntheticDefault ? " (unmatched: served default empty 200)" : string.Empty;
        return $"{Method} {Uri} → {status}{unmatched}";
    }

    internal static async Task<RecordedCall> CaptureAsync(HttpRequestMessage request)
    {
        var headers = new List<KeyValuePair<string, string>>();

        foreach (var header in request.Headers)
        {
            headers.Add(new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value)));
        }

        string requestBody = null;

        if (request.Content != null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers.Add(new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value)));
            }

            requestBody = await request.Content.ReadAsStringAsync();
        }

        return new RecordedCall
        {
            Method = request.Method,
            Uri = request.RequestUri,
            RequestBody = requestBody,
            Headers = headers,
            QueryParams = ParseQueryParams(request.RequestUri)
        };
    }

    private static List<KeyValuePair<string, string>> ParseQueryParams(Uri uri)
    {
        var queryParams = new List<KeyValuePair<string, string>>();

        if (uri == null || string.IsNullOrEmpty(uri.Query))
        {
            return queryParams;
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');

            queryParams.Add(separatorIndex < 0
                ? new KeyValuePair<string, string>(Decode(pair), null)
                : new KeyValuePair<string, string>(Decode(pair.Substring(0, separatorIndex)), Decode(pair.Substring(separatorIndex + 1))));
        }

        return queryParams;
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace("+", " "));
}
