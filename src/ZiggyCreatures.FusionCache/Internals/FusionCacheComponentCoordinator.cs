using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Internals.AutoRecovery;
using ZiggyCreatures.Caching.Fusion.Internals.Backplane;
using ZiggyCreatures.Caching.Fusion.Internals.Distributed;
using ZiggyCreatures.Caching.Fusion.Internals.Memory;
using ZiggyCreatures.Caching.Fusion.Locking;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Manages the setup, coordination, and lifecycle of all cache components including 
/// memory cache, distributed cache, backplane, and auto-recovery services.
/// </summary>
internal sealed class FusionCacheComponentCoordinator : IDisposable
{
	private readonly FusionCache _cache;
	private readonly FusionCacheOptions _options;
	private readonly ILogger<FusionCache>? _logger;
	private readonly FusionCacheEventsHub _events;

	// Component storage
	private IFusionCacheMemoryLocker _memoryLocker;
	private MemoryCacheAccessor _mca;
	private readonly bool _mcaCanClear;
	private DistributedCacheAccessor? _dca;
	private IFusionCacheSerializer? _serializer;
	private BackplaneAccessor? _bpa;
	private readonly object _backplaneLock = new();
	private AutoRecoveryService? _autoRecovery;
	private readonly object _autoRecoveryLock = new();

	/// <summary>
	/// Initializes a new instance of the FusionCacheComponentCoordinator.
	/// </summary>
	/// <param name="cache">The FusionCache instance that owns this coordinator.</param>
	/// <param name="options">The cache options.</param>
	/// <param name="logger">The logger instance.</param>
	/// <param name="events">The events hub.</param>
	/// <param name="memoryCache">The memory cache instance.</param>
	/// <param name="memoryLocker">The memory locker instance.</param>
	public FusionCacheComponentCoordinator(FusionCache cache, FusionCacheOptions options, ILogger<FusionCache>? logger, FusionCacheEventsHub events, IMemoryCache? memoryCache, IFusionCacheMemoryLocker? memoryLocker)
	{
		_cache = cache ?? throw new ArgumentNullException(nameof(cache));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_logger = logger;
		_events = events ?? throw new ArgumentNullException(nameof(events));

		// Initialize memory locker
		_memoryLocker = memoryLocker ?? new StandardMemoryLocker();

		// Initialize memory cache
		_mca = new MemoryCacheAccessor(memoryCache, options, logger, events.Memory);
		_mcaCanClear = _mca.CanClear;
	}

	/// <summary>
	/// Gets the memory cache accessor.
	/// </summary>
	public MemoryCacheAccessor MemoryCacheAccessor => _mca;

	/// <summary>
	/// Gets the distributed cache accessor.
	/// </summary>
	public DistributedCacheAccessor? DistributedCacheAccessor => _dca;

	/// <summary>
	/// Gets the backplane accessor.
	/// </summary>
	public BackplaneAccessor? BackplaneAccessor => _bpa;

	/// <summary>
	/// Gets the memory locker.
	/// </summary>
	public IFusionCacheMemoryLocker MemoryLocker => _memoryLocker;

	/// <summary>
	/// Gets a value indicating whether the memory cache can be cleared.
	/// </summary>
	public bool MemoryCacheCanClear => _mcaCanClear;

	/// <summary>
	/// Gets a value indicating whether a distributed cache is available.
	/// </summary>
	public bool HasDistributedCache => _dca is not null;

	/// <summary>
	/// Gets a value indicating whether a backplane is available.
	/// </summary>
	public bool HasBackplane => _bpa is not null;

	/// <summary>
	/// Gets the distributed cache instance.
	/// </summary>
	public IDistributedCache? DistributedCache => _dca?.DistributedCache;

	/// <summary>
	/// Gets the backplane instance.
	/// </summary>
	public IFusionCacheBackplane? Backplane => _bpa?.Backplane;

	/// <summary>
	/// Gets the current serializer.
	/// </summary>
	public IFusionCacheSerializer? Serializer => _serializer;

	/// <summary>
	/// Gets the auto-recovery service with lazy initialization.
	/// </summary>
	public AutoRecoveryService AutoRecovery
	{
		get
		{
			if (_autoRecovery is null)
			{
				lock (_autoRecoveryLock)
				{
					if (_autoRecovery is null)
					{
						_autoRecovery = new AutoRecoveryService(_cache, _options, _logger);
					}
				}
			}

			return _autoRecovery;
		}
	}

	/// <summary>
	/// Sets up the serializer for distributed cache operations.
	/// </summary>
	/// <param name="serializer">The serializer to use.</param>
	public void SetupSerializer(IFusionCacheSerializer serializer)
	{
		if (serializer is null)
			throw new ArgumentNullException(nameof(serializer));

		if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
			_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: setup serializer (SERIALIZER={SerializerType})", _options.CacheName, _options.InstanceId, serializer.GetType().FullName);

		_serializer = serializer;
	}

	/// <summary>
	/// Sets up the distributed cache with the current serializer.
	/// </summary>
	/// <param name="distributedCache">The distributed cache to set up.</param>
	public void SetupDistributedCache(IDistributedCache distributedCache)
	{
		if (distributedCache is null)
			throw new ArgumentNullException(nameof(distributedCache));

		if (_serializer is null)
			throw new InvalidOperationException("The serializer must be set before setting up the distributed cache");

		if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
			_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: setup distributed cache (CACHE={DistributedCacheType})", _options.CacheName, _options.InstanceId, distributedCache.GetType().FullName);

		_dca = new DistributedCacheAccessor(distributedCache, _serializer, _options, _logger, _events.Distributed);
	}

	/// <summary>
	/// Sets up both the serializer and distributed cache.
	/// </summary>
	/// <param name="distributedCache">The distributed cache to set up.</param>
	/// <param name="serializer">The serializer to use.</param>
	public void SetupDistributedCache(IDistributedCache distributedCache, IFusionCacheSerializer serializer)
	{
		SetupSerializer(serializer);
		SetupDistributedCache(distributedCache);
	}

	/// <summary>
	/// Removes the distributed cache.
	/// </summary>
	public void RemoveDistributedCache()
	{
		if (_dca is not null)
		{
			_dca = null;

			if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
				_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: distributed cache removed", _options.CacheName, _options.InstanceId);
		}
	}

	/// <summary>
	/// Sets up the backplane with proper subscription handling.
	/// </summary>
	/// <param name="backplane">The backplane to set up.</param>
	/// <param name="defaultEntryOptions">The default entry options for validation.</param>
	public void SetupBackplane(IFusionCacheBackplane backplane, FusionCacheEntryOptions defaultEntryOptions)
	{
		if (backplane is null)
			throw new ArgumentNullException(nameof(backplane));

		if (_bpa is not null)
		{
			RemoveBackplane();
		}

		var shouldSubscribe = false;
		lock (_backplaneLock)
		{
			shouldSubscribe = true;
			_bpa = new BackplaneAccessor(_cache, backplane, _options, _logger);
		}

		if (shouldSubscribe)
		{
			if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
				_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: setup backplane (BACKPLANE={BackplaneType})", _options.CacheName, _options.InstanceId, backplane.GetType().FullName);

			if (_options.WaitForInitialBackplaneSubscribe)
			{
				_bpa.Subscribe();
			}
			else
			{
				_ = Task.Run(async () =>
				{
					await _bpa.SubscribeAsync().ConfigureAwait(false);
				});
			}
		}

		ValidateBackplaneSetup(backplane, defaultEntryOptions);
	}

	/// <summary>
	/// Removes the backplane with proper cleanup.
	/// </summary>
	public void RemoveBackplane()
	{
		if (_bpa is not null)
		{
			lock (_backplaneLock)
			{
				if (_bpa is not null)
				{
					_bpa.Unsubscribe();
					_bpa = null;

					if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
						_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: backplane removed", _options.CacheName, _options.InstanceId);
				}
			}
		}
	}

	/// <summary>
	/// Validates backplane setup and warns about potential configuration issues.
	/// </summary>
	/// <param name="backplane">The backplane being set up.</param>
	/// <param name="defaultEntryOptions">The default entry options.</param>
	private void ValidateBackplaneSetup(IFusionCacheBackplane backplane, FusionCacheEntryOptions defaultEntryOptions)
	{
		// CHECK: WARN THE USER IN CASE OF
		// - HAS A MEMORY CACHE (ALWAYS)
		// - HAS A BACKPLANE
		// - DOES *NOT* HAVE A DISTRIBUTED CACHE
		// - THE OPTION DefaultEntryOptions.SkipBackplaneNotifications IS FALSE
		if (HasBackplane && HasDistributedCache == false && defaultEntryOptions.SkipBackplaneNotifications == false)
		{
			if (_logger?.IsEnabled(LogLevel.Error) ?? false)
				_logger.Log(LogLevel.Error, "FUSION [N={CacheName} I={CacheInstanceId}]: it has been detected a situation where there *IS* a backplane (BACKPLANE={BackplaneType}), there is *NOT* a distributed cache and the DefaultEntryOptions.SkipBackplaneNotifications option is set to false. This will probably cause problems, since a notification will be sent automatically at every change in the cache (Set, Remove, Expire and also GetOrSet when the factory is called) but there is not a distributed cache that different nodes can use, basically resulting in a situation where the cache will keep invalidating itself at every change. It is suggested to either (1) add a distributed cache or (2) change the DefaultEntryOptions.SkipBackplaneNotifications to true.", _options.CacheName, _options.InstanceId, backplane.GetType().FullName);
		}
	}

	/// <summary>
	/// Determines if a raw cache clear can be executed based on component configuration.
	/// </summary>
	/// <returns>True if raw clear can be executed, false otherwise.</returns>
	public bool CanExecuteRawClear()
	{
		// CHECK: NO DISTRIBUTED CACHE
		if (HasDistributedCache)
			return false;

		// CHECK: NO BACKPLANE
		if (HasBackplane)
			return false;

		// CHECK: THE INNER MEMORY CACHE SUPPORTS CLEARING
		if (_mcaCanClear == false)
			return false;

		return true;
	}

	/// <summary>
	/// Disposes all managed components.
	/// </summary>
	public void Dispose()
	{
		RemoveBackplane();
		RemoveDistributedCache();
		
		lock (_autoRecoveryLock)
		{
			_autoRecovery?.Dispose();
			_autoRecovery = null;
		}

		_mca?.Dispose();
	}
}