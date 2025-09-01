using System;
using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// A small fluent builder to define dependencies for a cache entry.
/// </summary>
public sealed class DependencyBuilder
{
   private readonly List<string> _parentKeys = new();
   private readonly List<string> _childKeys = new();

   /// <summary>
   /// Declare this entry depends on the specified parent keys. These will be added as directed edges parent → this key.
   /// </summary>
   /// <param name="keys">One or more parent keys that this entry depends on.</param>
   public DependencyBuilder Keys(params string[] keys)
   {
       if (keys is not null)
       {
           _parentKeys.AddRange(keys);
       }
       return this;
   }

   /// <summary>
   /// Declare that this entry is the parent of an existing child key. No invalidation is performed; this simply adds the dependency edge.
   /// </summary>
   /// <param name="childKey">The child key to add as dependent on this parent.</param>
   public DependencyBuilder ParentOf(string childKey)
   {
       if (childKey is null)
           throw new ArgumentNullException(nameof(childKey));
       _childKeys.Add(childKey);
       return this;
   }

   internal string[] ParentKeys => _parentKeys.ToArray();
   internal string[] ChildKeys => _childKeys.ToArray();
}
