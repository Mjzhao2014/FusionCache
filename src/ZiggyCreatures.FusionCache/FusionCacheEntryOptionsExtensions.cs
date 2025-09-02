namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Extension helpers for working with <see cref="FusionCacheEntryOptions"/>.
/// </summary>
public static class FusionCacheEntryOptionsExtensions
{
	/// <summary>
	/// Attaches a dependency builder to these entry options, used to declare parent/child relationships when adding the entry.
	/// This enables cascading invalidation of dependent entries when parent keys change.
	/// </summary>
	public static FusionCacheEntryOptions WithDependencies(this FusionCacheEntryOptions opts, DependencyBuilder deps)
	{
		if (opts is null)
			throw new ArgumentNullException(nameof(opts));
		if (deps is null)
			throw new ArgumentNullException(nameof(deps));
		opts.Dependencies = deps;
		return opts;
	}
}
