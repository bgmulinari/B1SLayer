using System;

namespace B1SLayer;

/// <summary>
///     The identity a single request runs with: the connection's session (cookies obtained through a login)
///     or a user's access token, as returned by <see cref="SLConnectionOptions.IdentityResolver" />.
/// </summary>
public sealed class SLRequestIdentity
{
    private SLRequestIdentity(ISLAccessTokenSource tokenSource, string companyId)
    {
        TokenSource = tokenSource;
        CompanyId = companyId;
    }

    /// <summary>
    ///     Gets the identity of the connection's session account, using its session cookies and logging in automatically when
    ///     needed.
    /// </summary>
    public static SLRequestIdentity Session { get; } = new(null, null);

    /// <summary>
    ///     Gets whether requests with this identity run with the connection's session cookies.
    /// </summary>
    public bool IsSession => TokenSource == null;

    /// <summary>
    ///     Gets the SLD company ID sent in the X-b1-companyid header, or null for <see cref="Session" />.
    /// </summary>
    public string CompanyId { get; }

    /// <summary>
    ///     Gets the source of the access token, or null for <see cref="Session" />.
    /// </summary>
    internal ISLAccessTokenSource TokenSource { get; }

    /// <summary>
    ///     Creates an identity that runs requests with the access tokens of the given source.
    /// </summary>
    /// <remarks>
    ///     Creating an identity is cheap, so one can be returned per resolver call. Keep the token source
    ///     itself per user, as that is what holds a cached token.
    /// </remarks>
    /// <param name="tokenSource">
    ///     The source of the access token.
    /// </param>
    /// <param name="companyId">
    ///     The company to access, as the SLD company ID (see <see cref="SLCompanyBinding.CompanyId" />).
    ///     This is not the company database name.
    /// </param>
    public static SLRequestIdentity FromTokenSource(ISLAccessTokenSource tokenSource, string companyId)
    {
        if (tokenSource == null)
        {
            throw new ArgumentNullException(nameof(tokenSource));
        }

        if (!SLRequestCredential.IsValidCompanyId(companyId))
        {
            throw new ArgumentException("The company ID can not be empty and must only contain printable ASCII characters.", nameof(companyId));
        }

        return new SLRequestIdentity(tokenSource, companyId);
    }

    /// <summary>
    ///     Returns a description of this identity, which never includes a token.
    /// </summary>
    public override string ToString() => IsSession
        ? $"{nameof(SLRequestIdentity)}({nameof(Session)})"
        : $"{nameof(SLRequestIdentity)}(Token, {nameof(CompanyId)}={CompanyId})";
}
