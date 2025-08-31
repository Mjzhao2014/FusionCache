namespace ZiggyCreatures.Caching.Fusion.Dependencies;

/// <summary>
/// Static helper class for creating dependency builders.
/// </summary>
public static class DependsOn
{
	/// <summary>
	/// Creates a dependency builder starting with key dependencies.
	/// </summary>
	/// <param name="keys">The keys this entry depends on.</param>
	/// <returns>A new dependency builder instance.</returns>
	public static DependencyBuilder Keys(params string[] keys)
	{
		return new DependencyBuilder().Keys(keys);
	}

	/// <summary>
	/// Creates a dependency builder starting with tag dependencies.
	/// </summary>
	/// <param name="tags">The tags this entry depends on.</param>
	/// <returns>A new dependency builder instance.</returns>
	public static DependencyBuilder Tags(params string[] tags)
	{
		return new DependencyBuilder().Tags(tags);
	}
}