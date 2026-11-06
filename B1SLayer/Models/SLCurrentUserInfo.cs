using System.Collections.Generic;

namespace B1SLayer;

/// <summary>
///     Represents the user an access token belongs to and the companies the user can access,
///     as returned by the CurrentUserInfo API of the System Landscape Directory (SLD).
/// </summary>
public sealed class SLCurrentUserInfo
{
    internal SLCurrentUserInfo()
    {
    }

    /// <summary>
    ///     Gets the identity of the user.
    /// </summary>
    public string UserIdentity { get; internal set; }

    /// <summary>
    ///     Gets the name of the user.
    /// </summary>
    public string UserName { get; internal set; }

    /// <summary>
    ///     Gets the companies the user is bound to.
    /// </summary>
    public IReadOnlyList<SLCompanyBinding> CompanyBindings { get; internal set; }

    /// <summary>
    ///     Gets the CurrentUserInfo object as returned by the SLD, for what isn't modeled here, such
    ///     as the user roles SAP documents as part of this response without specifying their format.
    /// </summary>
    public string RawJson { get; internal set; }
}

/// <summary>
///     Represents a company a user is bound to, along with the SAP Business One user it is bound through.
/// </summary>
public sealed class SLCompanyBinding
{
    internal SLCompanyBinding()
    {
    }

    /// <summary>
    ///     Gets the SLD company ID, which is what a request sent with an access token selects the company with.
    /// </summary>
    public string CompanyId { get; internal set; }

    /// <summary>
    ///     Gets the database schema name of the company.
    /// </summary>
    public string CompanySchemaName { get; internal set; }

    /// <summary>
    ///     Gets the display name of the company.
    /// </summary>
    public string CompanyDisplayName { get; internal set; }

    /// <summary>
    ///     Gets the code of the SAP Business One user that requests to this company run as.
    /// </summary>
    public string UserCode { get; internal set; }

    /// <summary>
    ///     Gets the ID of the service unit the company belongs to.
    /// </summary>
    public string ServiceUnitId { get; internal set; }

    /// <summary>
    ///     Gets the name of the service unit the company belongs to.
    /// </summary>
    public string ServiceUnitName { get; internal set; }

    /// <summary>
    ///     Gets the name of the database instance the company belongs to.
    /// </summary>
    public string DatabaseInstanceName { get; internal set; }

    /// <summary>
    ///     Gets the version of the company.
    /// </summary>
    public string Version { get; internal set; }

    /// <summary>
    ///     Gets the localization of the company.
    /// </summary>
    public string Localization { get; internal set; }

    /// <summary>
    ///     Gets whether the user binding is confirmed.
    /// </summary>
    public bool IsConfirmed { get; internal set; }

    /// <summary>
    ///     Gets whether the user is a superuser in the company.
    /// </summary>
    public bool IsSuperuser { get; internal set; }
}
