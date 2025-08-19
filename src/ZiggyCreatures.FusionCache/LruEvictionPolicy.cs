using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// An LRU (Least Recently Used) eviction policy for FusionCache.
/// Tracks entries in use order and evicts the least recently used entries when capacity thresholds are exceeded.
/// </summary>
public sealed class LruEvictionPolicy
	: IFusionCacheEvictionPolicy
{
	private class Node
	{
		public string Key;
		public Node? Prev;
		public Node? Next;
		public long Size;
		public Node(string key, long size)
		{
			Key = key;
			Size = size;
		}
	}

	private readonly Dictionary<string, Node> _map = new();
	private Node? _head;
	private Node? _tail;
	private long _currentSize;
	private readonly object _lock = new object();

	public string Name => "LRU";
	public FusionCacheEvictionPolicyConfig Config { get; }

	public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		Config = config ?? throw new ArgumentNullException(nameof(config));
	}

	private void AddToHead(Node node)
	{
		if (_head is null)
		{
			_head = _tail = node;
		}
		else
		{
			node.Next = _head;
			_head.Prev = node;
			_head = node;
		}
	}

	private void RemoveNode(Node node)
	{
		if (node.Prev is not null)
			node.Prev.Next = node.Next;
		else
			_head = node.Next;
		if (node.Next is not null)
			node.Next.Prev = node.Prev;
		else
			_tail = node.Prev;
		node.Prev = node.Next = null;
	}

	private void MoveToHead(Node node)
	{
		if (_head == node)
			return;
		RemoveNode(node);
		AddToHead(node);
	}

	public void OnGet(string key)
	{
		lock (_lock)
		{
			if (_map.TryGetValue(key, out var node))
			{
				MoveToHead(node);
			}
		}
	}

	public void OnSet(string key, FusionCacheEntryMetadata? metadata)
	{
		var size = metadata?.Size ?? 0;
		lock (_lock)
		{
			if (_map.TryGetValue(key, out var existing))
			{
				// adjust size
				_currentSize -= existing.Size;
				existing.Size = size;
				_currentSize += size;
				MoveToHead(existing);
			}
			else
			{
				var node = new Node(key, size);
				_map[key] = node;
				AddToHead(node);
				_currentSize += size;
			}
		}
	}

	public void OnRemove(string key)
	{
		lock (_lock)
		{
			if (_map.TryGetValue(key, out var node))
			{
				RemoveNode(node);
				_map.Remove(key);
				_currentSize -= node.Size;
			}
		}
	}

	public IEnumerable<string> GetKeysToEvict()
	{
		var keys = new List<string>();
		lock (_lock)
		{
			int count = _map.Count;
			long size = _currentSize;
			var maxCount = Config.MaxEntryCount;
			var maxSize = Config.MaxTotalSize;
			bool overCount = maxCount.HasValue && count > (int)Math.Ceiling(maxCount.Value * Config.EvictionThreshold);
			bool overSize = maxSize.HasValue && size > (long)Math.Ceiling(maxSize.Value * Config.EvictionThreshold);
			if (overCount == false && overSize == false)
			{
				return keys;
			}
			// remove least recently used until below thresholds or limits reached
			int removed = 0;
			long freed = 0;
			var node = _tail;
			while (node is not null && removed < Config.MaxEvictionBatchSize)
			{
				keys.Add(node.Key);
				removed++;
				freed += node.Size;
				if (removed >= Config.MinEvictionBatchSize)
				{
					// check if thresholds satisfied
					double newCount = count - removed;
					double newSize = size - freed;
					bool okCount = maxCount.HasValue == false || newCount <= maxCount.Value * Config.EvictionThreshold;
					bool okSize = maxSize.HasValue == false || newSize <= maxSize.Value * Config.EvictionThreshold;
					if (okCount && okSize)
						break;
				}
				node = node.Prev;
			}
		}
		return keys;
	}
}
