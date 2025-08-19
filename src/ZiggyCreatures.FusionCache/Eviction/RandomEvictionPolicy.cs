namespace ZiggyCreatures.Caching.Fusion.Eviction;

/// <summary>
/// Random eviction policy that selects entries for eviction randomly.
/// This provides a simple, low-overhead eviction strategy with minimal bookkeeping.
/// Can be useful for workloads where access patterns are unpredictable.
/// </summary>
public sealed class RandomEvictionPolicy : FusionCacheEvictionPolicyBase
{
	private readonly Random _random;

	/// <summary>
	/// Creates a new random eviction policy with the specified configuration.
	/// </summary>
	/// <param name="config">The eviction policy configuration</param>
	public RandomEvictionPolicy(FusionCacheEvictionPolicyConfig config)
		: base(config)
	{
		_random = new Random();
	}

	/// <summary>
	/// Creates a new random eviction policy with the specified configuration and seed.
	/// </summary>
	/// <param name="config">The eviction policy configuration</param>
	/// <param name="seed">Seed for the random number generator (useful for testing)</param>
	public RandomEvictionPolicy(FusionCacheEvictionPolicyConfig config, int seed)
		: base(config)
	{
		_random = new Random(seed);
	}

	/// <inheritdoc />
	public override string Name => "Random";

	/// <inheritdoc />
	public override void OnEntryAccessed(string key, IFusionCacheEntryInfo entry)
	{
		ThrowIfDisposed();
		// Random policy doesn't track access patterns
	}

	/// <inheritdoc />
	public override void OnEntrySet(string key, IFusionCacheEntryInfo entry)
	{
		ThrowIfDisposed();
		// Random policy doesn't track sets
	}

	/// <inheritdoc />
	public override void OnEntryRemoved(string key)
	{
		ThrowIfDisposed();
		// Random policy doesn't track removals
	}

	/// <inheritdoc />
	public override IReadOnlyList<string> SelectEntriesForEviction(
		IReadOnlyDictionary<string, IFusionCacheEntryInfo> currentEntries,
		int requestedEvictionCount)
	{
		ThrowIfDisposed();

		var keys = currentEntries.Keys.ToList();
		var result = new List<string>(Math.Min(requestedEvictionCount, keys.Count));

		// Fisher-Yates shuffle to randomly select entries
		for (int i = 0; i < Math.Min(requestedEvictionCount, keys.Count); i++)
		{
			var randomIndex = _random.Next(i, keys.Count);
			
			// Swap current position with random position
			if (randomIndex != i)
			{
				(keys[i], keys[randomIndex]) = (keys[randomIndex], keys[i]);
			}
			
			result.Add(keys[i]);
		}

		return result;
	}

	/// <inheritdoc />
	public override void Reset()
	{
		ThrowIfDisposed();
		// Random policy has no state to reset
	}

	/// <inheritdoc />
	protected override void Dispose(bool disposing)
	{
		// Random policy has no resources to dispose
		base.Dispose(disposing);
	}
}