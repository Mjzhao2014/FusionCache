using System;
using System.Threading;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ZiggyCreatures.Caching.Fusion.Internals
{
    /// <summary>
    /// Helper class responsible for processing and exposing FusionCache configuration and options.
    /// It validates, duplicates and stores the cache-wide options, ensures a unique instance id
    /// and surfaces pre-configured <see cref="FusionCacheEntryOptions"/> instances for internal use.
    /// </summary>
    internal sealed class FusionCacheConfigurationManager
    {
        private readonly ILogger<FusionCache>? _logger;
        private readonly FusionCacheOptions _options;
        private readonly string? _cacheKeyPrefix;

        public FusionCacheConfigurationManager(IOptions<FusionCacheOptions> optionsAccessor, ILogger<FusionCache>? logger)
        {
            if (optionsAccessor is null)
                throw new ArgumentNullException(nameof(optionsAccessor));

            _logger = logger;

            // Duplicate options to avoid external modifications
            _options = optionsAccessor.Value ?? throw new NullReferenceException($"No options have been provided via {nameof(optionsAccessor.Value)}.");
            _options = _options.Duplicate();

            DefaultEntryOptions = _options.DefaultEntryOptions;

            // Build try-update specialized options
            TryUpdateEntryOptions = new FusionCacheEntryOptions
            {
                DistributedCacheSoftTimeout = Timeout.InfiniteTimeSpan,
                DistributedCacheHardTimeout = Timeout.InfiniteTimeSpan,
                AllowBackgroundDistributedCacheOperations = false,
                ReThrowDistributedCacheExceptions = false,
                ReThrowSerializationExceptions = false,
                ReThrowBackplaneExceptions = false,
                SkipMemoryCacheRead = false,
                SkipMemoryCacheWrite = false,
                SkipDistributedCacheRead = false,
                SkipDistributedCacheWrite = false,
                SkipBackplaneNotifications = false,
            };

            TagsDefaultEntryOptions = _options.TagsDefaultEntryOptions;
            CascadeRemoveByTagEntryOptions = new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromHours(24),
                IsFailSafeEnabled = true,
                FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
                FailSafeMaxDuration = TimeSpan.FromHours(24),
                AllowBackgroundDistributedCacheOperations = false,
                AllowBackgroundBackplaneOperations = false,
                ReThrowDistributedCacheExceptions = false,
                ReThrowSerializationExceptions = false,
                ReThrowBackplaneExceptions = false,
                SkipMemoryCacheRead = false,
                SkipMemoryCacheWrite = false,
                SkipDistributedCacheRead = false,
                SkipDistributedCacheWrite = false,
                SkipBackplaneNotifications = false,
                Priority = CacheItemPriority.NeverRemove,
                Size = 1
            };

            // Ensure a globally unique Instance ID
            if (string.IsNullOrWhiteSpace(_options.InstanceId))
            {
                // The static utility generates a collision-safe id
                _options.SetInstanceIdInternal(FusionCacheInternalUtils.GenerateOperationId());
            }
            InstanceId = _options.InstanceId!;

            if (string.IsNullOrEmpty(_options.CacheKeyPrefix) == false)
                _cacheKeyPrefix = _options.CacheKeyPrefix;
        }

        /// <summary>
        /// The fully configured and duplicated options instance for this cache.
        /// </summary>
        public FusionCacheOptions Options => _options;

        /// <summary>
        /// The logical name for this cache (falls back to <see cref="FusionCacheOptions.DefaultCacheName"/>).
        /// </summary>
        public string CacheName => _options.CacheName;

        /// <summary>
        /// Globally unique identifier for this cache instance.
        /// </summary>
        public string InstanceId { get; }

        /// <summary>
        /// The default entry options used when none are specified.
        /// </summary>
        public FusionCacheEntryOptions DefaultEntryOptions { get; }

        /// <summary>
        /// Pre-configured options used for try-update scenarios.
        /// </summary>
        public FusionCacheEntryOptions TryUpdateEntryOptions { get; }

        /// <summary>
        /// Default entry options used for tagging operations.
        /// </summary>
        public FusionCacheEntryOptions TagsDefaultEntryOptions { get; }

        /// <summary>
        /// Pre-configured options used for internal cascade tagging invalidation.
        /// </summary>
        public FusionCacheEntryOptions CascadeRemoveByTagEntryOptions { get; }

        /// <summary>
        /// Factory for producing new <see cref="FusionCacheEntryOptions"/> cloned from <see cref="DefaultEntryOptions"/>.
        /// </summary>
        public FusionCacheEntryOptions CreateEntryOptions(Action<FusionCacheEntryOptions>? setupAction = null, TimeSpan? duration = null)
        {
            var res = DefaultEntryOptions.Duplicate(duration);
            setupAction?.Invoke(res);
            return res;
        }

        /// <summary>
        /// Applies the configured cache key prefix, if any, to the given key.
        /// </summary>
        internal void MaybePreProcessCacheKey(ref string key)
        {
            if (_cacheKeyPrefix is not null)
                key = _cacheKeyPrefix + key;
        }

        /// <summary>
        /// Throws if tagging has been disabled via options.
        /// </summary>
        internal void CheckTaggingEnabled()
        {
            if (_options.DisableTagging)
                throw new InvalidOperationException("This operation requires Tagging, which has been disabled via FusionCacheOptions.DisableTagging.");
        }

        /// <summary>
        /// Log a warning if a named cache is configured without an explicit cache key prefix, which can lead to collisions when sharing a memory cache.
        /// </summary>
        internal void ValidateNamedCacheSetup(IMemoryCache? memoryCache)
        {
            if (memoryCache is not null && CacheName != FusionCacheOptions.DefaultCacheName && string.IsNullOrWhiteSpace(_options.CacheKeyPrefix))
            {
                if (_logger?.IsEnabled(_options.MissingCacheKeyPrefixWarningLogLevel) ?? false)
                    _logger.Log(_options.MissingCacheKeyPrefixWarningLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: a named cache is being used, and no CacheKeyPrefix has been specified. It's usually better to specify a prefix to automatically avoid cache key collisions when sharing a memory cache.", CacheName, InstanceId);
            }
        }
    }
}
