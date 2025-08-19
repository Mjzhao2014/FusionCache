using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// An LFU (Least Frequently Used) eviction policy.
/// Tracks frequency counts for entries and evicts entries with the lowest usage count when capacity thresholds are exceeded.
/// </summary>
public sealed class LfuEvictionPolicy
	: IFusionCacheEvictionPolicy
{
	private class Node
	{
		public string Key;
		public int Freq;
		public long Size;
		public LinkedListNode<Node>? ListNode;
		public Node(string key, long size)
		{
			Key = key;
			Size = size;
			Freq = 1;
		}
	}

	// map key -> node
	private readonly Dictionary<string, Node> _nodes = new();
	// map frequency -> keys with that frequency (to preserve insertion order we can use LinkedList<string>)
	private readonly Dictionary<int, LinkedList<Node>> _freqLists = new();
	private int _minFreq = 1;
	private long _currentSize;
	private readonly object _lock = new object();

	public string Name => "LFU";
	public FusionCacheEvictionPolicyConfig Config { get; }

	public LfuEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		Config = config ?? throw new ArgumentNullException(nameof(config));
	}

	private void AddToFreqList(Node node, int freq)
	{
		if (!_freqLists.TryGetValue(freq, out var list))
		{
			list = new LinkedList<Node>();
			_freqLists[freq] = list;
		}
		node.ListNode = list.AddLast(node);
		if (freq < _minFreq)
		{
			_minFreq = freq;
		}
	}

	private void RemoveFromFreqList(Node node, int freq)
	{
		if (node.ListNode != null)
		{
			var list = node.ListNode.List;
			if (list is not null)
			{
				list.Remove(node.ListNode);
				if (list.Count == 0)
				{
					_freqLists.Remove(freq);
					if (_minFreq == freq)
					{
						_minFreq++;
					}
				}
			}
			node.ListNode = null;
		}
	}

	public void OnGet(string key)
	{
		lock (_lock)
		{
			if (_nodes.TryGetValue(key, out var node))
			{
				// increment frequency
				RemoveFromFreqList(node, node.Freq);
				node.Freq++;
				AddToFreqList(node, node.Freq);
			}
		}
	}

	public void OnSet(string key, FusionCacheEntryMetadata? metadata)
	{
		var size = metadata?.Size ?? 0;
		lock (_lock)
		{
			if (_nodes.TryGetValue(key, out var node))
			{
				// adjust size, keep frequency
				_currentSize -= node.Size;
				node.Size = size;
				_currentSize += size;
			}
			else
			{
				node = new Node(key, size);
				_nodes[key] = node;
				AddToFreqList(node, node.Freq);
				_currentSize += size;
				_minFreq = 1;
			}
		}
	}

	public void OnRemove(string key)
	{
		lock (_lock)
		{
			if (_nodes.TryGetValue(key, out var node))
			{
				RemoveFromFreqList(node, node.Freq);
				_nodes.Remove(key);
				_currentSize -= node.Size;
			}
		}
	}

	public IEnumerable<string> GetKeysToEvict()
	{
		var keys = new List<string>();
		lock (_lock)
		{
			int count = _nodes.Count;
			long size = _currentSize;
			var maxCount = Config.MaxEntryCount;
			var maxSize = Config.MaxTotalSize;
			bool overCount = maxCount.HasValue && count > (int)Math.Ceiling(maxCount.Value * Config.EvictionThreshold);
			bool overSize = maxSize.HasValue && size > (long)Math.Ceiling(maxSize.Value * Config.EvictionThreshold);
			if (overCount == false && overSize == false)
				return keys;
			int removed = 0;
			long freed = 0;
			int freq = _minFreq;
			while (removed < Config.MaxEvictionBatchSize && (overCount || overSize))
			{
				if (!_freqLists.TryGetValue(freq, out var list) || list.Count == 0)
				{
					freq++;
					continue;
				}
				var currentNode = list.First;
				while (currentNode is not null && removed < Config.MaxEvictionBatchSize)
				{
					var node = currentNode.Value;
					keys.Add(node.Key);
					removed++;
					freed += node.Size;
					if (removed >= Config.MinEvictionBatchSize)
					{
						int newCount = count - removed;
						long newSize = size - freed;
						overCount = maxCount.HasValue && newCount > (int)Math.Ceiling(maxCount.Value * Config.EvictionThreshold);
						overSize = maxSize.HasValue && newSize > (long)Math.Ceiling(maxSize.Value * Config.EvictionThreshold);
						if (overCount == false && overSize == false)
							break;
					}
					currentNode = currentNode.Next;
				}
				if (overCount == false && overSize == false)
					break;
				freq++;
			}
		}
		return keys;
	}
}
