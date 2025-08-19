using System.Collections.Concurrent;

namespace ZiggyCreatures.Caching.Fusion.Eviction;

/// <summary>
/// Size-based eviction policy that prioritizes evicting larger entries first.
/// This helps maximize the number of entries that can fit in the cache.
/// Tracks entry sizes and evicts largest entries when space is needed.
/// </summary>
public sealed class SizeBasedEvictionPolicy : FusionCacheEvictionPolicyBase
{
	private readonly ConcurrentDictionary<string, long> _entrySizes;

	/// <summary>
	/// Creates a new size-based eviction policy with the specified configuration.
	/// </summary>
	/// <param name="config">The eviction policy configuration</param>
	public SizeBasedEvictionPolicy(FusionCacheEvictionPolicyConfig config)
		: base(config)
	{
		_entrySizes = new ConcurrentDictionary<string, long>();
	}

	/// <inheritdoc />
	public override string Name => "Size-Based";

	/// <inheritdoc />
	public override void OnEntryAccessed(string key, IFusionCacheEntryInfo entry)
	{
		ThrowIfDisposed();
		// Size-based policy doesn't change behavior on access
	}

	/// <inheritdoc />
	public override void OnEntrySet(string key, IFusionCacheEntryInfo entry)
	{
		ThrowIfDisposed();
		var size = GetEntrySize(entry);
		_entrySizes.AddOrUpdate(key, size, (k, existing) => size);
	}

	/// <inheritdoc />
	public override void OnEntryRemoved(string key)
	{
		ThrowIfDisposed();
		_entrySizes.TryRemove(key, out _);
	}

	/// <inheritdoc />
	public override IReadOnlyList<string> SelectEntriesForEviction(
		IReadOnlyDictionary<string, IFusionCacheEntryInfo> currentEntries,
		int requestedEvictionCount)
	{
		ThrowIfDisposed();

		var candidates = new List<(string Key, long Size)>();

		// Collect size data for all current entries
		foreach (var kvp in currentEntries)
		{
			long size;
			if (_entrySizes.TryGetValue(kvp.Key, out var trackedSize))
			{
				size = trackedSize;
			}
			else
			{
				// Entry not tracked, calculate size now
				size = GetEntrySize(kvp.Value);
				_entrySizes.TryAdd(kvp.Key, size);
			}
			candidates.Add((kvp.Key, size));
		}

		// Sort by size (descending) - evict largest entries first
		candidates.Sort((a, b) => b.Size.CompareTo(a.Size));

		// Take the largest entries for eviction
		return candidates
			.Take(requestedEvictionCount)
			.Select(c => c.Key)
			.ToList();
	}

	/// <inheritdoc />
	public override void Reset()
	{
		ThrowIfDisposed();
		_entrySizes.Clear();
	}

	/// <inheritdoc />
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_entrySizes.Clear();
		}
		base.Dispose(disposing);
	}
}