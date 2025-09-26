using System;
using System.Collections.Generic;
using System.Linq;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// A least-frequently-used eviction policy. Frequency counts are incremented on every get/set
/// and entries with the lowest frequency (and oldest usage as a tie-breaker) are evicted when thresholds are exceeded.
/// </summary>
public class LfuEvictionPolicy
	: IFusionCacheEvictionPolicy
{
	private class FrequencyGroup
	{
		public int Frequency;
		public LinkedList<string> Keys = new();
		public LinkedListNode<FrequencyGroup> GroupNode;
	}

	private readonly Dictionary<string, LinkedListNode<string>> _keyNodes = new();
	private readonly Dictionary<string, FrequencyGroup> _keyGroups = new();
	private readonly LinkedList<FrequencyGroup> _freqList = new();
	private readonly object _lock = new();
	private int _count;

	/// <inheritdoc/>
	public string Name => "LFU";

	/// <inheritdoc/>
	public FusionCacheEvictionPolicyConfig Config { get; }

	/// <summary>
	/// Constructs a new <see cref="LfuEvictionPolicy"/> with the specified configuration.
	/// </summary>
	public LfuEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		Config = config ?? throw new ArgumentNullException(nameof(config));
	}

	private FrequencyGroup GetOrCreateFreqGroupAfter(FrequencyGroup? previousGroup, int freq)
	{
		if (previousGroup != null && previousGroup.GroupNode.Next != null && previousGroup.GroupNode.Next.Value.Frequency == freq)
		{
			return previousGroup.GroupNode.Next.Value;
		}
		// if there is no previous group, we are inserting at head
		if (previousGroup == null && _freqList.First != null && _freqList.First.Value.Frequency == freq)
		{
			return _freqList.First.Value;
		}
		var group = new FrequencyGroup { Frequency = freq };
		LinkedListNode<FrequencyGroup> node;
		if (previousGroup == null)
		{
			node = _freqList.AddFirst(group);
		}
		else
		{
			node = _freqList.AddAfter(previousGroup.GroupNode, group);
		}
		group.GroupNode = node;
		return group;
	}

	/// <inheritdoc/>
	public void OnGet(string key)
	{
		lock (_lock)
		{
			if (!_keyNodes.TryGetValue(key, out var node))
				return;
			var group = _keyGroups[key];
			// remove from current group
			group.Keys.Remove(node);
			var newFreq = group.Frequency + 1;
			var nextGroup = GetOrCreateFreqGroupAfter(group, newFreq);
			// add to next group at front
			var newNode = nextGroup.Keys.AddFirst(key);
			_keyNodes[key] = newNode;
			_keyGroups[key] = nextGroup;
			// if original group became empty, remove it from list
			if (group.Keys.Count == 0)
			{
				_freqList.Remove(group.GroupNode);
			}
		}
	}

	/// <inheritdoc/>
	public void OnSet(string key, FusionCacheEntryMetadata? metadata)
	{
		lock (_lock)
		{
			if (_keyNodes.ContainsKey(key))
			{
				// updating existing counts as an access
				OnGet(key);
				return;
			}
			// new key gets frequency 1
			FrequencyGroup group;
			if (_freqList.First != null && _freqList.First.Value.Frequency == 1)
			{
				group = _freqList.First.Value;
			}
			else
			{
				group = new FrequencyGroup { Frequency = 1 };
				var node = _freqList.AddFirst(group);
				group.GroupNode = node;
			}
			var keyNode = group.Keys.AddFirst(key);
			_keyNodes[key] = keyNode;
			_keyGroups[key] = group;
			_count++;
		}
	}

	/// <inheritdoc/>
	public void OnRemove(string key)
	{
		lock (_lock)
		{
			if (!_keyNodes.TryGetValue(key, out var node))
				return;
			var group = _keyGroups[key];
			group.Keys.Remove(node);
			if (group.Keys.Count == 0)
			{
				_freqList.Remove(group.GroupNode);
			}
			_keyNodes.Remove(key);
			_keyGroups.Remove(key);
			_count--;
		}
	}

	/// <inheritdoc/>
	public IEnumerable<string> GetKeysToEvict()
	{
		lock (_lock)
		{
			if (Config.MaxEntryCount is null)
				yield break;
			var capacity = Config.MaxEntryCount.Value;
			if (_count <= capacity * Config.EvictionThreshold)
				yield break;
			var toEvict = (int)Math.Round(capacity * Config.EvictionPercentage);
			if (toEvict < Config.MinEvictionBatchSize)
				toEvict = Config.MinEvictionBatchSize;
			if (toEvict > Config.MaxEvictionBatchSize)
				toEvict = Config.MaxEvictionBatchSize;
			var groupNode = _freqList.First;
			var evicted = 0;
			while (groupNode != null && evicted < toEvict)
			{
				var group = groupNode.Value;
				var keyNode = group.Keys.Last;
				while (keyNode != null && evicted < toEvict)
				{
					yield return keyNode.Value;
					evicted++;
					keyNode = keyNode.Previous;
				}
				groupNode = groupNode.Next;
			}
		}
	}
}
