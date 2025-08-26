using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Manages configuration, options processing, and cache key handling for FusionCache.
/// This component centralizes all configuration-related responsibilities.
/// </summary>
internal sealed class FusionCacheConfigurationManager
{
	private readonly FusionCacheOptions _options;
	private readonly string? _cacheKeyPrefix;
	private readonly ILogger<FusionCache>? _logger;
	private readonly FusionCacheEntryOptions _defaultEntryOptions;
	private readonly FusionCacheEntryOptions _tryUpdateEntryOptions;
	private readonly FusionCacheEntryOptions _tagsDefaultEntryOptions;
	private readonly FusionCacheEntryOptions _cascadeRemoveByTagEntryOptions;

	// Tag-related cache keys
	public readonly string TagInternalCacheKeyPrefix;
	public readonly string ClearRemoveTagCacheKey;
	public readonly string ClearRemoveTagInternalCacheKey;
	public readonly string ClearExpireTagCacheKey;
	public readonly string ClearExpireTagInternalCacheKey;

	// Constants for special tags
	internal const string ClearRemoveTag = "!";
	internal const string ClearExpireTag = "*";

	/// <summary>
	/// Initializes a new instance of the FusionCacheConfigurationManager.
	/// </summary>
	/// <param name="optionsAccessor">The options accessor containing cache configuration.</param>
	/// <param name="logger">The logger instance for logging configuration operations.</param>
	/// <param name="cacheName">The name of the cache for validation purposes.</param>
	/// <param name="memoryCache">The memory cache instance for named cache validation.</param>
	public FusionCacheConfigurationManager(IOptions<FusionCacheOptions> optionsAccessor, ILogger<FusionCache>? logger, string cacheName, IMemoryCache? memoryCache)
	{
		if (optionsAccessor is null)
			throw new ArgumentNullException(nameof(optionsAccessor));

		// OPTIONS
		_options = optionsAccessor.Value ?? throw new NullReferenceException($"No options have been provided via {nameof(optionsAccessor.Value)}.");

		// DUPLICATE OPTIONS (TO AVOID EXTERNAL MODIFICATIONS)
		_options = _options.Duplicate();

		_defaultEntryOptions = _options.DefaultEntryOptions;
		_logger = logger;

		// TRY UPDATE OPTIONS
		_tryUpdateEntryOptions = new FusionCacheEntryOptions
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

		// TAGGING
		_tagsDefaultEntryOptions = _options.TagsDefaultEntryOptions;
		_cascadeRemoveByTagEntryOptions = new FusionCacheEntryOptions
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

		// GLOBALLY UNIQUE INSTANCE ID
		if (string.IsNullOrWhiteSpace(_options.InstanceId))
		{
			_options.SetInstanceIdInternal(FusionCacheInternalUtils.GenerateOperationId());
		}

		// CACHE KEY PREFIX
		if (string.IsNullOrEmpty(_options.CacheKeyPrefix) == false)
			_cacheKeyPrefix = _options.CacheKeyPrefix;

		// Initialize tag-related cache keys
		TagInternalCacheKeyPrefix = GetTagInternalCacheKey("");
		ClearRemoveTagCacheKey = GetTagCacheKey(ClearRemoveTag);
		ClearRemoveTagInternalCacheKey = GetTagInternalCacheKey(ClearRemoveTag);
		ClearExpireTagCacheKey = GetTagCacheKey(ClearExpireTag);
		ClearExpireTagInternalCacheKey = GetTagInternalCacheKey(ClearExpireTag);

		// Validate named cache setup
		ValidateNamedCacheSetup(cacheName, memoryCache);
	}

	/// <summary>
	/// Gets the cache configuration options.
	/// </summary>
	public FusionCacheOptions Options => _options;

	/// <summary>
	/// Gets the cache name from the options.
	/// </summary>
	public string CacheName => _options.CacheName;

	/// <summary>
	/// Gets the unique instance identifier.
	/// </summary>
	public string InstanceId => _options.InstanceId!;

	/// <summary>
	/// Gets the default entry options.
	/// </summary>
	public FusionCacheEntryOptions DefaultEntryOptions => _defaultEntryOptions;

	/// <summary>
	/// Gets the try-update entry options.
	/// </summary>
	public FusionCacheEntryOptions TryUpdateEntryOptions => _tryUpdateEntryOptions;

	/// <summary>
	/// Gets the tags default entry options.
	/// </summary>
	public FusionCacheEntryOptions TagsDefaultEntryOptions => _tagsDefaultEntryOptions;

	/// <summary>
	/// Gets the cascade remove by tag entry options.
	/// </summary>
	public FusionCacheEntryOptions CascadeRemoveByTagEntryOptions => _cascadeRemoveByTagEntryOptions;

	/// <summary>
	/// Creates a new entry options instance by duplicating the default options and applying optional customization.
	/// </summary>
	/// <param name="setupAction">Optional setup action to customize the new options.</param>
	/// <param name="duration">Optional duration to set on the new options.</param>
	/// <returns>A new FusionCacheEntryOptions instance.</returns>
	public FusionCacheEntryOptions CreateEntryOptions(Action<FusionCacheEntryOptions>? setupAction = null, TimeSpan? duration = null)
	{
		var res = _defaultEntryOptions.Duplicate(duration);
		setupAction?.Invoke(res);
		return res;
	}

	/// <summary>
	/// Applies cache key prefix processing if configured.
	/// </summary>
	/// <param name="key">The cache key to potentially modify.</param>
	public void MaybePreProcessCacheKey(ref string key)
	{
		if (_cacheKeyPrefix is not null)
			key = _cacheKeyPrefix + key;
	}

	/// <summary>
	/// Validates that tagging operations are enabled.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when tagging is disabled.</exception>
	public void CheckTaggingEnabled()
	{
		if (_options.DisableTagging)
			throw new InvalidOperationException("This operation requires Tagging, which has been disabled via FusionCacheOptions.DisableTagging.");
	}

	/// <summary>
	/// Validates a cache key for null values.
	/// </summary>
	/// <param name="key">The cache key to validate.</param>
	/// <exception cref="ArgumentNullException">Thrown when key is null.</exception>
	public static void ValidateCacheKey(string key)
	{
		if (key is null)
			throw new ArgumentNullException(nameof(key));
	}

	/// <summary>
	/// Validates a tag for null values.
	/// </summary>
	/// <param name="tag">The tag to validate.</param>
	/// <exception cref="ArgumentNullException">Thrown when tag is null.</exception>
	public static void ValidateTag(string tag)
	{
		if (tag is null)
			throw new ArgumentNullException(nameof(tag));
	}

	/// <summary>
	/// Validates an array of tags and checks if tagging is enabled.
	/// </summary>
	/// <param name="tags">The tags to validate.</param>
	public void ValidateTags(string[]? tags)
	{
		if (tags is null || tags.Length == 0)
			return;

		CheckTaggingEnabled();

		foreach (var tag in tags)
		{
			ValidateTag(tag);
		}
	}

	/// <summary>
	/// Generates a cache key for a tag.
	/// </summary>
	/// <param name="tag">The tag name.</param>
	/// <returns>The formatted tag cache key.</returns>
	public static string GetTagCacheKey(string tag)
	{
		return $"__fc:t:{tag}";
	}

	/// <summary>
	/// Generates an internal cache key for a tag, applying key prefix processing.
	/// </summary>
	/// <param name="tag">The tag name.</param>
	/// <returns>The formatted and processed internal tag cache key.</returns>
	public string GetTagInternalCacheKey(string tag)
	{
		var res = GetTagCacheKey(tag);
		MaybePreProcessCacheKey(ref res);
		return res;
	}

	/// <summary>
	/// Validates setup for named caches and logs warnings if needed.
	/// </summary>
	/// <param name="cacheName">The cache name to validate.</param>
	/// <param name="memoryCache">The memory cache instance.</param>
	private void ValidateNamedCacheSetup(string cacheName, IMemoryCache? memoryCache)
	{
		if (memoryCache is not null && cacheName != FusionCacheOptions.DefaultCacheName && string.IsNullOrWhiteSpace(_options.CacheKeyPrefix))
		{
			if (_logger?.IsEnabled(_options.MissingCacheKeyPrefixWarningLogLevel) ?? false)
				_logger.Log(_options.MissingCacheKeyPrefixWarningLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: a named cache is being used, and no CacheKeyPrefix has been specified. It's usually better to specify a prefix to automatically avoid cache key collisions. If collisions are already avoided when manually creating the cache keys, you can change the MissingCacheKeyPrefixWarningLogLevel option.", cacheName, InstanceId);
		}
	}

	/// <summary>
	/// Generates a unique operation ID for tracing purposes.
	/// </summary>
	/// <returns>A unique operation ID string.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public string MaybeGenerateOperationId()
	{
		return FusionCacheInternalUtils.MaybeGenerateOperationId(_logger);
	}
}