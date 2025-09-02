using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Thrown when adding a dependency edge would introduce a cycle in the dependency graph.
/// </summary>
public class FusionCacheDependencyCycleException : Exception
{
	public FusionCacheDependencyCycleException(string? message)
		: base(message)
	{
	}
}
