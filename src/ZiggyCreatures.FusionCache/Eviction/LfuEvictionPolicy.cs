using System.Collections.Concurrent;

namespace ZiggyCreatures.Caching.Fusion.Eviction;

/// <summary>
/// Least Frequently Used (LFU) eviction policy.
/// Evicts entries that have been accessed the fewest number of times.
/// Uses approximate counting to maintain performance.
/// </summary>
public sealed class LfuEvictionPolicy : FusionCacheEvictionPolicyBase
{
	private readonly ConcurrentDictionary<string, LfuEntry> _entryStats;

	/// <summary>
	/// Creates a new LFU eviction policy with the specified configuration.
	/// </summary>
	/// <param name="config">The eviction policy configuration</param>
	public LfuEvictionPolicy(FusionCacheEvictionPolicyConfig config)
		: base(config)
	{
		_entryStats = new ConcurrentDictionary<string, LfuEntry>();
	}

	/// <inheritdoc />
	public override string Name => "LFU";

	/// <inheritdoc />
	public override void OnEntryAccessed(string key, IFusionCacheEntryInfo entry)
	{
		ThrowIfDisposed();
		IncrementFrequency(key);
	}

	/// <inheritdoc />
	public override void OnEntrySet(string key, IFusionCacheEntryInfo entry)
	{
		ThrowIfDisposed();
		
		// For new entries, start with frequency of 1
		// For existing entries, increment frequency
		_entryStats.AddOrUpdate(key, 
			new LfuEntry(1, DateTimeOffset.UtcNow.Ticks),
			(k, existing) => new LfuEntry(existing.Frequency + 1, DateTimeOffset.UtcNow.Ticks));
	}

	/// <inheritdoc />
	public override void OnEntryRemoved(string key)
	{
		ThrowIfDisposed();
		_entryStats.TryRemove(key, out _);
	}

	/// <inheritdoc />
	public override IReadOnlyList<string> SelectEntriesForEviction(
		IReadOnlyDictionary<string, IFusionCacheEntryInfo> currentEntries,
		int requestedEvictionCount)
	{
		ThrowIfDisposed();

		var candidates = new List<(string Key, long Frequency, long LastAccess)>();

		// Collect frequency data for all current entries
		foreach (var kvp in currentEntries)
		{
			if (_entryStats.TryGetValue(kvp.Key, out var stats))
			{
				candidates.Add((kvp.Key, stats.Frequency, stats.LastAccessTime));
			}
			else
			{
				// Entry not tracked, treat as never accessed
				candidates.Add((kvp.Key, 0, 0));
			}
		}

		// Sort by frequency (ascending), then by last access time (ascending) as tiebreaker
		candidates.Sort((a, b) =>
		{
			var freqComparison = a.Frequency.CompareTo(b.Frequency);
			return freqComparison != 0 ? freqComparison : a.LastAccess.CompareTo(b.LastAccess);
		});

		// Take the least frequently used entries
		return candidates
			.Take(requestedEvictionCount)
			.Select(c => c.Key)
			.ToList();
	}

	/// <inheritdoc />
	public override void Reset()
	{
		ThrowIfDisposed();
		_entryStats.Clear();
	}

	private void IncrementFrequency(string key)
	{
		_entryStats.AddOrUpdate(key,
			new LfuEntry(1, DateTimeOffset.UtcNow.Ticks),
			(k, existing) => new LfuEntry(existing.Frequency + 1, DateTimeOffset.UtcNow.Ticks));
	}

	/// <inheritdoc />
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_entryStats.Clear();
		}
		base.Dispose(disposing);
	}

	private readonly struct LfuEntry
	{
		public readonly long Frequency;
		public readonly long LastAccessTime;

		public LfuEntry(long frequency, long lastAccessTime)
		{
			Frequency = frequency;
			LastAccessTime = lastAccessTime;
		}
	}
}