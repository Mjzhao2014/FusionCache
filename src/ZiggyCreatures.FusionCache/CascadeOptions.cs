using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Options controlling how dependency cascades are handled within FusionCache.
/// </summary>
	public sealed class CascadeOptions
	{
	   /// <summary>
	   /// When <c>true</c> (default), any invalidation cascaded from a parent will also remove the child entry from L2 (distributed cache) if configured.
	   /// </summary>
	   public bool CascadeToL2 { get; set; } = true;

	   /// <summary>
	   /// Maximum depth of cascading dependency traversal before aborting to safeguard against cycles.
	   /// </summary>
	   public int MaxCascadeDepth { get; set; } = 4;

	   /// <summary>
	   /// Maximum total number of child entries traversed in a cascade before aborting to safeguard against runaway fanout.
	   /// </summary>
	   public int MaxCascadeFanout { get; set; } = 5000;

	   /// <summary>
	   /// When <c>true</c>, if a child entry has a factory, the factory will be scheduled for refresh instead of simply removing the entry on cascade.
	   /// </summary>
	   public bool NotifyChildFactory { get; set; } = false;

	   /// <summary>
	   /// Optional equality comparer to detect whether a parent value has effectively changed: allows suppression of cascades if a parent is set to an equal value.
	   /// </summary>
	   public IEqualityComparer<object?>? ParentValueComparer { get; set; }
	}
