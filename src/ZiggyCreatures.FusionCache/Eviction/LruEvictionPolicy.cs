using System.Collections.Concurrent;

namespace ZiggyCreatures.Caching.Fusion.Eviction;

/// <summary>
/// Least Recently Used (LRU) eviction policy.
/// Evicts entries that haven't been accessed for the longest time.
/// Maintains O(1) access time for tracking usage.
/// </summary>
public sealed class LruEvictionPolicy : FusionCacheEvictionPolicyBase
{
	private readonly ConcurrentDictionary<string, LinkedListNode<LruEntry>> _entryNodes;
	private readonly LinkedList<LruEntry> _accessOrder;
	private readonly object _accessOrderLock = new object();
	private long _accessCounter;

	/// <summary>
	/// Creates a new LRU eviction policy with the specified configuration.
	/// </summary>
	/// <param name="config">The eviction policy configuration</param>
	public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
		: base(config)
	{
		_entryNodes = new ConcurrentDictionary<string, LinkedListNode<LruEntry>>();
		_accessOrder = new LinkedList<LruEntry>();
	}

	/// <inheritdoc />
	public override string Name => "LRU";

	/// <inheritdoc />
	public override void OnEntryAccessed(string key, IFusionCacheEntryInfo entry)
	{
		ThrowIfDisposed();
		UpdateAccessOrder(key);
	}

	/// <inheritdoc />
	public override void OnEntrySet(string key, IFusionCacheEntryInfo entry)
	{
		ThrowIfDisposed();
		UpdateAccessOrder(key);
	}

	/// <inheritdoc />
	public override void OnEntryRemoved(string key)
	{
		ThrowIfDisposed();
		
		if (_entryNodes.TryRemove(key, out var node))
		{
			lock (_accessOrderLock)
			{
				_accessOrder.Remove(node);
			}
		}
	}

	/// <inheritdoc />
	public override IReadOnlyList<string> SelectEntriesForEviction(
		IReadOnlyDictionary<string, IFusionCacheEntryInfo> currentEntries,
		int requestedEvictionCount)
	{
		ThrowIfDisposed();

		var result = new List<string>(Math.Min(requestedEvictionCount, currentEntries.Count));

		lock (_accessOrderLock)
		{
			// Start from the tail (least recently used)
			var current = _accessOrder.Last;
			while (current != null && result.Count < requestedEvictionCount)
			{
				var key = current.Value.Key;
				
				// Only include if entry still exists in cache
				if (currentEntries.ContainsKey(key))
				{
					result.Add(key);
				}
				
				current = current.Previous;
			}
		}

		return result;
	}

	/// <inheritdoc />
	public override void Reset()
	{
		ThrowIfDisposed();

		_entryNodes.Clear();
		lock (_accessOrderLock)
		{
			_accessOrder.Clear();
		}
		_accessCounter = 0;
	}

	private void UpdateAccessOrder(string key)
	{
		var newAccessTime = Interlocked.Increment(ref _accessCounter);

		// Try to get existing node
		if (_entryNodes.TryGetValue(key, out var existingNode))
		{
			lock (_accessOrderLock)
			{
				// Update access time and move to front
				existingNode.Value = new LruEntry(key, newAccessTime);
				_accessOrder.Remove(existingNode);
				_accessOrder.AddFirst(existingNode);
			}
		}
		else
		{
			// Create new node
			var entry = new LruEntry(key, newAccessTime);
			lock (_accessOrderLock)
			{
				var newNode = _accessOrder.AddFirst(entry);
				_entryNodes.TryAdd(key, newNode);
			}
		}
	}

	/// <inheritdoc />
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_entryNodes.Clear();
			lock (_accessOrderLock)
			{
				_accessOrder.Clear();
			}
		}
		base.Dispose(disposing);
	}

	private readonly struct LruEntry
	{
		public readonly string Key;
		public readonly long AccessTime;

		public LruEntry(string key, long accessTime)
		{
			Key = key;
			AccessTime = accessTime;
		}
	}
}