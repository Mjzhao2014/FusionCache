using System.Collections.Immutable;
using System.Diagnostics;

namespace ZiggyCreatures.Caching.Fusion.Dependencies;

/// <summary>
/// Represents dependency information for a cache entry.
/// </summary>
[DebuggerDisplay("Keys: {ParentKeys.Count}, Tags: {ParentTags.Count}")]
public sealed class DependencyInfo
{
	/// <summary>
	/// Creates a new dependency info instance.
	/// </summary>
	/// <param name="parentKeys">The parent keys this entry depends on.</param>
	/// <param name="parentTags">The parent tags this entry depends on.</param>
	public DependencyInfo(ImmutableHashSet<string> parentKeys, ImmutableHashSet<string> parentTags)
	{
		ParentKeys = parentKeys;
		ParentTags = parentTags;
	}

	/// <summary>
	/// The parent keys this entry depends on.
	/// </summary>
	public ImmutableHashSet<string> ParentKeys { get; }

	/// <summary>
	/// The parent tags this entry depends on.
	/// </summary>
	public ImmutableHashSet<string> ParentTags { get; }

	/// <summary>
	/// Checks if this dependency info has any dependencies.
	/// </summary>
	public bool HasDependencies => ParentKeys.Count > 0 || ParentTags.Count > 0;
}