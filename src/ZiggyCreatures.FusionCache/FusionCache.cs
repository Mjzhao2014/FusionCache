using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Internals;
using ZiggyCreatures.Caching.Fusion.Internals.AutoRecovery;
using ZiggyCreatures.Caching.Fusion.Internals.Backplane;
using ZiggyCreatures.Caching.Fusion.Internals.Diagnostics;
using ZiggyCreatures.Caching.Fusion.Internals.Distributed;
using ZiggyCreatures.Caching.Fusion.Internals.Memory;
using ZiggyCreatures.Caching.Fusion.Locking;
using ZiggyCreatures.Caching.Fusion.Plugins;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// The standard implementation of <see cref="IFusionCache"/>.
/// </summary>
[DebuggerDisplay("NAME: {CacheName} - ID: {InstanceId} - DC: {HasDistributedCache} - BP: {HasBackplane}")]
public sealed partial class FusionCache
	: IFusionCache
{
	// underlying options snapshot (duplicated via configuration manager)
	private readonly FusionCacheOptions _options;
	private readonly ILogger<FusionCache>? _logger;
	private readonly FusionCacheConfigurationManager _configurationManager;
	private readonly FusionCacheComponentCoordinator _componentCoordinator;
	private readonly FusionCachePluginManager _pluginManager;
	private readonly string? _cacheKeyPrefix;
	internal readonly FusionCacheEntryOptions _defaultEntryOptions;
	internal readonly FusionCacheEntryOptions _tryUpdateEntryOptions;

	// MEMORY LOCKER (kept for tests)
	private IFusionCacheMemoryLocker _memoryLocker;

	// MEMORY CACHE (kept for tests)
	private MemoryCacheAccessor _mca;
	private readonly bool _mcaCanClear;

	// DISTRIBUTED CACHE (kept for tests)
	private DistributedCacheAccessor? _dca;
	// serializer used for distributed cache and auto-cloning
	private IFusionCacheSerializer? _serializer;

	// BACKPLANE (kept for tests)
	private BackplaneAccessor? _bpa;
	private readonly object _backplaneLock = new();

	// AUTO-RECOVERY is managed in the component coordinator

	// EVENTS
	private FusionCacheEventsHub _events;

	// PLUGINS (kept for tests)
	private List<IFusionCachePlugin>? _plugins;
	private readonly object _pluginsLock = new();

	// TAGGING
	private readonly FusionCacheEntryOptions _tagsDefaultEntryOptions;
	private readonly FusionCacheEntryOptions _cascadeRemoveByTagEntryOptions;

	internal readonly string TagInternalCacheKeyPrefix;

	internal const string ClearRemoveTag = "!";
	internal readonly string ClearRemoveTagCacheKey;
	internal readonly string ClearRemoveTagInternalCacheKey;
	internal long ClearRemoveTimestamp;

	internal const string ClearExpireTag = "*";
	internal readonly string ClearExpireTagCacheKey;
	internal readonly string ClearExpireTagInternalCacheKey;
	internal long ClearExpireTimestamp;

	/// <summary>
	/// Creates a new <see cref="FusionCache"/> instance.
	/// </summary>
	/// <param name="optionsAccessor">The set of cache-wide options to use with this instance of <see cref="FusionCache"/>.</param>
	/// <param name="memoryCache">The <see cref="IMemoryCache"/> instance to use. If null, one will be automatically created and managed.</param>
	/// <param name="logger">The <see cref="ILogger{TCategoryName}"/> instance to use. If null, logging will be completely disabled.</param>
	/// <param name="memoryLocker">The <see cref="IFusionCacheMemoryLocker"/> instance to use. If <see langword="null"/>, a standard one will be automatically created and managed.</param>
	public FusionCache(IOptions<FusionCacheOptions> optionsAccessor, IMemoryCache? memoryCache = null, ILogger<FusionCache>? logger = null, IFusionCacheMemoryLocker? memoryLocker = null)
	{
		if (optionsAccessor is null)
			throw new ArgumentNullException(nameof(optionsAccessor));

		// collapse null logger
		_logger = (logger is NullLogger<FusionCache>) ? null : logger;
		// build configuration manager and pull options snapshot
		_configurationManager = new FusionCacheConfigurationManager(optionsAccessor, _logger);
		_options = _configurationManager.Options;
		_defaultEntryOptions = _configurationManager.DefaultEntryOptions;
		_tryUpdateEntryOptions = _configurationManager.TryUpdateEntryOptions;
		_tagsDefaultEntryOptions = _configurationManager.TagsDefaultEntryOptions;
		_cascadeRemoveByTagEntryOptions = _configurationManager.CascadeRemoveByTagEntryOptions;
		_cacheKeyPrefix = string.IsNullOrEmpty(_options.CacheKeyPrefix) ? null : _options.CacheKeyPrefix;
		// EVENTS
		_events = new FusionCacheEventsHub(this, _options, _logger);
		// COMPONENT COORDINATOR wires up memory cache etc.
		_componentCoordinator = new FusionCacheComponentCoordinator(this, _options, memoryCache, _logger, _events, memoryLocker);
		_memoryLocker = _componentCoordinator.MemoryLocker;
		_mca = _componentCoordinator.MemoryCacheAccessor;
		_mcaCanClear = _componentCoordinator.MemoryCacheCanClear;
		// distributed/backplane start null until explicitly configured
		_dca = null;
		_bpa = null;
		// PLUGINS
		_plugins = [];
		_pluginManager = new FusionCachePluginManager(this, _options, _logger, _plugins);

		if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
			_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}]: instance created", CacheName, InstanceId);

		TagInternalCacheKeyPrefix = GetTagInternalCacheKey("");

		ClearRemoveTimestamp = -1;
		ClearRemoveTagCacheKey = GetTagCacheKey(ClearRemoveTag);
		ClearRemoveTagInternalCacheKey = GetTagInternalCacheKey(ClearRemoveTag);

		ClearExpireTimestamp = -1;
		ClearExpireTagCacheKey = GetTagCacheKey(ClearExpireTag);
		ClearExpireTagInternalCacheKey = GetTagInternalCacheKey(ClearExpireTag);

		// validate configuration for named caches and warn if necessary
		_configurationManager.ValidateNamedCacheSetup(memoryCache);
		// config manager warms up observability sources
	}

	/// <inheritdoc/>
	public string CacheName => _options.CacheName;

	/// <inheritdoc/>
	public string InstanceId => _options.InstanceId!;

	/// <inheritdoc/>
	public FusionCacheEntryOptions DefaultEntryOptions => _defaultEntryOptions;

	internal AutoRecoveryService AutoRecovery => _componentCoordinator.AutoRecovery;

	/// <inheritdoc/>
	public FusionCacheEntryOptions CreateEntryOptions(Action<FusionCacheEntryOptions>? setupAction = null, TimeSpan? duration = null)
	{
		return _configurationManager.CreateEntryOptions(setupAction, duration);
	}

	private static void ValidateCacheKey(string key)
	{
		if (key is null)
			throw new ArgumentNullException(nameof(key));
	}

	private static void ValidateTag(string tag)
	{
		if (tag is null)
			throw new ArgumentNullException(nameof(tag));

		// TODO: SHOULD WE KEEP THIS CHECK, AND SOMEHOW BYPASS IT INTERNALLY?
		//if (tag == ClearTag)
		//	throw new ArgumentOutOfRangeException(nameof(tag), $"The tag '{ClearTag}' is reserved and cannot be used.");
	}

	private void ValidateTags(string[]? tags)
	{
		if (tags is null || tags.Length == 0)
			return;

		CheckTaggingEnabled();

		foreach (var tag in tags)
		{
			ValidateTag(tag);
		}
	}

	private void MaybePreProcessCacheKey(ref string key)
	{
		_configurationManager.MaybePreProcessCacheKey(ref key);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private string MaybeGenerateOperationId()
	{
		return FusionCacheInternalUtils.MaybeGenerateOperationId(_logger);
	}

	// MEMORY ACCESSOR

	internal MemoryCacheAccessor MemoryCacheAccessor
	{
		get { return _mca; }
	}

	// DISTRIBUTED ACCESSOR

	internal DistributedCacheAccessor? DistributedCacheAccessor
	{
		get { return _dca; }
	}

	// BACKPLANE ACCESSOR

	internal BackplaneAccessor? BackplaneAccessor
	{
		get { return _bpa; }
	}

	// FAIL-SAFE

	private IFusionCacheMemoryEntry? TryActivateFailSafe<TValue>(string operationId, string key, FusionCacheDistributedEntry<TValue>? distributedEntry, IFusionCacheMemoryEntry? memoryEntry, MaybeValue<TValue> failSafeDefaultValue, FusionCacheEntryOptions options)
	{
		// FAIL-SAFE NOT ENABLED
		if (options.IsFailSafeEnabled == false)
		{
			if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
				_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): FAIL-SAFE not enabled", CacheName, InstanceId, operationId, key);

			return null;
		}

		// FAIL-SAFE ENABLED
		if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
			_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): trying to activate FAIL-SAFE", CacheName, InstanceId, operationId, key);

		IFusionCacheMemoryEntry? entry = null;

		if (distributedEntry is not null && (memoryEntry is null || distributedEntry.Timestamp > memoryEntry.Timestamp))
		{
			// TRY WITH DISTRIBUTED CACHE ENTRY
			if (_logger?.IsEnabled(_options.FailSafeActivationLogLevel) ?? false)
				_logger.Log(_options.FailSafeActivationLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): FAIL-SAFE activated (from distributed)", CacheName, InstanceId, operationId, key);

			//entry = FusionCacheMemoryEntry<TValue>.CreateFromOtherEntry(distributedEntry, options);
			entry = FusionCacheMemoryEntry<TValue>.CreateFromOptions(distributedEntry.GetValue<TValue>(), distributedEntry.Timestamp, distributedEntry.Tags, options, true, distributedEntry.Metadata?.LastModifiedTimestamp, distributedEntry.Metadata?.ETag);
		}
		else if (memoryEntry is not null && memoryEntry.Metadata is not null)
		{
			// TRY WITH MEMORY CACHE ENTRY
			if (_logger?.IsEnabled(_options.FailSafeActivationLogLevel) ?? false)
				_logger.Log(_options.FailSafeActivationLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): FAIL-SAFE activated (from memory)", CacheName, InstanceId, operationId, key);

			var exp = FusionCacheInternalUtils.GetNormalizedAbsoluteExpirationTimestamp(options.FailSafeThrottleDuration, options, false);

			if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
				_logger?.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): SHIFTING A MEMORY ENTRY FROM {OldExp} TO {NewExp} ({Diff} DIFF)", CacheName, InstanceId, operationId, key, new DateTimeOffset(memoryEntry.LogicalExpirationTimestamp, TimeSpan.Zero), new DateTimeOffset(exp, TimeSpan.Zero), new DateTimeOffset(exp, TimeSpan.Zero) - new DateTimeOffset(memoryEntry.LogicalExpirationTimestamp, TimeSpan.Zero));

			memoryEntry.Metadata.IsStale = true;
			memoryEntry.LogicalExpirationTimestamp = exp;
			memoryEntry.Metadata.EagerExpirationTimestamp = null;
			entry = memoryEntry;
		}
		else if (failSafeDefaultValue.HasValue)
		{
			// TRY WITH FAIL-SAFE DEFAULT VALUE
			if (_logger?.IsEnabled(_options.FailSafeActivationLogLevel) ?? false)
				_logger.Log(_options.FailSafeActivationLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): FAIL-SAFE activated (from fail-safe default value)", CacheName, InstanceId, operationId, key);

			entry = FusionCacheMemoryEntry<TValue>.CreateFromOptions(failSafeDefaultValue.Value, null, null, options, true, null, null);
		}

		if (entry is not null)
		{
			// EVENT
			_events.OnFailSafeActivate(operationId, key);

			return entry;
		}

		// UNABLE TO ACTIVATE FAIL-SAFE
		if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
			_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): unable to activate FAIL-SAFE (no entries in memory or distributed, nor fail-safe default value)", CacheName, InstanceId, operationId, key);

		return null;
	}

	// BACKGROUND FACTORY COMPLETION

	private void MaybeBackgroundCompleteTimedOutFactory<TValue>(string operationId, string key, FusionCacheFactoryExecutionContext<TValue> ctx, Task<TValue>? factoryTask, FusionCacheEntryOptions options, Activity? activity)
	{
		if (factoryTask is null)
		{
			// ACTIVITY
			activity?.Dispose();

			return;
		}

		if (factoryTask.IsFaulted || factoryTask.IsCanceled || ctx.HasFailed)
		{
			// ACTIVITY
			activity?.SetStatus(ActivityStatusCode.Error, factoryTask.Exception?.Message ?? ctx.ErrorMessage ?? "An error occurred while running the factory");
			if (factoryTask.Exception is not null)
				activity?.AddException(factoryTask.Exception);
			activity?.Dispose();

			return;
		}

		if (options.AllowTimedOutFactoryBackgroundCompletion == false)
		{
			// ACTIVITY
			activity?.AddEvent(new ActivityEvent(Activities.EventNames.FactoryBackgroundMoveNotAllowed));
			activity?.Dispose();

			return;
		}

		activity?.AddEvent(new ActivityEvent(Activities.EventNames.FactoryBackgroundMove));
		CompleteBackgroundFactory<TValue>(operationId, key, ctx, factoryTask, options, null, activity);
	}

	private void CompleteBackgroundFactory<TValue>(string operationId, string key, FusionCacheFactoryExecutionContext<TValue> ctx, Task<TValue> factoryTask, FusionCacheEntryOptions options, object? memoryLockObj, Activity? activity)
	{
		if (factoryTask.IsFaulted || factoryTask.IsCanceled || ctx.HasFailed)
		{
			try
			{
				if (_logger?.IsEnabled(_options.FactoryErrorsLogLevel) ?? false)
					_logger.Log(_options.FactoryErrorsLogLevel, factoryTask.Exception?.GetSingleInnerExceptionOrSelf(), "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): a background factory has thrown an exception", CacheName, InstanceId, operationId, key);

				// EVENT
				_events.OnBackgroundFactoryError(operationId, key);
			}
			finally
			{
				// MEMORY LOCK
				if (memoryLockObj is not null)
					ReleaseMemoryLock(operationId, key, memoryLockObj);

				// ACTIVITY
				activity?.SetStatus(ActivityStatusCode.Error, factoryTask.Exception?.Message ?? ctx.ErrorMessage ?? "An error occurred while running the factory");
				if (factoryTask.Exception is not null)
					activity?.AddException(factoryTask.Exception);
				activity?.Dispose();
			}

			return;
		}

		// CONTINUE IN THE BACKGROUND TO TRY TO KEEP THE RESULT AS SOON AS IT WILL COMPLETE SUCCESSFULLY
		if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
			_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): trying to complete a background factory", CacheName, InstanceId, operationId, key);

		_ = factoryTask.ContinueWith(async antecedent =>
		{
			try
			{
				if (antecedent.Status == TaskStatus.Faulted || antecedent.Status == TaskStatus.Canceled || ctx.HasFailed)
				{
					if (_logger?.IsEnabled(_options.FactoryErrorsLogLevel) ?? false)
						_logger.Log(_options.FactoryErrorsLogLevel, antecedent.Exception?.GetSingleInnerExceptionOrSelf(), "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): a background factory thrown an exception", CacheName, InstanceId, operationId, key);

					// ACTIVITY
					activity?.SetStatus(ActivityStatusCode.Error, factoryTask.Exception?.Message ?? ctx.ErrorMessage ?? "An error occurred while running the factory");
					if (factoryTask.Exception is not null)
						activity?.AddException(factoryTask.Exception);
					activity?.Dispose();

					// EVENT
					_events.OnBackgroundFactoryError(operationId, key);
				}
				else if (antecedent.Status == TaskStatus.RanToCompletion)
				{
					if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
						_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): a background factory successfully completed, keeping the result", CacheName, InstanceId, operationId, key);

					// ACTIVITY
					activity?.Dispose();

					// UPDATE ADAPTIVE OPTIONS
					var maybeNewOptions = ctx.GetOptions();
					if (ReferenceEquals(options, maybeNewOptions) == false)
					{
						options = maybeNewOptions;
					}
					else
					{
						options = options.Duplicate();
					}

					options.AllowBackgroundDistributedCacheOperations = false;
					options.AllowBackgroundBackplaneOperations = false;
					options.ReThrowDistributedCacheExceptions = false;
					options.ReThrowSerializationExceptions = false;
					options.ReThrowBackplaneExceptions = false;

					// ADAPTIVE CACHING UPDATE
					var lateEntry = FusionCacheMemoryEntry<TValue>.CreateFromOptions(antecedent.GetAwaiter().GetResult(), null, ctx.Tags, options, false, ctx.LastModified?.UtcTicks, ctx.ETag);

					if (_mca.ShouldWrite(options))
					{
						_mca.SetEntry<TValue>(operationId, key, lateEntry, options);
					}

					if (RequiresDistributedOperations(options))
					{
						await DistributedSetEntryAsync<TValue>(operationId, key, lateEntry, options, default).ConfigureAwait(false);
					}

					// EVENT
					_events.OnBackgroundFactorySuccess(operationId, key);
					_events.OnSet(operationId, key);
				}
			}
			finally
			{
				// MEMORY LOCK
				if (memoryLockObj is not null)
					ReleaseMemoryLock(operationId, key, memoryLockObj);
			}
		});
	}

	// MEMORY LOCKER

	private async ValueTask<object?> AcquireMemoryLockAsync(string operationId, string key, TimeSpan timeout, CancellationToken token)
	{
		if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
			_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): waiting to acquire the LOCK", CacheName, InstanceId, operationId, key);

		var lockObj = await _memoryLocker.AcquireLockAsync(CacheName, InstanceId, operationId, key, timeout, _logger, token);

		if (lockObj is not null)
		{
			// LOCK ACQUIRED
			if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
				_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): LOCK acquired", CacheName, InstanceId, operationId, key);
		}
		else
		{
			// LOCK TIMEOUT
			if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
				_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): LOCK timeout", CacheName, InstanceId, operationId, key);
		}

		return lockObj;
	}

	private object? AcquireMemoryLock(string operationId, string key, TimeSpan timeout, CancellationToken token)
	{
		if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
			_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): waiting to acquire the LOCK", CacheName, InstanceId, operationId, key);

		var lockObj = _memoryLocker.AcquireLock(CacheName, InstanceId, operationId, key, timeout, _logger, token);

		if (lockObj is not null)
		{
			// LOCK ACQUIRED
			if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
				_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): LOCK acquired", CacheName, InstanceId, operationId, key);
		}
		else
		{
			// LOCK TIMEOUT
			if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
				_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): LOCK timeout", CacheName, InstanceId, operationId, key);
		}

		return lockObj;
	}

	private void ReleaseMemoryLock(string operationId, string key, object? lockObj)
	{
		if (lockObj is null)
			return;

		if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
			_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): releasing MEMORY LOCK", CacheName, InstanceId, operationId, key);

		try
		{
			_memoryLocker.ReleaseLock(CacheName, InstanceId, operationId, key, lockObj, _logger);

			if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
				_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): MEMORY LOCK released", CacheName, InstanceId, operationId, key);
		}
		catch (Exception exc)
		{
			if (_logger?.IsEnabled(LogLevel.Warning) ?? false)
				_logger.Log(LogLevel.Warning, exc, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): releasing the MEMORY LOCK has thrown an exception", CacheName, InstanceId, operationId, key);
		}
	}

	// FACTORY STUFF

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ProcessFactoryError(string operationId, string key, Exception exc)
	{
		if (exc is SyntheticTimeoutException)
		{
			if (_logger?.IsEnabled(_options.FactorySyntheticTimeoutsLogLevel) ?? false)
				_logger.Log(_options.FactorySyntheticTimeoutsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): a synthetic timeout occurred while calling the factory", CacheName, InstanceId, operationId, key);

			// EVENT
			_events.OnFactorySyntheticTimeout(operationId, key);

			return;
		}

		if (_logger?.IsEnabled(_options.FactoryErrorsLogLevel) ?? false)
			_logger.Log(_options.FactoryErrorsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): an error occurred while calling the factory", CacheName, InstanceId, operationId, key);

		// EVENT
		_events.OnFactoryError(operationId, key);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ProcessFactoryError(string operationId, string key, string errorMessage)
	{
		if (_logger?.IsEnabled(_options.FactoryErrorsLogLevel) ?? false)
			_logger.Log(_options.FactoryErrorsLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): an error occurred while calling the factory: {ErrorMessage}", CacheName, InstanceId, operationId, key, errorMessage);

		// EVENT
		_events.OnFactoryError(operationId, key);
	}

	internal void RemoveMemoryEntryInternal(string operationId, string key)
	{
		if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
			_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): calling RemoveMemoryEntryInternal", CacheName, InstanceId, operationId, key);

		_mca.RemoveEntry(operationId, key);
	}

	internal void ExpireMemoryEntryInternal(string operationId, string key, long? timestampThreshold)
	{
		if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
			_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): calling ExpireMemoryEntryInternal (timestampThreshold={TimestampThreshold})", CacheName, InstanceId, operationId, key, timestampThreshold);

		_mca.ExpireEntry(operationId, key, timestampThreshold);
	}

	// TAGGING

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void CheckTaggingEnabled()
	{
		_configurationManager.CheckTaggingEnabled();
	}

	private static string GetTagCacheKey(string tag)
	{
		return $"__fc:t:{tag}";
	}

	private string GetTagInternalCacheKey(string tag)
	{
		var res = GetTagCacheKey(tag);
		MaybePreProcessCacheKey(ref res);
		return res;
	}

	internal bool CanExecuteRawClear()
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

		// NOTE: WE MAY THINK ABOUT ALSO CHECKING FOR THE USAGE OF A
		// CacheKeyPrefix, WHICH WOULD *PROBABLY* INDICATE (NOT 100%
		// SURE THOUGH) THAT THE INNER MEMORY CACHE IS BEING SHARED
		// WITH OTHER INSTANCES.
		// THIS IS NOT A STRONG ENOUGH SIGNAL THOUGH, SO IT CANNOT BE USED.
		// 
		// ALSO, WE ALREADY CHECKED, VIA _mca.CanClear(), THAT THE INTERNAL
		// MEMORY CACHE CAN BE CLEARED, WHICH IN TURN ALSO CHECKED THAT THE
		// INNER MEMORY CACHE SHOULD BE DISPOSED, WHICH IN TURN MEANS THAT
		// WE CREATED THE INSTANCE, WHICH IN TURN MEANS THAT NOBODY ELSE CAN
		// BE USING IT.
		// 
		// ALL OF THIS MEANS THAT WE ARE SURE THAT BY CALLING .Clear() WE ARE
		// NOT CLEARING SOMEBODY ELSE'S DATA.

		return true;
	}

	internal bool TryExecuteRawClear(string operationId)
	{
		if (CanExecuteRawClear() == false)
		{
			if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
				_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): it was not possible to execute a raw clear", _options.CacheName, _options.InstanceId, operationId);

			return false;
		}

		if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
			_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): executing a raw clear", _options.CacheName, _options.InstanceId, operationId);

		return _mca.TryClear();
	}

	// SERIALIZATION

	/// <inheritdoc/>
	public IFusionCache SetupSerializer(IFusionCacheSerializer serializer)
	{
		// keep local serializer for auto-clone scenarios, and pass down to component coordinator
		_serializer = serializer;
		_componentCoordinator.SetupSerializer(serializer);
		return this;
	}

	// DISTRIBUTED CACHE

	/// <inheritdoc/>
	public IFusionCache SetupDistributedCache(IDistributedCache distributedCache)
	{
		_componentCoordinator.SetupDistributedCache(distributedCache);
		// keep local field in sync for tests
		_dca = _componentCoordinator.DistributedCacheAccessor;
		return this;
	}

	/// <inheritdoc/>
	public IFusionCache SetupDistributedCache(IDistributedCache distributedCache, IFusionCacheSerializer serializer)
	{
		// ensure serializer is set at both fusion cache and coordinator, then setup distributed cache
		SetupSerializer(serializer);
		SetupDistributedCache(distributedCache);
		return this;
	}

	/// <inheritdoc/>
	public IFusionCache RemoveDistributedCache()
	{
		_componentCoordinator.RemoveDistributedCache();
		_dca = null;
		return this;
	}

	/// <inheritdoc/>
	public bool HasDistributedCache => _componentCoordinator.HasDistributedCache;
	/// <inheritdoc/>
	public IDistributedCache? DistributedCache => _componentCoordinator.DistributedCache;

	// BACKPLANE

	/// <inheritdoc/>
	public IFusionCache SetupBackplane(IFusionCacheBackplane backplane)
	{
		// delegate to component coordinator
		_componentCoordinator.SetupBackplane(backplane);
		_bpa = _componentCoordinator.BackplaneAccessor;
		return this;
	}

		// (old backplane setup code removed)

	/// <inheritdoc/>
	public IFusionCache RemoveBackplane()
	{
		_componentCoordinator.RemoveBackplane();
		_bpa = null;
		return this;
	}

	/// <inheritdoc/>
	public bool HasBackplane => _componentCoordinator.HasBackplane;
	/// <inheritdoc/>
	public IFusionCacheBackplane? Backplane => _componentCoordinator.Backplane;

	// EVENTS

	/// <inheritdoc/>
	public FusionCacheEventsHub Events { get { return _events; } }

	// PLUGINS

	/// <inheritdoc/>
	public void AddPlugin(IFusionCachePlugin plugin)
	{
		_pluginManager.AddPlugin(plugin);
	}

	/// <inheritdoc/>
	public bool RemovePlugin(IFusionCachePlugin plugin)
	{
		return _pluginManager.RemovePlugin(plugin);
	}

	// DISTRIBUTED OPERATIONS

	internal bool RequiresDistributedOperations(FusionCacheEntryOptions options)
	{
		if (HasDistributedCache && options.SkipDistributedCacheRead == false && options.SkipDistributedCacheWrite == false)
			return true;

		if (HasBackplane && options.SkipBackplaneNotifications == false)
			return true;

		return false;
	}

	internal bool MustAwaitDistributedOperations(FusionCacheEntryOptions options)
	{
		if (HasDistributedCache && options.AllowBackgroundDistributedCacheOperations == false)
			return true;

		if (HasDistributedCache == false && HasBackplane && options.AllowBackgroundBackplaneOperations == false)
			return true;

		return false;
	}

	internal bool MustAwaitBackplaneOperations(FusionCacheEntryOptions options)
	{
		if (HasBackplane && options.AllowBackgroundBackplaneOperations == false)
			return true;

		return false;
	}

	// ADAPTIVE CACHING

	private void UpdateAdaptiveOptions<TValue>(FusionCacheFactoryExecutionContext<TValue> ctx, ref FusionCacheEntryOptions options)
	{
		// UPDATE ADAPTIVE OPTIONS
		var maybeNewOptions = ctx.GetOptions();

		if (ReferenceEquals(options, maybeNewOptions))
			return;

		options = maybeNewOptions;
	}

	// INTERNAL UPDATES

	internal TValue GetValueFromMemoryEntry<TValue>(string operationId, string key, IFusionCacheMemoryEntry entry, FusionCacheEntryOptions? options)
	{
		options ??= _defaultEntryOptions;

		if (options.EnableAutoClone == false)
			return entry.GetValue<TValue>();

		if (_serializer is null)
			throw new InvalidOperationException($"A serializer is needed when using {nameof(FusionCacheEntryOptions.EnableAutoClone)}.");

		if (entry.Value is null)
			return entry.GetValue<TValue>();

		if (_options.SkipAutoCloneForImmutableObjects && ImmutableTypeCache<TValue>.IsImmutable)
			return entry.GetValue<TValue>();

		byte[] serializedValue;
		try
		{
			serializedValue = entry.GetSerializedValue(_serializer);
		}
		catch (Exception exc)
		{
			if (_logger?.IsEnabled(_options.SerializationErrorsLogLevel) ?? false)
				_logger.Log(_options.SerializationErrorsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [DC] an error occurred while serializing a value", _options.CacheName, _options.InstanceId, operationId, key);

			// EVENT
			_events.Distributed.OnSerializationError(operationId, key);

			if (_options.ReThrowOriginalExceptions)
			{
				throw;
			}
			else
			{
				throw new FusionCacheSerializationException("An error occurred while serializing a value", exc);
			}
		}

		try
		{
			return _serializer.Deserialize<TValue>(serializedValue)!;
		}
		catch (Exception exc)
		{
			if (_logger?.IsEnabled(_options.SerializationErrorsLogLevel) ?? false)
				_logger.Log(_options.SerializationErrorsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [DC] an error occurred while deserializing a value", _options.CacheName, _options.InstanceId, operationId, key);

			// EVENT
			_events.Distributed.OnDeserializationError(operationId, key);

			if (_options.ReThrowOriginalExceptions)
			{
				throw;
			}
			else
			{
				throw new FusionCacheSerializationException("An error occurred while deserializing a value", exc);
			}
		}
	}

	// IDISPOSABLE

	private bool _disposedValue = false;

	/// <summary>
	/// Release all resources managed by FusionCache.
	/// </summary>
	/// <param name="disposing">Indicates if the disposing is happening.</param>
	private void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				// plugin manager will stop and remove all plugins
				_pluginManager.RemoveAllPlugins();
				// coordinator will unsubscribe backplane, remove distributed cache, dispose auto-recovery and memory accessor
				_componentCoordinator.RemoveBackplane();
				_componentCoordinator.RemoveDistributedCache();
				_componentCoordinator.Dispose();

	#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
				_memoryLocker = null;
				_mca = null;
				_events = null;
	#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
			}

			_disposedValue = true;
		}
	}

	/// <summary>
	/// Release all resources managed by FusionCache.
	/// </summary>
	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}
}
