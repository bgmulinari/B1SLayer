using System;

namespace B1SLayer;

/// <summary>
///     Represents a session cookie.
/// </summary>
internal sealed class SLCookie
{
    /// <summary>
    ///     Gets or sets the cookie name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     Gets or sets the cookie value.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    ///     Gets or sets the cookie path. When the Set-Cookie header carries no path attribute, the
    ///     RFC 6265 default-path computed from the origin URL is stored here at parse time.
    ///     A null path matches every request path.
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    ///     Gets or sets the cookie domain. When set, the cookie is sent to the domain and its subdomains;
    ///     otherwise it is only sent back to <see cref="OriginHost" />.
    /// </summary>
    public string Domain { get; set; }

    /// <summary>
    ///     Gets or sets the host that set the cookie. A cookie without a <see cref="Domain" /> is only
    ///     sent back to this exact host; when null (e.g. SSO context cookies), any host matches.
    /// </summary>
    public string OriginHost { get; set; }

    /// <summary>
    ///     Gets or sets the absolute expiration of the cookie, resolved at parse time
    ///     (Max-Age takes precedence over Expires). Null means a session cookie.
    /// </summary>
    public DateTimeOffset? Expires { get; set; }

    /// <summary>
    ///     Gets or sets whether the cookie is marked as Secure.
    /// </summary>
    public bool Secure { get; set; }

    /// <summary>
    ///     Gets or sets whether the cookie is marked as HttpOnly.
    /// </summary>
    public bool HttpOnly { get; set; }
}
