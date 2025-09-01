using System;
using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Builder class used to fluently declare dependencies for a cache entry when setting it.
/// </summary>
public sealed class DependencyBuilder
{
   private readonly List<string> _keys = new();
   private readonly List<string> _tags = new();

   /// <summary>
   /// Declare that the entry depends on the specified cache keys.
   /// <br/>
   /// Multiple calls to <see cref="Keys"/> will append to the current list.
   /// </summary>
   /// <param name="keys">The cache keys that are parents of this entry.</param>
   /// <returns>The builder instance for chaining.</returns>
   public DependencyBuilder Keys(params string[] keys)
   {
       if (keys is not null && keys.Length > 0)
           _keys.AddRange(keys);
       return this;
   }

   /// <summary>
   /// Declare that the entry depends on the specified tags.
   /// <br/>
   /// Multiple calls to <see cref="Tags"/> will append to the current list.
   /// </summary>
   /// <param name="tags">The tag identifiers that are parents of this entry.</param>
   /// <returns>The builder instance for chaining.</returns>
   public DependencyBuilder Tags(params string[] tags)
   {
       if (tags is not null && tags.Length > 0)
           _tags.AddRange(tags);
       return this;
   }

   internal string[]? GetKeysOrDefault()
   {
       return _keys.Count == 0 ? null : _keys.ToArray();
   }

   internal string[]? GetTagsOrDefault()
   {
       return _tags.Count == 0 ? null : _tags.ToArray();
   }
}
