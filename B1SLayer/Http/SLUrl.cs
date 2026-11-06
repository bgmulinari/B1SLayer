using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace B1SLayer;

/// <summary>
///     Provides URL building and encoding helpers.
/// </summary>
/// <remarks>
///     Encoding rules: path segments and query parameter names have only strictly illegal characters encoded,
///     preserving reserved characters such as '$', '(', ')' and quotes that are common in OData URLs, while
///     query parameter values are fully percent-encoded.
/// </remarks>
internal static class SLUrl
{
    // Uri.EscapeDataString throws on longer inputs, so bigger strings must be encoded in chunks
    private const int MaxEscapeDataStringLength = 65519;

    /// <summary>
    ///     Combines the given URL parts of a batch part, ensuring a single '/' separator and encoding illegal characters.
    ///     The relative part may carry its own query string.
    /// </summary>
    public static string Combine(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative))
        {
            return EncodeIllegalCharacters(root);
        }

        var encodedRoot = EncodeIllegalCharacters(root);
        var queryStart = FindQueryStart(relative);

        var path = queryStart < 0 ? relative : relative.Substring(0, queryStart);

        // Unlike in the path of a regular request, the Service Layer reads a '+' in the path of a batch part as a space
        var url = AppendResourcePath(encodedRoot, path.Replace("+", "%2B"));

        if (queryStart < 0)
        {
            return url;
        }

        // The Service Layer also decodes the query of a batch part twice, which turns %2B into a '+' and then a space. Only
        // this one is encoded again, as that double decoding is an observation of 10.0 FP 2608 rather than documented behavior
        return url + "?" + string.Join("&", SplitQuery(relative.Substring(queryStart + 1))).Replace("%2B", "%252B").Replace("%2b", "%252b");
    }

    /// <summary>
    ///     Appends the path of a resource to the URL, which is empty for a resource made up of only a query string.
    /// </summary>
    public static string AppendResourcePath(string url, string path) => path.Length == 0 ? url : AppendPathSegment(url, path);

    /// <summary>
    ///     Finds the '?' that starts the query string of a resource, or -1 when it has none. A '?' within a quoted key
    ///     (e.g. BusinessPartners('A?B')) is data, not a query delimiter, unless the quotes aren't balanced, which leaves
    ///     nothing to tell it from one.
    /// </summary>
    public static int FindQueryStart(string resource)
    {
        var inQuotes = false;

        for (var i = 0; i < resource.Length; i++)
            if (IsQuote(resource, i))
            {
                inQuotes = !inQuotes;
            }
            else if (resource[i] == '?' && !inQuotes)
            {
                return i;
            }

        return inQuotes ? resource.IndexOf('?') : -1;
    }

    /// <summary>
    ///     Encodes the query string of a resource and splits it into its parameters. A '&amp;' within a quoted
    ///     literal (e.g. $filter=ItemName eq 'A&amp;B') is data, not a parameter delimiter, so it gets encoded as such, like a
    ///     '+' within one.
    ///     Unbalanced quotes (e.g. name=O'Brien&amp;$top=1) are no literal, so they don't merge the parameters after them.
    /// </summary>
    public static IEnumerable<string> SplitQuery(string query)
    {
        // '#' is always data in a Service Layer resource (e.g. an item code), never a fragment
        // delimiter — left unencoded, everything after it would be dropped from the request
        query = EncodeIllegalCharacters(query.Replace("#", "%23"));

        var hasBalancedQuotes = Enumerable.Range(0, query.Length).Count(i => IsQuote(query, i)) % 2 == 0;
        var part = new StringBuilder();
        var inQuotes = false;
        var inPlainQuotes = false;

        for (var i = 0; i < query.Length; i++)
        {
            var character = query[i];

            if (IsQuote(query, i))
            {
                inQuotes = !inQuotes;
                inPlainQuotes = inQuotes && character == '\'';
                part.Append(character);
            }
            else if (character == '&' && !(inQuotes && hasBalancedQuotes))
            {
                if (part.Length > 0)
                {
                    yield return part.ToString();
                    part.Clear();
                }
            }
            else if (character == '&')
            {
                part.Append("%26");
            }
            else if (character == '+' && inPlainQuotes && hasBalancedQuotes)
            {
                // The Service Layer reads a '+' in a query as a space, so 'A+B' would silently match the record named "A B". With
                // an encoded quote, the whole query was encoded by something else, whose '+' may well be a space
                part.Append("%2B");
            }
            else
            {
                part.Append(character);
            }
        }

        if (part.Length > 0)
        {
            yield return part.ToString();
        }
    }

    /// <summary>
    ///     Appends a path segment to the URL, ensuring a single '/' separator and encoding
    ///     illegal characters, '?' and '#' in the segment.
    /// </summary>
    public static string AppendPathSegment(string url, string segment)
    {
        var encodedSegment = EncodeIllegalCharacters(segment)
            .Replace("?", "%3F")
            .Replace("#", "%23");

        return url.TrimEnd('/') + "/" + encodedSegment.TrimStart('/');
    }

    /// <summary>
    ///     Builds a query string from the given name/value pairs. Names have illegal characters
    ///     encoded, while values are fully percent-encoded.
    /// </summary>
    public static string BuildQueryString(IEnumerable<KeyValuePair<string, string>> queryParams)
    {
        return string.Join("&", queryParams.Select(x => $"{EncodeIllegalCharacters(x.Key)}={EncodeQueryValue(x.Value)}"));
    }

    /// <summary>
    ///     Fully percent-encodes a query parameter value, including reserved characters.
    /// </summary>
    public static string EncodeQueryValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        if (value.Length <= MaxEscapeDataStringLength)
        {
            return Uri.EscapeDataString(value);
        }

        var result = new StringBuilder(value.Length * 2);
        var start = 0;

        while (start < value.Length)
        {
            var length = Math.Min(MaxEscapeDataStringLength, value.Length - start);

            // A chunk must not end between the halves of a surrogate pair, as each lone half
            // would be encoded as a replacement character, corrupting the value
            if (start + length < value.Length && char.IsHighSurrogate(value[start + length - 1]))
            {
                length--;
            }

            result.Append(Uri.EscapeDataString(value.Substring(start, length)));
            start += length;
        }

        return result.ToString();
    }

    /// <summary>
    ///     Percent-encodes characters that are neither reserved nor unreserved (RFC 3986),
    ///     preserving '%' when it begins a valid percent-encoded sequence to avoid double encoding.
    /// </summary>
    public static string EncodeIllegalCharacters(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        StringBuilder result = null;

        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];

            if (IsAllowedInUri(character) || IsEscapeSequenceStart(value, i))
            {
                result?.Append(character);
                continue;
            }

            if (result == null)
            {
                result = new StringBuilder(value.Length + 16);
                result.Append(value, 0, i);
            }

            // Encodes the character (or surrogate pair) as percent-encoded UTF-8 bytes
            if (char.IsHighSurrogate(character) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                result.Append(Uri.EscapeDataString(value.Substring(i, 2)));
                i++;
            }
            else
            {
                result.Append(Uri.EscapeDataString(character.ToString()));
            }
        }

        return result?.ToString() ?? value;
    }

    /// <summary>
    ///     Determines whether a quote starts at the given position, in its plain or percent-encoded form, as a resource may
    ///     mix them. An OData literal escapes a quote by doubling it, which also holds for an encoded one, so the quotes stay
    ///     balanced.
    /// </summary>
    private static bool IsQuote(string value, int position) =>
        value[position] == '\''
        || value[position] == '%' && position + 2 < value.Length && value[position + 1] == '2' && value[position + 2] == '7';

    /// <summary>
    ///     Determines whether the character is an RFC 3986 unreserved or reserved character, allowed to appear unencoded in a
    ///     URI.
    /// </summary>
    private static bool IsAllowedInUri(char character)
    {
        if (character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return true;
        }

        return character is '-' or '.' or '_' or '~'
            or ':' or '/' or '?' or '#' or '[' or ']' or '@'
            or '!' or '$' or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or ';' or '=';
    }

    /// <summary>
    ///     Determines whether the character at the given position starts a valid percent-encoded sequence.
    /// </summary>
    private static bool IsEscapeSequenceStart(string value, int position) =>
        value[position] == '%'
        && position + 2 < value.Length
        && IsHexDigit(value[position + 1])
        && IsHexDigit(value[position + 2]);

    private static bool IsHexDigit(char character) => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
