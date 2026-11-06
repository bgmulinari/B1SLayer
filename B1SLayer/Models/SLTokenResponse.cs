using System;

namespace B1SLayer;

/// <summary>
///     Represents the tokens issued by the authentication server, either obtained through <see cref="SLOidcClient" />
///     or built by the application from the tokens of its own OIDC library.
/// </summary>
public sealed class SLTokenResponse
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SLTokenResponse" /> class.
    /// </summary>
    /// <param name="accessToken">
    ///     The access token.
    /// </param>
    /// <param name="expiresAtUtc">
    ///     When the access token expires, or null when unknown, in which case it is used until it is rejected.
    /// </param>
    /// <param name="refreshToken">
    ///     The refresh token, when there is one.
    /// </param>
    /// <param name="idToken">
    ///     The ID token, when there is one.
    /// </param>
    /// <param name="scope">
    ///     The scope of the access token, when known.
    /// </param>
    /// <param name="refreshExpiresAtUtc">
    ///     When the refresh token expires, or null when unknown or when it has no expiry of its own.
    /// </param>
    public SLTokenResponse(string accessToken, DateTimeOffset? expiresAtUtc = null, string refreshToken = null, string idToken = null, string scope = null, DateTimeOffset? refreshExpiresAtUtc = null)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new ArgumentException("The access token can not be empty.", nameof(accessToken));
        }

        AccessToken = accessToken;
        ExpiresAtUtc = expiresAtUtc;
        RefreshToken = refreshToken;
        IdToken = idToken;
        Scope = scope;
        RefreshExpiresAtUtc = refreshExpiresAtUtc;
    }

    /// <summary>
    ///     Gets the access token.
    /// </summary>
    public string AccessToken { get; }

    /// <summary>
    ///     Gets when the access token expires, or null when unknown.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; }

    /// <summary>
    ///     Gets the refresh token, or null when there is none, as is the case of the client credentials grant.
    ///     After <see cref="SLOidcClient.RefreshTokenAsync" />, this is always the refresh token to use next,
    ///     whether the server rotated it or not, and it is the one the application must persist.
    /// </summary>
    public string RefreshToken { get; }

    /// <summary>
    ///     Gets when the refresh token expires, or null when unknown or when it has no expiry of its own, as
    ///     the authentication server reports for an offline token. Once it has expired, the user must sign in again.
    /// </summary>
    public DateTimeOffset? RefreshExpiresAtUtc { get; }

    /// <summary>
    ///     Gets the ID token, or null when there is none.
    /// </summary>
    /// <remarks>
    ///     B1SLayer does not validate it, so it must not be used as proof of the user's identity.
    ///     It is exposed to be sent as the id_token_hint of the end session endpoint. A refresh may omit it (OpenID Connect
    ///     Core section 12.2), in which case <see cref="SLOidcClient.RefreshTokenAsync" /> returns null here and the previous
    ///     one still applies. A source from <see cref="SLOidcClient.CreateRefreshTokenSource" /> carries the previous one
    ///     over.
    /// </remarks>
    public string IdToken { get; }

    /// <summary>
    ///     Gets the scope of the access token, or null when not known.
    /// </summary>
    public string Scope { get; }

    /// <summary>
    ///     Returns a description of this response, which never includes a token.
    /// </summary>
    public override string ToString() => $"{nameof(SLTokenResponse)}({nameof(ExpiresAtUtc)}={ExpiresAtUtc:O}, {nameof(RefreshToken)}={(RefreshToken == null ? "no" : "yes")})";
}
