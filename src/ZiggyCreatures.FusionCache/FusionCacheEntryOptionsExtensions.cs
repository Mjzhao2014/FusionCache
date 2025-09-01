namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Extension methods for <see cref="FusionCacheEntryOptions"/>.
/// </summary>
public static class FusionCacheEntryOptionsExtensions
{
   /// <summary>
   /// Attach a set of declared dependencies to this entry options instance.
   /// </summary>
   /// <param name="opts">The entry options to mutate.</param>
   /// <param name="deps">A dependency builder describing the parent keys/tags for this entry.</param>
   /// <returns>The same options instance for chaining.</returns>
   public static FusionCacheEntryOptions WithDependencies(this FusionCacheEntryOptions opts, DependencyBuilder deps)
   {
       if (deps is null)
           return opts;
       opts.DependencyKeys = deps.GetKeysOrDefault();
       opts.DependencyTags = deps.GetTagsOrDefault();
       return opts;
   }
}
