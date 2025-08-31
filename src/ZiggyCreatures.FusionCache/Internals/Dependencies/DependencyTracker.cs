using System.Collections.Concurrent;

namespace ZiggyCreatures.Caching.Fusion.Internals.Dependencies;

internal sealed class DependencyTracker
{
	private readonly ConcurrentDictionary<string, HashSet<string>> _dependents = new();
	private readonly ConcurrentDictionary<string, HashSet<string>> _dependencies = new();
	private readonly object _syncLock = new();

	public void AddDependency(string dependentKey, string dependencyKey)
	{
		if (string.IsNullOrEmpty(dependentKey) || string.IsNullOrEmpty(dependencyKey))
			return;

		lock (_syncLock)
		{
			_dependents.AddOrUpdate(
				dependencyKey,
				(key) => new HashSet<string> { dependentKey },
				(key, existing) =>
				{
					existing.Add(dependentKey);
					return existing;
				}
			);

			_dependencies.AddOrUpdate(
				dependentKey,
				(key) => new HashSet<string> { dependencyKey },
				(key, existing) =>
				{
					existing.Add(dependencyKey);
					return existing;
				}
			);
		}
	}

	public void AddDependencies(string dependentKey, IEnumerable<string>? dependencies)
	{
		if (string.IsNullOrEmpty(dependentKey) || dependencies == null)
			return;

		foreach (var dependency in dependencies)
		{
			AddDependency(dependentKey, dependency);
		}
	}

	public HashSet<string>? GetDependents(string dependencyKey)
	{
		if (string.IsNullOrEmpty(dependencyKey))
			return null;

		lock (_syncLock)
		{
			if (_dependents.TryGetValue(dependencyKey, out var dependents))
			{
				return new HashSet<string>(dependents);
			}
		}

		return null;
	}

	public void RemoveDependency(string dependentKey, string dependencyKey)
	{
		if (string.IsNullOrEmpty(dependentKey) || string.IsNullOrEmpty(dependencyKey))
			return;

		lock (_syncLock)
		{
			if (_dependents.TryGetValue(dependencyKey, out var dependents))
			{
				dependents.Remove(dependentKey);
				if (dependents.Count == 0)
				{
					_dependents.TryRemove(dependencyKey, out _);
				}
			}

			if (_dependencies.TryGetValue(dependentKey, out var dependencies))
			{
				dependencies.Remove(dependencyKey);
				if (dependencies.Count == 0)
				{
					_dependencies.TryRemove(dependentKey, out _);
				}
			}
		}
	}

	public void RemoveAllDependencies(string dependentKey)
	{
		if (string.IsNullOrEmpty(dependentKey))
			return;

		lock (_syncLock)
		{
			if (_dependencies.TryRemove(dependentKey, out var dependencies))
			{
				foreach (var dependency in dependencies)
				{
					if (_dependents.TryGetValue(dependency, out var dependents))
					{
						dependents.Remove(dependentKey);
						if (dependents.Count == 0)
						{
							_dependents.TryRemove(dependency, out _);
						}
					}
				}
			}
		}
	}

	public void Clear()
	{
		lock (_syncLock)
		{
			_dependents.Clear();
			_dependencies.Clear();
		}
	}
}