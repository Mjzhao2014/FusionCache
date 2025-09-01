using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Thrown when attempting to add a dependency edge that would create a cycle.
/// </summary>
public class FusionCacheDependencyCycleException : Exception
{
    public FusionCacheDependencyCycleException(string? message)
        : base(message)
    {
    }
}
