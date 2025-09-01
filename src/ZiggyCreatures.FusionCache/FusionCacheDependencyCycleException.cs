using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Exception thrown when adding a dependency edge would result in a cycle.
/// </summary>
public class FusionCacheDependencyCycleException
	: Exception
{
	public FusionCacheDependencyCycleException(string? message)
		: base(message)
	{
	}
}
