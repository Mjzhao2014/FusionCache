using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Options related to cascading invalidation of dependent entries.
/// </summary>
public sealed class CascadeOptions
{
    /// <summary>
    /// When invalidating a parent, also remove dependent entries from the distributed cache (L2).
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool CascadeToL2 { get; init; } = true;

    /// <summary>
    /// Maximum recursion depth when cascading invalidation along dependency edges.
    /// </summary>
    public int MaxCascadeDepth { get; init; } = 4;

    /// <summary>
    /// Maximum number of descendant entries to invalidate in a single cascade operation.
    /// </summary>
    public int MaxCascadeFanout { get; init; } = 5000;

    /// <summary>
    /// If <c>true</c>, instead of removing dependent entries, enqueue their factory for refresh.
    /// Not implemented currently.
    /// </summary>
    public bool NotifyChildFactory { get; init; } = false;

    /// <summary>
    /// Optional comparer used to detect no-op parent changes: if the parent value is equal before/after,
    /// cascading invalidation may be skipped.
    /// </summary>
    public IEqualityComparer<object?>? ParentValueComparer { get; init; } = null;
}
