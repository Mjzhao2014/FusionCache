using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Exception thrown when attempting to add a cache dependency edge that would create a cycle.
/// This indicates an invalid dependency graph.
/// </summary>
public class FusionCacheDependencyCycleException : Exception
{
   public FusionCacheDependencyCycleException(string? message) : base(message)
   {
   }
}
