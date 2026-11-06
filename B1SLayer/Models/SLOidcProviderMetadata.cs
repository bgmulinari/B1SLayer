using System;

namespace B1SLayer;

/// <summary>
///     Represents the OpenID Connect provider metadata of the landscape's authentication server,
///     discovered through the System Landscape Directory (SLD) with its issuer validated.
/// </summary>
public sealed class SLOidcProviderMetadata
{
    internal SLOidcProviderMetadata()
    {
    }

    /// <summary>
    ///     Gets the discovery document URI returned by the SLD. An application that signs users in
    ///     through its own OIDC library should configure it with this URI or with <see cref="Issuer" />.
    /// </summary>
    public Uri DiscoveryUri { get; internal set; }

    /// <summary>
    ///     Gets the issuer identifier of the provider.
    /// </summary>
    public string Issuer { get; internal set; }

    /// <summary>
    ///     Gets the token endpoint.
    /// </summary>
    public Uri TokenEndpoint { get; internal set; }

    /// <summary>
    ///     Gets the authorization endpoint, or null when not advertised.
    /// </summary>
    public Uri AuthorizationEndpoint { get; internal set; }

    /// <summary>
    ///     Gets the UserInfo endpoint, or null when not advertised.
    /// </summary>
    public Uri UserInfoEndpoint { get; internal set; }

    /// <summary>
    ///     Gets the end session (logout) endpoint, or null when not advertised.
    /// </summary>
    public Uri EndSessionEndpoint { get; internal set; }

    /// <summary>
    ///     Gets the token revocation endpoint, or null when not advertised.
    /// </summary>
    public Uri RevocationEndpoint { get; internal set; }

    /// <summary>
    ///     Gets the URI of the provider's JSON Web Key Set, or null when not advertised.
    /// </summary>
    public Uri JwksUri { get; internal set; }
}
