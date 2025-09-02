using System;
using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Options controlling how dependency cascades are processed when invalidating dependent entries.
/// </summary>
	public sealed class CascadeOptions
	{
		/// <summary>
		/// If <see langword="true"/> and a distributed cache (L2) is configured, dependent children will be removed from it
		/// when a parent is invalidated. Defaults to <c>true</c>.
		/// </summary>
		public bool CascadeToL2 { get; set; } = true;

		/// <summary>
		/// Maximum depth of dependency chains explored when cascading invalidations. Defaults to 4 to prevent runaway graphs.
		/// A depth of 0 means no cascading.
		/// </summary>
		public int MaxCascadeDepth { get; set; } = 4;

		/// <summary>
		/// Maximum total number of items that will be invalidated via cascades before giving up.
		/// Defaults to 5000 to prevent runaway fan-out.
		/// </summary>
		public int MaxCascadeFanout { get; set; } = 5000;

		/// <summary>
		/// If <see langword="true"/>, dependent children will be expired (so factories will be triggered on next access) instead of removed outright.
		/// </summary>
		public bool NotifyChildFactory { get; set; } = false;

		/// <summary>
		/// Optional comparer to determine if a parent's value actually changed. If specified, a cascade will be skipped when a parent
		/// is updated with a new value that compares equal to the previous value.
		/// </summary>
		public IEqualityComparer<object?>? ParentValueComparer { get; set; }
	}
