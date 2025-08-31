using System.Diagnostics;

namespace ZiggyCreatures.Caching.Fusion.Dependencies;

/// <summary>
/// Options for controlling cascade invalidation behavior.
/// </summary>
[DebuggerDisplay("CascadeToL2: {CascadeToL2}, MaxDepth: {MaxCascadeDepth}, MaxFanout: {MaxCascadeFanout}")]
public sealed class CascadeOptions
{
	/// <summary>
	/// Whether to cascade invalidation to L2 distributed cache. Default is true.
	/// </summary>
	public bool CascadeToL2 { get; set; } = true;

	/// <summary>
	/// Maximum depth for cascade invalidation to prevent infinite loops. Default is 4.
	/// </summary>
	public int MaxCascadeDepth { get; set; } = 4;

	/// <summary>
	/// Maximum number of entries that can be invalidated in a single cascade operation. Default is 5000.
	/// </summary>
	public int MaxCascadeFanout { get; set; } = 5000;

	/// <summary>
	/// Whether to notify child factories for refresh instead of plain removal. Default is false.
	/// When true, dependent entries will be refreshed rather than just invalidated.
	/// </summary>
	public bool NotifyChildFactory { get; set; } = false;

	/// <summary>
	/// Optional comparer to optimize no-op refresh scenarios by comparing parent values.
	/// If provided and the parent value hasn't changed, dependent entries won't be invalidated.
	/// </summary>
	public IEqualityComparer<object?>? ParentValueComparer { get; set; }
}