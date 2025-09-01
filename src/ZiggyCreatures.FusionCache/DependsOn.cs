namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Helper to fluently build dependency information for an entry.
/// </summary>
public static class DependsOn
{
   /// <summary>
   /// Start a dependency builder declaring the specified parent cache keys.
   /// </summary>
   public static DependencyBuilder Keys(params string[] keys)
   {
       return new DependencyBuilder().Keys(keys);
   }

   /// <summary>
   /// Start a dependency builder declaring this entry is the parent of the specified existing child key.
   /// </summary>
   public static DependencyBuilder ParentOf(string childKey)
   {
       return new DependencyBuilder().ParentOf(childKey);
   }
}
