using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion.Internals.Diagnostics;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Handles all configuration and options-related concerns for a <see cref="FusionCache"/> instance.
/// It encapsulates the cache-wide <see cref="FusionCacheOptions"/> that drive the behavior of an instance,
/// generates a globally unique instance id if none is provided, maintains pre-configured
/// <see cref="FusionCacheEntryOptions"/> instances for common scenarios, and provides utilities to
/// create new entry options from the defaults. It also handles cache key prefixing and centralized
/// validation of tagging-related features and named-cache configuration.
/// </summary>
internal sealed class FusionCacheConfigurationManager
{
    private readonly FusionCacheOptions _options;
    private readonly ILogger<FusionCache>? _logger;
    private readonly string? _cacheKeyPrefix;

    internal FusionCacheConfigurationManager(IOptions<FusionCacheOptions> optionsAccessor, ILogger<FusionCache>? logger)
    {
        if (optionsAccessor is null)
            throw new ArgumentNullException(nameof(optionsAccessor));

        _options = optionsAccessor.Value ?? throw new NullReferenceException($"No options have been provided via {nameof(optionsAccessor.Value)}.");

        // Duplicate to shield from external changes
        _options = _options.Duplicate();

        // Ensure instance id is set
        if (string.IsNullOrWhiteSpace(_options.InstanceId))
        {
            // generate a random id internally
            _options.SetInstanceIdInternal(FusionCacheInternalUtils.GenerateOperationId());
        }

        if (string.IsNullOrEmpty(_options.CacheKeyPrefix) == false)
            _cacheKeyPrefix = _options.CacheKeyPrefix;

        // collapse null logger to avoid pay-for-play overhead
        _logger = (logger is NullLogger<FusionCache>) ? null : logger;

        // Precompute the default entry options set
        DefaultEntryOptions = _options.DefaultEntryOptions;

        // Precompute special options for try-update scenarios
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

        // Tagging-specific defaults
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

        // warm up observability sources
        _ = Activities.Source;
        _ = Metrics.Meter;
    }

    public FusionCacheOptions Options => _options;
    public string CacheName => _options.CacheName;
    public string InstanceId => _options.InstanceId!;
    public FusionCacheEntryOptions DefaultEntryOptions { get; }
    public FusionCacheEntryOptions TryUpdateEntryOptions { get; }
    public FusionCacheEntryOptions TagsDefaultEntryOptions { get; }
    public FusionCacheEntryOptions CascadeRemoveByTagEntryOptions { get; }

    /// <summary>
    /// Creates a new entry options instance starting from the configured default entry options and optionally
    /// applying a setup action and/or overriding the duration.
    /// </summary>
    public FusionCacheEntryOptions CreateEntryOptions(Action<FusionCacheEntryOptions>? setupAction = null, TimeSpan? duration = null)
    {
        var res = DefaultEntryOptions.Duplicate(duration);
        setupAction?.Invoke(res);
        return res;
    }

    public void MaybePreProcessCacheKey(ref string key)
    {
        if (_cacheKeyPrefix is not null)
            key = _cacheKeyPrefix + key;
    }

    public void CheckTaggingEnabled()
    {
        if (_options.DisableTagging)
            throw new InvalidOperationException("This operation requires Tagging, which has been disabled via FusionCacheOptions.DisableTagging.");
    }

    /// <summary>
    /// Logs a warning if the cache has been given a distinct name but no explicit CacheKeyPrefix has been configured.
    /// For named caches using shared memory cache instances it's often recommended to configure a prefix to avoid accidental collisions.
    /// </summary>
    public void ValidateNamedCacheSetup(IMemoryCache? memoryCache)
    {
        if (memoryCache is not null && CacheName != FusionCacheOptions.DefaultCacheName && string.IsNullOrWhiteSpace(_options.CacheKeyPrefix))
        {
            if (_logger?.IsEnabled(_options.MissingCacheKeyPrefixWarningLogLevel) ?? false)
                _logger.Log(_options.MissingCacheKeyPrefixWarningLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: a named cache is being used, and no CacheKeyPrefix has been specified. It's usually better to specify a prefix to automatically avoid cache key collisions. If collisions are already avoided when manually creating the cache keys, you can change the MissingCacheKeyPrefixWarningLogLevel option.", CacheName, InstanceId);
        }
    }
}
