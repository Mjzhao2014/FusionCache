namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Options controlling cascading invalidation of entries when dependencies change.
/// </summary>
public sealed class CascadeOptions
{
	/// <summary>
	/// If <c>true</c> (default), cascading invalidation will also remove dependent entries from any configured distributed cache (L2).
	/// If <c>false</c>, only the in-process (L1) entries will be invalidated and distributed cache entries will be left untouched.
	/// </summary>
	public bool CascadeToL2 { get; set; } = true;

	/// <summary>
	/// The maximum depth of cascading to apply when invalidating dependent entries.
	/// Defaults to 4 to guard against infinite recursion in case of erroneous graphs.
	/// </summary>
	public int MaxCascadeDepth { get; set; } = 4;

	internal CascadeOptions Duplicate()
	{
		return new CascadeOptions
		{
			CascadeToL2 = CascadeToL2,
			MaxCascadeDepth = MaxCascadeDepth,
		};
	}
}
