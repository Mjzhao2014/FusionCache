using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Dependencies;

namespace ZiggyCreatures.Caching.Fusion.Internals.Dependencies;

/// <summary>
/// Tracks dependency relationships between cache entries for cascade invalidation.
/// </summary>
internal sealed class DependencyTracker
{
	private readonly ConcurrentDictionary<string, ConcurrentHashSet<string>> _keyToChildren = new();
	private readonly ConcurrentDictionary<string, ConcurrentHashSet<string>> _tagToChildren = new();
	private readonly object _lock = new();
	private readonly ILogger? _logger;
	private readonly string _cacheName;
	private readonly string _instanceId;

	public DependencyTracker(ILogger? logger, string cacheName, string instanceId)
	{
		_logger = logger;
		_cacheName = cacheName;
		_instanceId = instanceId;
	}

	/// <summary>
	/// Registers dependencies for a cache entry.
	/// </summary>
	/// <param name="childKey">The cache key of the dependent entry.</param>
	/// <param name="dependencies">The dependency information.</param>
	public void RegisterDependencies(string childKey, DependencyInfo dependencies)
	{
		if (dependencies == null || !dependencies.HasDependencies)
			return;

		lock (_lock)
		{
			// Remove any existing dependencies for this child
			RemoveDependenciesInternal(childKey);

			// Register new dependencies
			foreach (var parentKey in dependencies.ParentKeys)
			{
				var children = _keyToChildren.GetOrAdd(parentKey, _ => new ConcurrentHashSet<string>());
				children.Add(childKey);
			}

			foreach (var parentTag in dependencies.ParentTags)
			{
				var children = _tagToChildren.GetOrAdd(parentTag, _ => new ConcurrentHashSet<string>());
				children.Add(childKey);
			}

			if (_logger?.IsEnabled(LogLevel.Debug) == true)
			{
				_logger.LogDebug("FUSION [N={CacheName} I={CacheInstanceId}]: Registered dependencies for key {CacheKey}: {ParentKeys} keys, {ParentTags} tags",
					_cacheName, _instanceId, childKey, dependencies.ParentKeys.Count, dependencies.ParentTags.Count);
			}
		}
	}

	/// <summary>
	/// Removes all dependencies for a cache entry.
	/// </summary>
	/// <param name="childKey">The cache key to remove dependencies for.</param>
	public void RemoveDependencies(string childKey)
	{
		lock (_lock)
		{
			RemoveDependenciesInternal(childKey);
		}
	}

	/// <summary>
	/// Gets all dependent children for a parent key.
	/// </summary>
	/// <param name="parentKey">The parent key.</param>
	/// <returns>Collection of dependent child keys.</returns>
	public IReadOnlyCollection<string> GetDependentChildren(string parentKey)
	{
		if (_keyToChildren.TryGetValue(parentKey, out var children))
		{
			return children.ToArray();
		}
		return Array.Empty<string>();
	}

	/// <summary>
	/// Gets all dependent children for a parent tag.
	/// </summary>
	/// <param name="parentTag">The parent tag.</param>
	/// <returns>Collection of dependent child keys.</returns>
	public IReadOnlyCollection<string> GetDependentChildrenByTag(string parentTag)
	{
		if (_tagToChildren.TryGetValue(parentTag, out var children))
		{
			return children.ToArray();
		}
		return Array.Empty<string>();
	}

	/// <summary>
	/// Gets all dependent children for multiple parent tags.
	/// </summary>
	/// <param name="parentTags">The parent tags.</param>
	/// <returns>Collection of dependent child keys.</returns>
	public IReadOnlyCollection<string> GetDependentChildrenByTags(IEnumerable<string> parentTags)
	{
		var allChildren = new HashSet<string>();

		foreach (var parentTag in parentTags)
		{
			if (_tagToChildren.TryGetValue(parentTag, out var children))
			{
				foreach (var child in children.ToArray())
				{
					allChildren.Add(child);
				}
			}
		}

		return allChildren.ToArray();
	}

	private void RemoveDependenciesInternal(string childKey)
	{
		// Remove from key dependencies
		foreach (var kvp in _keyToChildren.ToArray())
		{
			if (kvp.Value.Remove(childKey) && kvp.Value.Count == 0)
			{
				_keyToChildren.TryRemove(kvp.Key, out _);
			}
		}

		// Remove from tag dependencies
		foreach (var kvp in _tagToChildren.ToArray())
		{
			if (kvp.Value.Remove(childKey) && kvp.Value.Count == 0)
			{
				_tagToChildren.TryRemove(kvp.Key, out _);
			}
		}
	}

	/// <summary>
	/// Gets diagnostic information about the current state of dependencies.
	/// </summary>
	/// <returns>Diagnostic information.</returns>
	public (int KeyDependencies, int TagDependencies, int TotalChildren) GetDiagnosticInfo()
	{
		var keyDeps = _keyToChildren.Count;
		var tagDeps = _tagToChildren.Count;
		var totalChildren = _keyToChildren.Values.Sum(v => v.Count) + _tagToChildren.Values.Sum(v => v.Count);
		return (keyDeps, tagDeps, totalChildren);
	}
}

/// <summary>
/// Thread-safe hash set implementation.
/// </summary>
internal sealed class ConcurrentHashSet<T>
{
	private readonly HashSet<T> _hashSet = new();
	private readonly object _lock = new();

	public int Count
	{
		get
		{
			lock (_lock)
			{
				return _hashSet.Count;
			}
		}
	}

	public bool Add(T item)
	{
		lock (_lock)
		{
			return _hashSet.Add(item);
		}
	}

	public bool Remove(T item)
	{
		lock (_lock)
		{
			return _hashSet.Remove(item);
		}
	}

	public bool Contains(T item)
	{
		lock (_lock)
		{
			return _hashSet.Contains(item);
		}
	}

	public T[] ToArray()
	{
		lock (_lock)
		{
			return _hashSet.ToArray();
		}
	}
}