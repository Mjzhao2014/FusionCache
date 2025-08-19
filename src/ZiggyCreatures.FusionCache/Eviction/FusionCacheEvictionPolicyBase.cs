namespace ZiggyCreatures.Caching.Fusion.Eviction;

/// <summary>
/// Base class for eviction policies that provides common functionality
/// for capacity management and eviction triggers.
/// </summary>
public abstract class FusionCacheEvictionPolicyBase : IFusionCacheEvictionPolicy
{
	private readonly FusionCacheEvictionPolicyConfig _config;
	private bool _disposed;

	/// <summary>
	/// Creates a new instance with the specified configuration.
	/// </summary>
	/// <param name="config">The eviction policy configuration</param>
	protected FusionCacheEvictionPolicyBase(FusionCacheEvictionPolicyConfig config)
	{
		_config = config ?? throw new ArgumentNullException(nameof(config));
		_config.Validate();
	}

	/// <summary>
	/// Gets the configuration for this eviction policy.
	/// </summary>
	protected FusionCacheEvictionPolicyConfig Config => _config;

	/// <inheritdoc />
	public abstract string Name { get; }

	/// <inheritdoc />
	public abstract void OnEntryAccessed(string key, IFusionCacheEntryInfo entry);

	/// <inheritdoc />
	public abstract void OnEntrySet(string key, IFusionCacheEntryInfo entry);

	/// <inheritdoc />
	public abstract void OnEntryRemoved(string key);

	/// <inheritdoc />
	public abstract IReadOnlyList<string> SelectEntriesForEviction(
		IReadOnlyDictionary<string, IFusionCacheEntryInfo> currentEntries,
		int requestedEvictionCount);

	/// <inheritdoc />
	public virtual bool ShouldTriggerEviction(int currentEntryCount, long currentTotalSize)
	{
		if (_config.MaxEntryCount.HasValue)
		{
			var entryThreshold = (int)Math.Ceiling(_config.MaxEntryCount.Value * _config.EvictionThreshold);
			if (currentEntryCount >= entryThreshold)
			{
				return true;
			}
		}

		if (_config.MaxTotalSize.HasValue)
		{
			var sizeThreshold = (long)Math.Ceiling(_config.MaxTotalSize.Value * _config.EvictionThreshold);
			if (currentTotalSize >= sizeThreshold)
			{
				return true;
			}
		}

		return false;
	}

	/// <inheritdoc />
	public virtual int GetEvictionCount(int currentEntryCount, long currentTotalSize)
	{
		var targetEvictionCount = (int)Math.Ceiling(currentEntryCount * _config.EvictionPercentage);
		
		// Apply min/max constraints
		targetEvictionCount = Math.Max(targetEvictionCount, _config.MinEvictionCount);
		targetEvictionCount = Math.Min(targetEvictionCount, _config.MaxEvictionCount);
		
		// Don't evict more than what we have
		targetEvictionCount = Math.Min(targetEvictionCount, currentEntryCount);

		return targetEvictionCount;
	}

	/// <inheritdoc />
	public abstract void Reset();

	/// <summary>
	/// Gets the estimated size of an entry in bytes.
	/// Returns the size from metadata if available, otherwise returns a default estimate.
	/// </summary>
	/// <param name="entry">The cache entry</param>
	/// <returns>Estimated size in bytes</returns>
	protected virtual long GetEntrySize(IFusionCacheEntryInfo entry)
	{
		// Use size from metadata if available
		var size = entry.GetSize();
		if (size.HasValue)
		{
			return size.Value;
		}

		// Default size estimate (can be overridden by derived classes)
		return 1024; // 1KB default
	}

	/// <summary>
	/// Throws if the object has been disposed.
	/// </summary>
	protected void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(GetType().Name);
		}
	}

	/// <summary>
	/// Releases resources used by the eviction policy.
	/// </summary>
	/// <param name="disposing">True if disposing managed resources</param>
	protected virtual void Dispose(bool disposing)
	{
		if (!_disposed && disposing)
		{
			// Dispose managed resources in derived classes
		}
		_disposed = true;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}
}