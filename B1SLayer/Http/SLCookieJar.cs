using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace B1SLayer;

/// <summary>
///     A session cookie store that can be serialized for distributed caching and rendered as a
///     Cookie request header, honoring the browser-like sending rules (domain, path, security
///     and expiry) for the target URL.
/// </summary>
internal sealed class SLCookieJar : IEnumerable<SLCookie>
{
    private readonly List<SLCookie> _cookies = [];

    /// <summary>
    ///     Gets the number of cookies in the jar.
    /// </summary>
    public int Count => _cookies.Count;

    public IEnumerator<SLCookie> GetEnumerator() => _cookies.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    ///     Adds the given cookie to the jar, replacing any existing cookie with the same name.
    /// </summary>
    public void AddOrReplace(SLCookie cookie)
    {
        var index = _cookies.FindIndex(x => string.Equals(x.Name, cookie.Name, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            _cookies[index] = cookie;
        }
        else
        {
            _cookies.Add(cookie);
        }
    }

    /// <summary>
    ///     Finds a cookie by name, or null if not present.
    /// </summary>
    public SLCookie Find(string name)
    {
        return _cookies.Find(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Creates a new jar containing the same cookies, so per-call updates (e.g. cookies set on
    ///     redirect hops) don't mutate a jar shared across requests.
    /// </summary>
    public SLCookieJar Clone()
    {
        var clone = new SLCookieJar();
        clone._cookies.AddRange(_cookies);
        return clone;
    }

    /// <summary>
    ///     Merges the given Set-Cookie header values into the jar. An expired cookie
    ///     (e.g. Max-Age=0) removes the matching cookie, honoring deletion semantics.
    /// </summary>
    public void MergeSetCookieHeaders(IEnumerable<string> setCookieHeaders, Uri originUri)
    {
        foreach (var setCookieHeader in setCookieHeaders)
        {
            var cookie = ParseSetCookieHeader(setCookieHeader, originUri);

            if (cookie == null)
            {
                continue;
            }

            if (cookie.Expires is { } expires && expires <= DateTimeOffset.UtcNow)
            {
                _cookies.RemoveAll(x => string.Equals(x.Name, cookie.Name, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                AddOrReplace(cookie);
            }
        }
    }

    /// <summary>
    ///     Renders the cookies valid for the given request URL as a Cookie header value, or null when
    ///     none apply. Cookies are ordered by longest path first (RFC 6265), preserving insertion
    ///     order within the same path length.
    /// </summary>
    public string GetCookieHeaderFor(Uri requestUri)
    {
        if (_cookies.Count == 0)
        {
            return null;
        }

        var applicableCookies = _cookies
            .Where(x => ShouldSend(x, requestUri))
            .OrderByDescending(x => x.Path?.Length ?? 0)
            .Select(x => $"{x.Name}={x.Value}")
            .ToList();

        return applicableCookies.Count == 0 ? null : string.Join("; ", applicableCookies);
    }

    /// <summary>
    ///     Determines whether the cookie is valid for the given request URL,
    ///     considering expiry, security, domain and path.
    /// </summary>
    private static bool ShouldSend(SLCookie cookie, Uri requestUri)
    {
        if (cookie.Expires is { } expires && expires <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        if (cookie.Secure && !string.Equals(requestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // An explicit domain matches itself and its subdomains; a host-only cookie matches its exact
        // origin host; cookies without either (SSO context, legacy cache entries) match any host
        if (!string.IsNullOrEmpty(cookie.Domain))
        {
            var domain = cookie.Domain.TrimStart('.');

            if (!requestUri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                && !requestUri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        else if (!string.IsNullOrEmpty(cookie.OriginHost) && !requestUri.Host.Equals(cookie.OriginHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsPathMatch(cookie.Path, requestUri.AbsolutePath);
    }

    /// <summary>
    ///     RFC 6265 path-match: the request path equals the cookie path, or starts
    ///     with it at a '/' boundary. A null/root cookie path matches everything.
    /// </summary>
    private static bool IsPathMatch(string cookiePath, string requestPath)
    {
        if (string.IsNullOrEmpty(cookiePath))
        {
            return true;
        }

        if (cookiePath.Length > 1 && cookiePath.EndsWith("/", StringComparison.Ordinal))
        {
            cookiePath = cookiePath.TrimEnd('/');
        }

        if (cookiePath == "/")
        {
            return true;
        }

        if (string.IsNullOrEmpty(requestPath))
        {
            requestPath = "/";
        }

        // Path is case-sensitive, unlike Domain
        return requestPath.Equals(cookiePath, StringComparison.Ordinal)
               || requestPath.StartsWith(cookiePath, StringComparison.Ordinal) && requestPath[cookiePath.Length] == '/';
    }

    /// <summary>
    ///     Serializes the cookies to a string suitable for distributed caching, or null if the jar is empty.
    /// </summary>
    public string Serialize() => _cookies.Count == 0 ? null : JsonSerializer.Serialize(_cookies);

    /// <summary>
    ///     Deserializes a jar previously serialized with <see cref="Serialize" />.
    ///     Returns null when the value is empty or can't be parsed, so invalid
    ///     cache entries degrade to a fresh login instead of failing.
    /// </summary>
    public static SLCookieJar Deserialize(string serialized)
    {
        if (string.IsNullOrEmpty(serialized))
        {
            return null;
        }

        try
        {
            var cookies = JsonSerializer.Deserialize<List<SLCookie>>(serialized);

            if (cookies == null || cookies.Count == 0 || cookies.Any(x => string.IsNullOrEmpty(x?.Name)))
            {
                return null;
            }

            var jar = new SLCookieJar();
            jar._cookies.AddRange(cookies);
            return jar;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Parses a single Set-Cookie header value into a <see cref="SLCookie" />, or null if invalid.
    /// </summary>
    private static SLCookie ParseSetCookieHeader(string setCookieHeader, Uri originUri)
    {
        if (string.IsNullOrWhiteSpace(setCookieHeader))
        {
            return null;
        }

        var tokens = setCookieHeader.Split(';');

        // The name/value pair is split on the first '=' only, as the value itself may contain '='
        var separatorIndex = tokens[0].IndexOf('=');

        if (separatorIndex <= 0)
        {
            return null;
        }

        var cookie = new SLCookie
        {
            Name = tokens[0].Substring(0, separatorIndex).Trim(),
            Value = tokens[0].Substring(separatorIndex + 1).Trim(),
            OriginHost = originUri?.Host
        };

        if (string.IsNullOrEmpty(cookie.Name))
        {
            return null;
        }

        DateTimeOffset? expiresAttribute = null;
        int? maxAgeAttribute = null;

        foreach (var token in tokens.Skip(1))
        {
            var attribute = token.Trim();

            if (attribute.StartsWith("path=", StringComparison.OrdinalIgnoreCase))
            {
                cookie.Path = attribute.Substring(5);
            }
            else if (attribute.StartsWith("domain=", StringComparison.OrdinalIgnoreCase))
            {
                cookie.Domain = attribute.Substring(7);
            }
            else if (attribute.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(attribute.Substring(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxAge))
                {
                    maxAgeAttribute = maxAge;
                }
            }
            else if (attribute.StartsWith("expires=", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTimeOffset.TryParse(attribute.Substring(8), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires))
                {
                    expiresAttribute = expires;
                }
            }
            else if (attribute.Equals("secure", StringComparison.OrdinalIgnoreCase))
            {
                cookie.Secure = true;
            }
            else if (attribute.Equals("httponly", StringComparison.OrdinalIgnoreCase))
            {
                cookie.HttpOnly = true;
            }
        }

        // Max-Age takes precedence over Expires (RFC 6265)
        cookie.Expires = maxAgeAttribute is { } seconds
            ? DateTimeOffset.UtcNow.AddSeconds(seconds)
            : expiresAttribute;

        // A path attribute not starting with '/' is ignored in favor of the origin's default-path (RFC 6265 5.2.4)
        if (cookie.Path?.StartsWith("/", StringComparison.Ordinal) != true)
        {
            cookie.Path = GetDefaultPath(originUri);
        }

        return cookie;
    }

    /// <summary>
    ///     Computes the RFC 6265 default-path for a cookie set without a path attribute:
    ///     the origin URL's path up to, but not including, its last '/'.
    /// </summary>
    private static string GetDefaultPath(Uri originUri)
    {
        var originPath = originUri?.AbsolutePath;

        if (string.IsNullOrEmpty(originPath) || originPath[0] != '/' || originPath.Count(x => x == '/') <= 1)
        {
            return "/";
        }

        return originPath.Substring(0, originPath.LastIndexOf('/'));
    }
}
