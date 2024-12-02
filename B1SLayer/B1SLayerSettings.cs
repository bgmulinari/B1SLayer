using Microsoft.Extensions.Caching.Distributed;

namespace B1SLayer;

/// <summary>
/// A static object for configuring B1SLayer.
/// </summary>
public static class B1SLayerSettings
{
    /// <summary>
    /// Gets or sets the <see cref="IDistributedCache"/> implementation to be used for session management. By default, an in-memory implementation is used.
    /// </summary>
    public static IDistributedCache DistributedCache { get; set; } = new SimpleMemoryDistributedCache();
}
