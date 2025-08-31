namespace ZiggyCreatures.Caching.Fusion.Dependencies;

/// <summary>
/// Extension methods for <see cref="FusionCacheEntryOptions"/> to support dependencies.
/// </summary>
public static class FusionCacheEntryOptionsExtensions
{
	/// <summary>
	/// Adds dependency information to the cache entry options. When any of the specified parent keys or tags change, this entry will be automatically invalidated.
	/// </summary>
	/// <param name="options">The options to modify.</param>
	/// <param name="dependencies">The dependency builder containing parent keys and tags.</param>
	/// <returns>The <see cref="FusionCacheEntryOptions"/> so that additional calls can be chained.</returns>
	public static FusionCacheEntryOptions WithDependencies(this FusionCacheEntryOptions options, DependencyBuilder dependencies)
	{
		if (options == null)
			throw new ArgumentNullException(nameof(options));

		if (dependencies == null)
			throw new ArgumentNullException(nameof(dependencies));

		options.Dependencies = dependencies.Build();
		return options;
	}
}