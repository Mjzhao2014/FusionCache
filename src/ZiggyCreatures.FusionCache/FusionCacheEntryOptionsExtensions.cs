namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Extension methods for <see cref="FusionCacheEntryOptions"/> for configuring dependencies.
/// </summary>
public static class FusionCacheEntryOptionsExtensions
{
   /// <summary>
   /// Attach dependency metadata to this options instance so the resulting entry will update the dependency graph when set.
   /// </summary>
   /// <param name="opts">The options being configured.</param>
   /// <param name="deps">A builder describing parent/child dependencies.</param>
   public static FusionCacheEntryOptions WithDependencies(this FusionCacheEntryOptions opts, DependencyBuilder deps)
   {
       opts.Dependencies = deps;
       return opts;
   }
}
