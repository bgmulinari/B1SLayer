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
    ///     Combines the given URL parts, ensuring a single '/' separator and encoding illegal characters.
    ///     The relative part may carry its own query string.
    /// </summary>
    public static string Combine(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative))
        {
            return EncodeIllegalCharacters(root);
        }

        // '#' is always data in a Service Layer resource (e.g. an item code), never a fragment
        // delimiter — left unencoded, everything after it would be dropped from the request path
        relative = relative.Replace("#", "%23");

        var combined = relative.StartsWith("?", StringComparison.Ordinal)
            ? root.TrimEnd('?') + relative
            : root.TrimEnd('/') + "/" + relative.TrimStart('/');

        return EncodeIllegalCharacters(combined);
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
    ///     Determines whether the character is an RFC 3986 unreserved or reserved character, allowed to appear unencoded in a URI.
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
