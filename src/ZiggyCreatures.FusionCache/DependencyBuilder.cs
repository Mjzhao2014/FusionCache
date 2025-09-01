using System;
using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// A fluent builder used to describe cache entry dependencies on other cache keys.
/// </summary>
public sealed class DependencyBuilder
{
	private readonly List<string> _parentKeys = [];
	private readonly List<string> _childKeys = [];

	/// <summary>
	/// Specify one or more parent keys this entry depends on.
	/// </summary>
	/// <param name="keys">The parent cache keys this entry depends on.</param>
	/// <returns>The builder.</returns>
	public DependencyBuilder Keys(params string[] keys)
	{
		if (keys is null)
			throw new ArgumentNullException(nameof(keys));
		_parentKeys.AddRange(keys);
		return this;
	}

	/// <summary>
	/// Specify this entry is the parent of the specified child key.
	/// </summary>
	/// <param name="childKey">An existing cache key that depends on this entry.</param>
	/// <returns>The builder.</returns>
	public DependencyBuilder ParentOf(string childKey)
	{
		if (childKey is null)
			throw new ArgumentNullException(nameof(childKey));
		_childKeys.Add(childKey);
		return this;
	}

	internal string[]? ParentKeys => _parentKeys.Count > 0 ? _parentKeys.ToArray() : null;
	internal string[]? ChildKeys => _childKeys.Count > 0 ? _childKeys.ToArray() : null;
}

/// <summary>
/// Helper class to start dependency builders.
/// </summary>
public static class DependsOn
{
	/// <summary>
	/// Start a <see cref="DependencyBuilder"/> specifying one or more parent keys this entry depends on.
	/// </summary>
	/// <param name="keys">The parent cache keys this entry depends on.</param>
	public static DependencyBuilder Keys(params string[] keys) => new DependencyBuilder().Keys(keys);

	/// <summary>
	/// Start a <see cref="DependencyBuilder"/> specifying that the current cache entry is the parent of the specified child key.
	/// </summary>
	/// <param name="childKey">A cache key that depends on the current entry.</param>
	public static DependencyBuilder ParentOf(string childKey) => new DependencyBuilder().ParentOf(childKey);
}
