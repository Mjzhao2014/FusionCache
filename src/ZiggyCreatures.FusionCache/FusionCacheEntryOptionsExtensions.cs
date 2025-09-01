using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Extension methods for <see cref="FusionCacheEntryOptions"/>.
/// </summary>
public static class FusionCacheEntryOptionsExtensions
{
	/// <summary>
	/// Attach a dependency descriptor to these entry options.
	/// </summary>
	/// <param name="opts">The options.</param>
	/// <param name="deps">A dependency builder describing parent and/or child relationships.</param>
	/// <returns>The same options for chaining.</returns>
	public static FusionCacheEntryOptions WithDependencies(this FusionCacheEntryOptions opts, DependencyBuilder deps)
	{
		if (opts is null)
			throw new ArgumentNullException(nameof(opts));
		if (deps is null)
			throw new ArgumentNullException(nameof(deps));
		opts.DependencyParentKeys = deps.ParentKeys;
		opts.DependencyChildKeys = deps.ChildKeys;
		return opts;
	}
}
