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

namespace ZiggyCreatures.Caching.Fusion.Internals
{
    /// <summary>
    /// Helper class responsible for creating and coordinating all cache components like
    /// memory/distributed caches, backplanes, auto-recovery and memory locking.
    /// Handles all lifecycle concerns (setup/remove/subscribe/dispose) for these components.
    /// </summary>
    internal sealed class FusionCacheComponentCoordinator : IDisposable
    {
        private readonly FusionCache _cache;
        private readonly FusionCacheOptions _options;
        private readonly ILogger<FusionCache>? _logger;
        private readonly FusionCacheEventsHub _events;
        private readonly bool _mcaCanClear;

        private readonly object _backplaneLock = new();
        private readonly object _autoRecoveryLock = new();

        private IFusionCacheSerializer? _serializer;
        /// <summary>
        /// Currently active serializer for distributed cache payloads, if any has been configured.
        /// </summary>
        public IFusionCacheSerializer? Serializer => _serializer;
        private DistributedCacheAccessor? _dca;
        private BackplaneAccessor? _bpa;
        private AutoRecoveryService? _autoRecovery;

        internal FusionCacheComponentCoordinator(FusionCache cache, FusionCacheOptions options, ILogger<FusionCache>? logger, FusionCacheEventsHub events, IMemoryCache? memoryCache, IFusionCacheMemoryLocker? memoryLocker)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
            _events = events;

            // Memory locker
            MemoryLocker = memoryLocker ?? new StandardMemoryLocker();

            // Memory cache accessor
            MemoryCacheAccessor = new MemoryCacheAccessor(memoryCache, _options, _logger, _events.Memory);
            _mcaCanClear = MemoryCacheAccessor.CanClear;
        }

        public IFusionCacheMemoryLocker MemoryLocker { get; private set; }

        public MemoryCacheAccessor MemoryCacheAccessor { get; }

        public DistributedCacheAccessor? DistributedCacheAccessor => _dca;

        public BackplaneAccessor? BackplaneAccessor => _bpa;

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
        /// Configure the serializer used for distributed cache payloads.
        /// </summary>
        public void SetupSerializer(IFusionCacheSerializer serializer)
        {
            if (serializer is null)
                throw new ArgumentNullException(nameof(serializer));

            if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
                _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: setup serializer (SERIALIZER={SerializerType})", _cache.CacheName, _cache.InstanceId, serializer.GetType().FullName);

            _serializer = serializer;
        }

        /// <summary>
        /// Setup the distributed cache, validating that a serializer has been configured.
        /// </summary>
        public void SetupDistributedCache(IDistributedCache distributedCache)
        {
            if (distributedCache is null)
                throw new ArgumentNullException(nameof(distributedCache));

            if (_serializer is null)
                throw new InvalidOperationException("The serializer must be set before setting up the distributed cache");

            if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
                _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: setup distributed cache (CACHE={DistributedCacheType})", _cache.CacheName, _cache.InstanceId, distributedCache.GetType().FullName);

            _dca = new DistributedCacheAccessor(distributedCache, _serializer, _options, _logger, _events.Distributed);
        }

        /// <summary>
        /// Convenience method to set up both serializer and distributed cache together.
        /// </summary>
        public void SetupDistributedCache(IDistributedCache distributedCache, IFusionCacheSerializer serializer)
        {
            SetupSerializer(serializer);
            SetupDistributedCache(distributedCache);
        }

        public void RemoveDistributedCache()
        {
            if (_dca is not null)
            {
                _dca = null;
                if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
                    _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: distributed cache removed", _cache.CacheName, _cache.InstanceId);
            }
        }

        public bool HasDistributedCache => _dca is not null;

        public IDistributedCache? DistributedCache => _dca?.DistributedCache;

        public void SetupBackplane(IFusionCacheBackplane backplane)
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
                    _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: setup backplane (BACKPLANE={BackplaneType})", _cache.CacheName, _cache.InstanceId, backplane.GetType().FullName);

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

            // warn about using a backplane without a distributed cache
            if (HasBackplane && HasDistributedCache == false && _options.DefaultEntryOptions.SkipBackplaneNotifications == false)
            {
                if (_logger?.IsEnabled(LogLevel.Error) ?? false)
                    _logger.Log(LogLevel.Error, "FUSION [N={CacheName} I={CacheInstanceId}]: it has been detected a situation where there *IS* a backplane (BACKPLANE={BackplaneType}), there is *NOT* a distributed cache and the DefaultEntryOptions.SkipBackplaneNotifications option is set to false. This will probably cause problems, since a notification will be sent automatically at every change in the cache (Set, Remove, Expire and also GetOrSet when the factory is called) but there is not a distributed cache that different nodes can use, basically resulting in a situation where the cache will keep invalidating itself at every change. It is suggested to either (1) add a distributed cache or (2) change the DefaultEntryOptions.SkipBackplaneNotifications to true.", _cache.CacheName, _cache.InstanceId, backplane.GetType().FullName);
            }
        }

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
                            _logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}]: backplane removed", _cache.CacheName, _cache.InstanceId);
                    }
                }
            }
        }

        public bool HasBackplane => _bpa is not null;

        public IFusionCacheBackplane? Backplane => _bpa?.Backplane;

        public bool CanExecuteRawClear()
        {
            if (HasDistributedCache)
                return false;
            if (HasBackplane)
                return false;
            if (_mcaCanClear == false)
                return false;
            return true;
        }

        public bool TryExecuteRawClear(string operationId)
        {
            if (CanExecuteRawClear() == false)
            {
                if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
                    _logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): it was not possible to execute a raw clear", _cache.CacheName, _cache.InstanceId, operationId);

                return false;
            }

            if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
                _logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): executing a raw clear", _cache.CacheName, _cache.InstanceId, operationId);

            return MemoryCacheAccessor.TryClear();
        }

        public void Dispose()
        {
            RemoveBackplane();
            RemoveDistributedCache();
            _autoRecovery?.Dispose();
            _autoRecovery = null;
            MemoryLocker.Dispose();
            MemoryLocker = null;
            MemoryCacheAccessor.Dispose();
        }
    }
}
