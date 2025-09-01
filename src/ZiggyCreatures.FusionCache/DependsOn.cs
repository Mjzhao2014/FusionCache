namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Static helper to start building a dependency declaration for a cache entry.
/// </summary>
public static class DependsOn
{
   /// <summary>
   /// Start a dependency declaration using one or more parent cache keys.
   /// </summary>
   public static DependencyBuilder Keys(params string[] keys)
   {
       return new DependencyBuilder().Keys(keys);
   }

   /// <summary>
   /// Start a dependency declaration using one or more parent tags.
   /// </summary>
   public static DependencyBuilder Tags(params string[] tags)
   {
       return new DependencyBuilder().Tags(tags);
   }
}
