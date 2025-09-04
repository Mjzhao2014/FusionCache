namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Options to control cascade invalidation when parents are changed.
/// </summary>
public sealed class CascadeOptions
{
	/// <summary>
	/// When true, and an entry with dependents is invalidated, child entries will also be
	/// removed from any configured distributed cache.
	/// </summary>
	public bool CascadeToL2 { get; set; } = true;

	/// <summary>
	/// The maximum depth of dependency links to follow when cascading invalidations.
	/// </summary>
	public int MaxCascadeDepth { get; set; } = 4;

	/// <summary>
	/// The maximum total number of dependency edges to follow when cascading invalidations.
	/// </summary>
	public int MaxCascadeFanout { get; set; } = 5000;
}
