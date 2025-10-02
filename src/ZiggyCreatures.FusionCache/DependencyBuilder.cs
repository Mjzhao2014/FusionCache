using System;
using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Builder used to declare dependency relationships between cache entries when setting an entry.
/// </summary>
public sealed class DependencyBuilder
{
	private readonly HashSet<string> _parentKeys = new();
	private readonly HashSet<string> _childKeys = new();
	private bool _hasParentDeclarations;
	private bool _hasChildDeclarations;

	/// <summary>
	/// Declare that the current entry depends on the given parent keys.
	/// </summary>
	/// <param name="keys">One or more parent cache keys this entry depends on.</param>
	/// <returns>The builder for fluent chaining.</returns>
	public DependencyBuilder Keys(params string[] keys)
	{
		if (keys is null)
			return this;
		_hasParentDeclarations = true;
		foreach (var key in keys)
		{
			if (string.IsNullOrEmpty(key))
				continue;
			_parentKeys.Add(key);
		}
		return this;
	}

	/// <summary>
	/// Declare that the current entry is a parent of the specified child key.
	/// This allows adding a dependency edge for an existing child entry without overwriting it.
	/// </summary>
	/// <param name="childKey">The downstream child key for which this entry is a parent.</param>
	/// <returns>The builder for fluent chaining.</returns>
	public DependencyBuilder ParentOf(string childKey)
	{
		_hasChildDeclarations = true;
		if (!string.IsNullOrEmpty(childKey))
		{
			_childKeys.Add(childKey);
		}
		return this;
	}

	internal IReadOnlyCollection<string> ParentKeys => _parentKeys;
	internal IReadOnlyCollection<string> ChildKeys => _childKeys;
	internal bool HasParentDeclarations => _hasParentDeclarations;
	internal bool HasChildDeclarations => _hasChildDeclarations;
}

/// <summary>
/// Static helpers to start building dependency declarations.
/// </summary>
public static class DependsOn
{
	public static DependencyBuilder Keys(params string[] keys) => new DependencyBuilder().Keys(keys);
	public static DependencyBuilder ParentOf(string childKey) => new DependencyBuilder().ParentOf(childKey);
}
