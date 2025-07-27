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
/// Internal helper responsible for coordinating setup and lifecycle of all cache components:
/// memory cache/locker, distributed cache, backplane and auto-recovery.
/// </summary>
internal class FusionCacheComponentCoordinator
{
    private readonly ILogger<FusionCache>? _logger;
    private readonly FusionCacheOptions _options;
    private readonly FusionCacheEventsHub _events;
    private readonly object _backplaneLock = new();
    private readonly object _autoRecoveryLock = new();

    public IFusionCacheMemoryLocker MemoryLocker { get; private set; }
    public MemoryCacheAccessor MemoryCacheAccessor { get; private set; }
    public bool MemoryCacheCanClear => MemoryCacheAccessor.CanClear;
    public DistributedCacheAccessor? DistributedCacheAccessor { get; private set; }
    public IFusionCacheSerializer? Serializer { get; private set; }
    public BackplaneAccessor? BackplaneAccessor { get; private set; }
    private AutoRecoveryService? _autoRecovery;

    public FusionCacheComponentCoordinator(FusionCache owner, IMemoryCache? memoryCache, FusionCacheOptions options, ILogger<FusionCache>? logger, IFusionCacheMemoryLocker? memoryLocker, FusionCacheEventsHub eventsHub)
    {
        _options = options;
        _logger = logger;
        _events = eventsHub;
        MemoryLocker = memoryLocker ?? new StandardMemoryLocker();
        MemoryCacheAccessor = new MemoryCacheAccessor(memoryCache, _options, _logger, _events.Memory);
        DistributedCacheAccessor = null;
        BackplaneAccessor = null;
        Owner = owner;
    }

    private FusionCache Owner { get; }

    public void SetupSerializer(IFusionCacheSerializer serializer)
    {
        if (serializer is null)
            throw new ArgumentNullException(nameof(serializer));
        if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
            _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: setup serializer (SERIALIZER={SerializerType})", _options.CacheName, _options.InstanceId, serializer.GetType().FullName);
        Serializer = serializer;
    }

    public void SetupDistributedCache(IDistributedCache distributedCache)
    {
        if (distributedCache is null)
            throw new ArgumentNullException(nameof(distributedCache));
        if (Serializer is null)
            throw new InvalidOperationException("The serializer must be set before setting up the distributed cache");
        if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
            _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: setup distributed cache (CACHE={DistributedCacheType})", _options.CacheName, _options.InstanceId, distributedCache.GetType().FullName);
        DistributedCacheAccessor = new DistributedCacheAccessor(distributedCache, Serializer, _options, _logger, _events.Distributed);
    }

    public void RemoveDistributedCache()
    {
        if (DistributedCacheAccessor is not null)
        {
            DistributedCacheAccessor = null;
            if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
                _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: distributed cache removed", _options.CacheName, _options.InstanceId);
        }
    }

    public bool HasDistributedCache => DistributedCacheAccessor is not null;
    public IDistributedCache? DistributedCache => DistributedCacheAccessor?.DistributedCache;

    public void SetupBackplane(IFusionCacheBackplane backplane)
    {
        if (backplane is null)
            throw new ArgumentNullException(nameof(backplane));
        if (BackplaneAccessor is not null)
        {
            RemoveBackplane();
        }
        bool shouldSubscribe;
        lock (_backplaneLock)
        {
            shouldSubscribe = true;
            BackplaneAccessor = new BackplaneAccessor(Owner, backplane, _options, _logger);
        }
        if (shouldSubscribe)
        {
            if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
                _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: setup backplane (BACKPLANE={BackplaneType})", _options.CacheName, _options.InstanceId, backplane.GetType().FullName);
            if (_options.WaitForInitialBackplaneSubscribe)
            {
                BackplaneAccessor.Subscribe();
            }
            else
            {
                _ = Task.Run(async () =>
                {
                    await BackplaneAccessor.SubscribeAsync().ConfigureAwait(false);
                });
            }
        }
        // warn about backplane without distributed cache when skip notifications is false
        if (HasBackplane && HasDistributedCache == false && _options.DefaultEntryOptions.SkipBackplaneNotifications == false)
        {
            if (_logger?.IsEnabled(LogLevel.Error) ?? false)
                _logger.Log(LogLevel.Error, "FUSION [N={CacheName} I={CacheInstanceId}]: it has been detected a situation where there *IS* a backplane (BACKPLANE={BackplaneType}), there is *NOT* a distributed cache and the DefaultEntryOptions.SkipBackplaneNotifications option is set to false. This will probably cause problems, since a notification will be sent automatically at every change in the cache (Set, Remove, Expire and also GetOrSet when the factory is called) but there is not a distributed cache that different caches can use to share data.", _options.CacheName, _options.InstanceId, backplane.GetType().FullName);
        }
    }

    public void RemoveBackplane()
    {
        if (BackplaneAccessor is not null)
        {
            lock (_backplaneLock)
            {
                if (BackplaneAccessor is not null)
                {
                    BackplaneAccessor.Unsubscribe();
                    BackplaneAccessor = null;
                    if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
                        _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: backplane removed", _options.CacheName, _options.InstanceId);
                }
            }
        }
    }

    public bool HasBackplane => BackplaneAccessor is not null;
    public IFusionCacheBackplane? Backplane => BackplaneAccessor?.Backplane;

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
                        _autoRecovery = new AutoRecoveryService(Owner, _options, _logger);
                    }
                }
            }
            return _autoRecovery;
        }
    }

    public void Dispose()
    {
        // remove backplane/distributed to unsubscribe
        RemoveBackplane();
        RemoveDistributedCache();
        if (_autoRecovery is not null)
        {
            _autoRecovery.Dispose();
            _autoRecovery = null;
        }
        MemoryLocker.Dispose();
        MemoryCacheAccessor.Dispose();
    }
}
