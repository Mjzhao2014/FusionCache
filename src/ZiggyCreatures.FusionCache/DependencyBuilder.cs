using System;
using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// A builder to construct dependency declarations between cache keys.
/// This is created via helpers on <see cref="DependsOn"/>, and can specify
/// either parent keys that the current entry depends on, or children that
/// the current entry should be considered a parent of.
/// </summary>
public sealed class DependencyBuilder
{
   private readonly HashSet<string> _parentKeys = new();
   private readonly HashSet<string> _childKeys = new();

   /// <summary>
   /// Adds one or more parent keys to this builder. When attached to an entry,
   /// the entry is considered a child of these parent keys.
   /// </summary>
   public DependencyBuilder Keys(params string[] keys)
   {
       if (keys is null) return this;
       foreach (var k in keys)
       {
           if (k is not null)
               _parentKeys.Add(k);
       }
       return this;
   }

   /// <summary>
   /// Adds a child key to this builder. When attached to an entry, the entry
   /// is considered a parent of this child key.
   /// </summary>
   public DependencyBuilder ParentOf(string childKey)
   {
       if (childKey is not null)
           _childKeys.Add(childKey);
       return this;
   }

   internal IReadOnlyCollection<string> ParentKeys => _parentKeys;
   internal IReadOnlyCollection<string> ChildKeys => _childKeys;
}
