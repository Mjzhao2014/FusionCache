namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Extension helpers for building up <see cref="FusionCacheEntryOptions"/> fluently with additional behaviors.
/// </summary>
public static class FusionCacheEntryOptionsExtensions
{
	/// <summary>
	/// Attaches a set of dependencies to this set of entry options so they will be registered when the entry is written.
	/// </summary>
	public static FusionCacheEntryOptions WithDependencies(this FusionCacheEntryOptions options, DependencyBuilder deps)
	{
		if (options is null)
			throw new ArgumentNullException(nameof(options));
		if (deps is null)
			throw new ArgumentNullException(nameof(deps));
		options.Dependencies = deps;
		return options;
	}
}
