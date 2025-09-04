namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Convenience factory for constructing dependency declarations between cache keys.
/// Use when setting up <see cref="FusionCacheEntryOptions"/>.
/// </summary>
public static class DependsOn
{
   /// <summary>
   /// Build a declaration that the target entry depends on the specified set of parent keys.
   /// </summary>
   public static DependencyBuilder Keys(params string[] keys)
   {
       return new DependencyBuilder().Keys(keys);
   }

   /// <summary>
   /// Build a declaration that the target entry should be considered a parent of the given child key.
   /// This can be used to declare strong dependency edges to an already existing child entry.
   /// </summary>
   public static DependencyBuilder ParentOf(string childKey)
   {
       return new DependencyBuilder().ParentOf(childKey);
   }
}
