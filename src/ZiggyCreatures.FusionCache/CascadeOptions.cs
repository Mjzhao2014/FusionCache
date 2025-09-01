using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Global configuration controlling how dependency cascades operate.
/// </summary>
public sealed class CascadeOptions
{
   /// <summary>
   /// If set to <see langword="true"/> (default), when invalidating a parent key all children/descendants
   /// will also be removed from any configured <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>.
   /// </summary>
   public bool CascadeToL2 { get; set; } = true;

   /// <summary>
   /// Maximum depth to descend when traversing dependency edges during a cascade invalidation.
   /// Defaults to 4.
   /// </summary>
   public int MaxCascadeDepth { get; set; } = 4;

   /// <summary>
   /// Maximum number of entries to invalidate when cascading from a parent.
   /// Defaults to 5000.
   /// </summary>
   public int MaxCascadeFanout { get; set; } = 5000;

   /// <summary>
   /// If <see langword="true"/> and a child factory is registered, invalidation will notify the child factory to refresh
   /// rather than simply removing the child entry.
   /// Currently ignored.
   /// </summary>
   public bool NotifyChildFactory { get; set; } = false;

   /// <summary>
   /// If provided, used to compare parent values to avoid cascading when setting the same value.
   /// Currently ignored.
   /// </summary>
	public IEqualityComparer<object?>? ParentValueComparer { get; set; }

	/// <summary>
	/// Creates a copy of this options object.
	/// </summary>
	public CascadeOptions Duplicate()
	{
		return new CascadeOptions
		{
			CascadeToL2 = CascadeToL2,
			MaxCascadeDepth = MaxCascadeDepth,
			MaxCascadeFanout = MaxCascadeFanout,
			NotifyChildFactory = NotifyChildFactory,
			ParentValueComparer = ParentValueComparer,
		};
	}
}
