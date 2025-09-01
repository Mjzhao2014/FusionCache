using System;
using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Options controlling how dependency cascades are processed.
/// </summary>
public sealed class CascadeOptions
{
	/// <summary>
	/// When <c>true</c>, dependent children entries will be removed from any configured distributed cache when their parent is invalidated.
	/// Defaults to <c>true</c>.
	/// </summary>
	public bool CascadeToL2 { get; set; } = true;
	/// <summary>
	/// The maximum depth to traverse when cascading an invalidation.
	/// Defaults to 4.
	/// </summary>
	public int MaxCascadeDepth { get; set; } = 4;
	/// <summary>
	/// The maximum number of child entries that will be invalidated in a single cascade.
	/// Defaults to 5000.
	/// </summary>
	public int MaxCascadeFanout { get; set; } = 5000;
	/// <summary>
	/// When <c>true</c>, instead of simply removing children they will be treated as expired so that a refresh factory is triggered.
	/// </summary>
	public bool NotifyChildFactory { get; set; } = false;
	/// <summary>
	/// An optional comparer to determine if a parent value has changed, to avoid unnecessary cascades.
	/// </summary>
	public IEqualityComparer<object?>? ParentValueComparer { get; set; }
}
