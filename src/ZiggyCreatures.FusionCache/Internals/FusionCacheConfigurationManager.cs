using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion.Internals.Diagnostics;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Internal helper responsible for configuration and option handling for a <see cref="FusionCache"/>).
/// Centralizes all option duplication, instance id generation and cache key handling.
/// </summary>
internal class FusionCacheConfigurationManager
{
    private readonly ILogger<FusionCache>? _logger;
    private readonly string? _cacheKeyPrefix;

    public FusionCacheOptions Options { get; }

    public string CacheName => Options.CacheName;

    public string InstanceId { get; }

    public FusionCacheEntryOptions DefaultEntryOptions { get; }

    /// <summary>
    /// Options used internally when doing try-update operations.
    /// </summary>
    public FusionCacheEntryOptions TryUpdateEntryOptions { get; }

    /// <summary>
    /// The base entry options for tagging operations.
    /// </summary>
    public FusionCacheEntryOptions TagsDefaultEntryOptions { get; }

    /// <summary>
    /// Internal options for cascade tag remove operations.
    /// </summary>
    public FusionCacheEntryOptions CascadeRemoveByTagEntryOptions { get; }

    // Tagging related special keys
    internal readonly string TagInternalCacheKeyPrefix;
    internal const string ClearRemoveTag = "!";
    internal readonly string ClearRemoveTagCacheKey;
    internal readonly string ClearRemoveTagInternalCacheKey;
    internal long ClearRemoveTimestamp;
    internal const string ClearExpireTag = "*";
    internal readonly string ClearExpireTagCacheKey;
    internal readonly string ClearExpireTagInternalCacheKey;
    internal long ClearExpireTimestamp;

    public FusionCacheConfigurationManager(IOptions<FusionCacheOptions> optionsAccessor, ILogger<FusionCache>? logger, IMemoryCache? memoryCache)
    {
        if (optionsAccessor is null)
            throw new ArgumentNullException(nameof(optionsAccessor));

        // ILogger: ignore NullLogger for perf
        _logger = logger is NullLogger<FusionCache> ? null : logger;

        // Options
        Options = optionsAccessor.Value ?? throw new NullReferenceException($"No options have been provided via {nameof(optionsAccessor.Value)}.");
        // duplicate to avoid external modifications
        Options = Options.Duplicate();

        DefaultEntryOptions = Options.DefaultEntryOptions;
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
        TagsDefaultEntryOptions = Options.TagsDefaultEntryOptions;
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

        // instance id generation
        if (string.IsNullOrWhiteSpace(Options.InstanceId))
        {
            Options.SetInstanceIdInternal(FusionCacheInternalUtils.GenerateOperationId());
        }
        InstanceId = Options.InstanceId!;

        // cache key prefix
        if (string.IsNullOrEmpty(Options.CacheKeyPrefix) == false)
            _cacheKeyPrefix = Options.CacheKeyPrefix;

        // Initialize tagging related keys
        TagInternalCacheKeyPrefix = GetTagInternalCacheKey("");

        ClearRemoveTimestamp = -1;
        ClearRemoveTagCacheKey = GetTagCacheKey(ClearRemoveTag);
        ClearRemoveTagInternalCacheKey = GetTagInternalCacheKey(ClearRemoveTag);

        ClearExpireTimestamp = -1;
        ClearExpireTagCacheKey = GetTagCacheKey(ClearExpireTag);
        ClearExpireTagInternalCacheKey = GetTagInternalCacheKey(ClearExpireTag);

        // Check for cache key prefix
        ValidateNamedCacheSetup(memoryCache);

        // Warm up observability
        _ = Activities.Source;
        _ = Metrics.Meter;
    }

    public FusionCacheEntryOptions CreateEntryOptions(Action<FusionCacheEntryOptions>? setupAction = null, TimeSpan? duration = null)
    {
        var res = DefaultEntryOptions.Duplicate(duration);
        setupAction?.Invoke(res);
        return res;
    }

    internal void MaybePreProcessCacheKey(ref string key)
    {
        if (_cacheKeyPrefix is not null)
            key = _cacheKeyPrefix + key;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal string MaybeGenerateOperationId()
    {
        return FusionCacheInternalUtils.MaybeGenerateOperationId(_logger);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CheckTaggingEnabled()
    {
        if (Options.DisableTagging)
            throw new InvalidOperationException("This operation requires Tagging, which has been disabled via FusionCacheOptions.DisableTagging.");
    }

	internal static string GetTagCacheKey(string tag)
	{
		return $"__fc:t:{tag}";
	}

    private string GetTagInternalCacheKey(string tag)
    {
        var res = GetTagCacheKey(tag);
        MaybePreProcessCacheKey(ref res);
        return res;
    }

    internal void ValidateNamedCacheSetup(IMemoryCache? memoryCache)
    {
        if (memoryCache is not null && CacheName != FusionCacheOptions.DefaultCacheName && string.IsNullOrWhiteSpace(Options.CacheKeyPrefix))
        {
            if (_logger?.IsEnabled(Options.MissingCacheKeyPrefixWarningLogLevel) ?? false)
                _logger.Log(Options.MissingCacheKeyPrefixWarningLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: a named cache is being used, and no CacheKeyPrefix has been specified. It's usually better to specify a prefix to automatically avoid cache key collisions between different named caches using the same underlying memory/distributed store.", CacheName, InstanceId);
        }
    }
}
