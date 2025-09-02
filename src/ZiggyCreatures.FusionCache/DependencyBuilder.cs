using System;
using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// A fluent builder used to attach key dependencies when setting a cache entry. A dependency means that if one of the parent keys changes,
/// this entry (the child) should be invalidated.
/// </summary>
public sealed class DependencyBuilder
{
	private readonly HashSet<string> _parentKeys = new(StringComparer.Ordinal);
	private readonly HashSet<string> _childKeys = new(StringComparer.Ordinal);

	/// <summary>
	/// Adds one or more parent keys this entry depends on.
	/// </summary>
	public DependencyBuilder Keys(params string[] keys)
	{
		if (keys is null)
			throw new ArgumentNullException(nameof(keys));
		foreach (var k in keys)
		{
			if (k is null)
				throw new ArgumentNullException(nameof(keys), "A dependency parent key cannot be null");
			_parentKeys.Add(k);
		}
		return this;
	}

	/// <summary>
	/// Declares that this entry is a parent of the given child key, adding an explicit parent→child edge.
	/// </summary>
	public DependencyBuilder ParentOf(string childKey)
	{
		if (childKey is null)
			throw new ArgumentNullException(nameof(childKey));
		_childKeys.Add(childKey);
		return this;
	}

	internal IReadOnlyCollection<string> ParentKeys => _parentKeys;
	internal IReadOnlyCollection<string> ChildKeys => _childKeys;
}

/// <summary>
/// Convenience static factory for creating <see cref="DependencyBuilder"/> instances.
/// </summary>
public static class DependsOn
{
	public static DependencyBuilder Keys(params string[] keys)
	{
		return new DependencyBuilder().Keys(keys);
	}

	public static DependencyBuilder ParentOf(string childKey)
	{
		return new DependencyBuilder().ParentOf(childKey);
	}
}
