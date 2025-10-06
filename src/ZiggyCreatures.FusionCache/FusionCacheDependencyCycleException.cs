using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Exception thrown if attempting to register a dependency relationship that would create a cycle in the dependency graph.
/// Cycles are not supported because cascading invalidation relies on a directed acyclic graph.
/// </summary>
public class FusionCacheDependencyCycleException : Exception
{
	public FusionCacheDependencyCycleException(string? message)
		: base(message)
	{
	}
}
