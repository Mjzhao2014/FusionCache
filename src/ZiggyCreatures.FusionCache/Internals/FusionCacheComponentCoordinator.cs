using System;
using System.Threading.Tasks;
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
/// Encapsulates the core component setup and lifetime management for a <see cref="FusionCache"/> instance,
/// including memory and distributed caches, backplane, serializer, memory locker and auto-recovery service.
/// Responsible for wires up the optional distributed cache and backplane using the configured serializer,
/// handles subscription to the backplane, and ensures proper disposal of resources on teardown.
/// </summary>
internal sealed class FusionCacheComponentCoordinator : IDisposable
{
    private readonly FusionCache _cache;
    private readonly FusionCacheOptions _options;
    private readonly ILogger<FusionCache>? _logger;
    private readonly FusionCacheEventsHub _events;

    internal FusionCacheComponentCoordinator(FusionCache cache, FusionCacheOptions options, IMemoryCache? memoryCache, ILogger<FusionCache>? logger, FusionCacheEventsHub events, IFusionCacheMemoryLocker? memoryLocker = null)
    {
        _cache = cache;
        _options = options;
        _logger = logger;
        _events = events;

        MemoryLocker = memoryLocker ?? new StandardMemoryLocker();

        MemoryCacheAccessor = new MemoryCacheAccessor(memoryCache, _options, _logger, _events.Memory);
        MemoryCacheCanClear = MemoryCacheAccessor.CanClear;
    }

    // MEMORY LOCKER
    internal IFusionCacheMemoryLocker MemoryLocker { get; private set; }

    // MEMORY CACHE
    internal MemoryCacheAccessor MemoryCacheAccessor { get; private set; }
    internal bool MemoryCacheCanClear { get; private set; }

    // DISTRIBUTED CACHE
	private DistributedCacheAccessor? _distributedAccessor;
	private IFusionCacheSerializer? _serializer;
	internal IFusionCacheSerializer? Serializer => _serializer;
	internal DistributedCacheAccessor? DistributedCacheAccessor => _distributedAccessor;
	public bool HasDistributedCache => _distributedAccessor is not null;
	public IDistributedCache? DistributedCache => _distributedAccessor?.DistributedCache;

    public IFusionCache SetupSerializer(IFusionCacheSerializer serializer)
    {
        if (serializer is null)
            throw new ArgumentNullException(nameof(serializer));

        if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
            _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: setup serializer (SERIALIZER={SerializerType})", _cache.CacheName, _cache.InstanceId, serializer.GetType().FullName);

        _serializer = serializer;
        return _cache;
    }

    public IFusionCache SetupDistributedCache(IDistributedCache distributedCache)
    {
        if (distributedCache is null)
            throw new ArgumentNullException(nameof(distributedCache));

        if (_serializer is null)
            throw new InvalidOperationException("The serializer must be set before setting up the distributed cache");

        if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
            _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: setup distributed cache (CACHE={DistributedCacheType})", _cache.CacheName, _cache.InstanceId, distributedCache.GetType().FullName);

        _distributedAccessor = new DistributedCacheAccessor(distributedCache, _serializer, _options, _logger, _events.Distributed);
        return _cache;
    }

    public IFusionCache SetupDistributedCache(IDistributedCache distributedCache, IFusionCacheSerializer serializer)
    {
        SetupSerializer(serializer);
        SetupDistributedCache(distributedCache);
        return _cache;
    }

    public IFusionCache RemoveDistributedCache()
    {
        if (_distributedAccessor is not null)
        {
            _distributedAccessor = null;
            if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
                _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: distributed cache removed", _cache.CacheName, _cache.InstanceId);
        }
        return _cache;
    }

    // BACKPLANE
	private BackplaneAccessor? _backplaneAccessor;
	internal BackplaneAccessor? BackplaneAccessor => _backplaneAccessor;
	private readonly object _backplaneLock = new();
    public bool HasBackplane => _backplaneAccessor is not null;
    public IFusionCacheBackplane? Backplane => _backplaneAccessor?.Backplane;

    public IFusionCache SetupBackplane(IFusionCacheBackplane backplane)
    {
        if (backplane is null)
            throw new ArgumentNullException(nameof(backplane));

        if (_backplaneAccessor is not null)
        {
            RemoveBackplane();
        }

        bool shouldSubscribe;
        lock (_backplaneLock)
        {
            shouldSubscribe = true;
            _backplaneAccessor = new BackplaneAccessor(_cache, backplane, _options, _logger);
        }

        if (shouldSubscribe)
        {
            if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
                _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: setup backplane (BACKPLANE={BackplaneType})", _cache.CacheName, _cache.InstanceId, backplane.GetType().FullName);

            if (_options.WaitForInitialBackplaneSubscribe)
            {
                _backplaneAccessor.Subscribe();
            }
            else
            {
                _ = Task.Run(async () =>
                {
                    await _backplaneAccessor.SubscribeAsync().ConfigureAwait(false);
                });
            }
        }

        if (HasBackplane && HasDistributedCache == false && _cache.DefaultEntryOptions.SkipBackplaneNotifications == false)
        {
            if (_logger?.IsEnabled(LogLevel.Error) ?? false)
                _logger.Log(LogLevel.Error, "FUSION [N={CacheName} I={CacheInstanceId}]: it has been detected a situation where there *IS* a backplane (BACKPLANE={BackplaneType}), there is *NOT* a distributed cache and the DefaultEntryOptions.SkipBackplaneNotifications is FALSE: this is potentially unsafe and should be avoided", _cache.CacheName, _cache.InstanceId, backplane.GetType().FullName);
        }

        return _cache;
    }

    public IFusionCache RemoveBackplane()
    {
        if (_backplaneAccessor is not null)
        {
            lock (_backplaneLock)
            {
                if (_backplaneAccessor is not null)
                {
                    _backplaneAccessor.Unsubscribe();
                    _backplaneAccessor = null;
                    if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
                        _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: backplane removed", _cache.CacheName, _cache.InstanceId);
                }
            }
        }
        return _cache;
    }

    // AUTO-RECOVERY
    private AutoRecoveryService? _autoRecovery;
    private readonly object _autoRecoveryLock = new();
    internal AutoRecoveryService AutoRecovery
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

    public void Dispose()
    {
        RemoveBackplane();
        RemoveDistributedCache();
        _autoRecovery?.Dispose();
        _autoRecovery = null;
        MemoryLocker.Dispose();
        MemoryCacheAccessor.Dispose();
    }
}
