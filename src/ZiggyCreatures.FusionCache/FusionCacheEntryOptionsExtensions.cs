namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Extension helpers for working with <see cref="FusionCacheEntryOptions"/>.
/// </summary>
public static class FusionCacheEntryOptionsExtensions
{
	/// <summary>
	/// Attach the given dependency builder so that any edges specified will be updated when the entry is saved.
	/// </summary>
	public static FusionCacheEntryOptions WithDependencies(this FusionCacheEntryOptions opts, DependencyBuilder deps)
	{
		if (opts is null)
			throw new ArgumentNullException(nameof(opts));
		opts.Dependencies = deps;
		return opts;
	}
}
