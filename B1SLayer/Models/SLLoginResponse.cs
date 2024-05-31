using System;

namespace B1SLayer;

/// <summary>
/// Represents the response details of a login request.
/// </summary>
public class SLLoginResponse
{
    /// <summary>
    /// Gets or sets the session ID of the session.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the SBO version.
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Gets or sets the session timeout.
    /// </summary>
    public int SessionTimeout { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="DateTime"/> of the last login.
    /// </summary>
    public DateTime LastLogin { get; set; }
}