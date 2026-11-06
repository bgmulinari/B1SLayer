namespace B1SLayer;

/// <summary>
///     The credential a single send attempt carries: nothing (Login, Ping), session cookies or an access token.
///     Having one value hold exactly one of them is what makes a request carrying both unrepresentable before
///     <see cref="SLConnection" /> enforces it on the wire.
/// </summary>
internal sealed class SLRequestCredential
{
    private SLRequestCredential(SLRequestIdentity identity, SLCookieJar sessionCookies, string accessToken)
    {
        Identity = identity;
        SessionCookies = sessionCookies;
        AccessToken = accessToken;
    }

    /// <summary>
    ///     An attempt that doesn't authenticate, such as the Login request itself or Ping.
    /// </summary>
    public static SLRequestCredential None { get; } = new(null, null, null);

    /// <summary>
    ///     The identity this credential was resolved from, or null when the attempt doesn't authenticate.
    /// </summary>
    public SLRequestIdentity Identity { get; }

    /// <summary>
    ///     The session cookies to send, or null when this isn't a session credential.
    /// </summary>
    public SLCookieJar SessionCookies { get; }

    /// <summary>
    ///     The access token to send, or null when this isn't a token credential.
    /// </summary>
    public string AccessToken { get; }

    /// <summary>
    ///     Whether this attempt authenticates with an access token.
    /// </summary>
    public bool IsToken => AccessToken != null;

    public static SLRequestCredential ForSession(SLCookieJar sessionCookies) => new(SLRequestIdentity.Session, sessionCookies, null);

    public static SLRequestCredential ForToken(SLRequestIdentity identity, string accessToken) => new(identity, null, accessToken);

    /// <summary>
    ///     Determines whether the value is a syntactically valid RFC 6750 bearer token (b64token), which
    ///     also rules out anything that could break out of the Authorization header.
    /// </summary>
    public static bool IsValidBearerToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var length = token.Length;

        // Only trailing '=' padding is allowed, and at least one other character must precede it
        while (length > 0 && token[length - 1] == '=') length--;

        if (length == 0)
        {
            return false;
        }

        for (var i = 0; i < length; i++)
        {
            var c = token[i];

            if (c is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.' or '_' or '~' or '+' or '/'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Determines whether the value can be sent as the X-b1-companyid header value.
    /// </summary>
    public static bool IsValidCompanyId(string companyId)
    {
        if (string.IsNullOrWhiteSpace(companyId))
        {
            return false;
        }

        foreach (var c in companyId)
        {
            if (c is < ' ' or > '~')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Returns a description of this credential, which never includes a token or cookie.
    /// </summary>
    public override string ToString() => $"{nameof(SLRequestCredential)}({(IsToken ? "Token" : SessionCookies != null ? "Session" : "None")})";
}
