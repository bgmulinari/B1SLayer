using System;
using System.Net;

namespace B1SLayer;

/// <summary>
///     Represents a failure to authenticate with an access token: a token source that failed, or an OpenID Connect
///     provider or System Landscape Directory (SLD) request that was rejected or returned an invalid response.
/// </summary>
/// <remarks>
///     A rejection of the token by the Service Layer itself is a regular <see cref="SLException" /> with status code 401.
///     This exception never carries tokens, secrets, client assertions or the raw body of a token response, so
///     <see cref="SLException.ResponseContent" /> is always null.
/// </remarks>
public class SLAuthenticationException : SLException
{
    internal SLAuthenticationException(string message, Exception innerException = null, HttpStatusCode? statusCode = null, string oAuthError = null, string oAuthErrorDescription = null)
        : base(message, null, statusCode, null, innerException)
    {
        OAuthError = oAuthError;
        OAuthErrorDescription = oAuthErrorDescription;
    }

    /// <summary>
    ///     Gets the OAuth error code returned by the token endpoint (RFC 6749 section 5.2), or null when there is none.
    ///     For example, "invalid_grant" on a refresh means the refresh token is no longer valid and the user must sign in
    ///     again.
    /// </summary>
    public string OAuthError { get; }

    /// <summary>
    ///     Gets the human-readable description the token endpoint returned along with <see cref="OAuthError" />, or null when
    ///     there is none.
    /// </summary>
    public string OAuthErrorDescription { get; }
}
