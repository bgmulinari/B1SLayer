namespace B1SLayer.Test;

/// <summary>
///     URL helpers for building expected request URLs in assertions.
/// </summary>
internal static class TestUrlExtensions
{
    /// <summary>
    ///     Appends a path segment to the URI, ensuring a single '/' separator.
    /// </summary>
    public static string AppendPathSegment(this Uri uri, string segment) => $"{uri.ToString().TrimEnd('/')}/{segment.TrimStart('/')}";
}
