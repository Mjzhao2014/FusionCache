namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Extension helpers for <see cref="FusionCacheEntryOptions"/>.
/// </summary>
public static class FusionCacheEntryOptionsExtensions
{
    /// <summary>
    /// Attach dependency information to this entry options.
    /// </summary>
    public static FusionCacheEntryOptions WithDependencies(this FusionCacheEntryOptions opts, DependencyBuilder deps)
    {
        opts.Dependencies = deps;
        return opts;
    }
}
