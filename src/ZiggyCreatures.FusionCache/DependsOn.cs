namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Helper static class used to easily construct <see cref="DependencyBuilder"/> for expressing dependencies.
/// </summary>
public static class DependsOn
{
    /// <summary>
    /// Create a dependency builder for the specified parent keys this entry depends on.
    /// </summary>
    public static DependencyBuilder Keys(params string[] keys)
    {
        return new DependencyBuilder().Keys(keys);
    }

    /// <summary>
    /// Create a dependency builder to declare the current entry is the parent of the specified child key.
    /// </summary>
    public static DependencyBuilder ParentOf(string childKey)
    {
        return new DependencyBuilder().ParentOf(childKey);
    }
}
