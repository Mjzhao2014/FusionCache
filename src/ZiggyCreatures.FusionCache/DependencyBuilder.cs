using System;
using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Builder used to declare parent/child dependency relationships between cache keys.
/// </summary>
public sealed class DependencyBuilder
{
    internal readonly List<string> ParentKeys = new();
    internal readonly List<string> ChildKeys = new();

    /// <summary>
    /// Add one or more keys that the current entry depends on.
    /// </summary>
    /// <param name="keys">The parent keys.</param>
    /// <returns>This builder.</returns>
    public DependencyBuilder Keys(params string[] keys)
    {
        if (keys is null)
            throw new ArgumentNullException(nameof(keys));
        ParentKeys.AddRange(keys);
        return this;
    }

    /// <summary>
    /// Declare the current key as being the parent of the specified child key.
    /// </summary>
    /// <param name="childKey">The child key.</param>
    /// <returns>This builder.</returns>
    public DependencyBuilder ParentOf(string childKey)
    {
        if (childKey is null)
            throw new ArgumentNullException(nameof(childKey));
        ChildKeys.Add(childKey);
        return this;
    }
}
