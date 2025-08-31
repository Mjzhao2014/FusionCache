using System.Collections.Immutable;

namespace ZiggyCreatures.Caching.Fusion.Dependencies;

/// <summary>
/// Builder class for creating cache entry dependencies.
/// </summary>
public sealed class DependencyBuilder
{
	private readonly List<string> _keys = new();
	private readonly List<string> _tags = new();

	/// <summary>
	/// Adds key dependencies to this entry. When any of these keys change, this entry will be invalidated.
	/// </summary>
	/// <param name="keys">The keys this entry depends on.</param>
	/// <returns>This builder instance for method chaining.</returns>
	public DependencyBuilder Keys(params string[] keys)
	{
		if (keys != null)
		{
			_keys.AddRange(keys);
		}
		return this;
	}

	/// <summary>
	/// Adds tag dependencies to this entry. When any entries with these tags change, this entry will be invalidated.
	/// </summary>
	/// <param name="tags">The tags this entry depends on.</param>
	/// <returns>This builder instance for method chaining.</returns>
	public DependencyBuilder Tags(params string[] tags)
	{
		if (tags != null)
		{
			_tags.AddRange(tags);
		}
		return this;
	}

	/// <summary>
	/// Builds the dependency information.
	/// </summary>
	/// <returns>The dependency information, or null if no dependencies were specified.</returns>
	internal DependencyInfo? Build()
	{
		if (_keys.Count == 0 && _tags.Count == 0)
			return null;

		return new DependencyInfo(
			_keys.ToImmutableHashSet(),
			_tags.ToImmutableHashSet()
		);
	}
}