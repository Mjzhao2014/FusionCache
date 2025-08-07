using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace ZiggyCreatures.Caching.Fusion.Internals.Distributed;

internal sealed partial class DistributedCacheAccessor
{
	private readonly IDistributedCache _cache;
	private readonly IFusionCacheSerializer _serializer;
	private readonly FusionCacheOptions _options;
	private readonly ILogger? _logger;
	private readonly FusionCacheDistributedEventsHub _events;
	private readonly IFusionCacheCircuitBreaker _breaker;
	private readonly string _wireFormatToken;

	public DistributedCacheAccessor(IDistributedCache distributedCache, IFusionCacheSerializer serializer, FusionCacheOptions options, ILogger? logger, FusionCacheDistributedEventsHub events)
	{
		if (distributedCache is null)
			throw new ArgumentNullException(nameof(distributedCache));

		if (serializer is null)
			throw new ArgumentNullException(nameof(serializer));

		_cache = distributedCache;
		_serializer = serializer;

		_options = options;

		_logger = logger;
		_events = events;

		// CIRCUIT-BREAKER
		// If an advanced circuit breaker is configured (failure threshold > 0), use it; otherwise, fall back to simple consecutive-failure-based break.
		if (options.DistributedCacheCircuitBreakerFailureThreshold > 0)
		{
			_breaker = new AdvancedCircuitBreaker(options.DistributedCacheCircuitBreakerFailureThreshold, options.DistributedCacheCircuitBreakerSamplingDuration, options.DistributedCacheCircuitBreakerMinimumThroughput, options.DistributedCacheCircuitBreakerDuration);
		}
		else
		{
			_breaker = new SimpleCircuitBreaker(options.DistributedCacheCircuitBreakerDuration, options.DistributedCacheCircuitBreakerFailuresAllowedBeforeBreaking);
		}

		// WIRE FORMAT SETUP
		_wireFormatToken = _options.DistributedCacheKeyModifierMode == CacheKeyModifierMode.Prefix
			? (FusionCacheOptions.DistributedCacheWireFormatVersion + FusionCacheOptions.DistributedCacheWireFormatSeparator)
			: _options.DistributedCacheKeyModifierMode == CacheKeyModifierMode.Suffix
				? FusionCacheOptions.DistributedCacheWireFormatSeparator + FusionCacheOptions.DistributedCacheWireFormatVersion
				: string.Empty;

		_wireFormatToken = _options.DistributedCacheKeyModifierMode switch
		{
			CacheKeyModifierMode.Prefix => FusionCacheOptions.DistributedCacheWireFormatVersion + FusionCacheOptions.DistributedCacheWireFormatSeparator,
			CacheKeyModifierMode.Suffix => FusionCacheOptions.DistributedCacheWireFormatSeparator + FusionCacheOptions.DistributedCacheWireFormatVersion,
			CacheKeyModifierMode.None => string.Empty,
			_ => throw new NotImplementedException(),
		};
	}

	public IDistributedCache DistributedCache
	{
		get { return _cache; }
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private string MaybeProcessCacheKey(string key)
	{
		return _options.DistributedCacheKeyModifierMode switch
		{
			CacheKeyModifierMode.Prefix => _wireFormatToken + key,
			CacheKeyModifierMode.Suffix => key + _wireFormatToken,
			_ => key,
		};
	}

	private void RecordBreakerFailure(string operationId, string key)
	{
		if (_cache is null)
			return;
		var opened = false;
		var stateChanged = false;
		_breaker.RecordFailure(out stateChanged);
		opened = _breaker.State == CircuitBreakerState.Open;
		if (opened && stateChanged)
		{
			if (_logger?.IsEnabled(LogLevel.Warning) ?? false)
			{
				_logger.Log(LogLevel.Warning, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [DC] distributed cache temporarily de-activated for {BreakDuration}",
					_options.CacheName, _options.InstanceId, operationId, key, _options.DistributedCacheCircuitBreakerDuration);
			}
			_events.OnCircuitBreakerChange(operationId, key, false);
		}
	}

	public bool IsCurrentlyUsable(string? operationId, string? key)
	{
		var canExecute = _breaker.TryExecute(out var stateChanged);
		// If the breaker transitioned back to closed after a half-open probe succeeded, log reactivation
		if (stateChanged && _breaker.State == CircuitBreakerState.Closed)
		{
			if (_logger?.IsEnabled(LogLevel.Warning) ?? false)
				_logger.Log(LogLevel.Warning, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [DC] distributed cache activated again", _options.CacheName, _options.InstanceId, operationId, key);
			_events.OnCircuitBreakerChange(operationId, key, true);
		}
		return canExecute;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ProcessError(string operationId, string key, Exception exc, string actionDescription)
	{
		if (exc is SyntheticTimeoutException)
		{
			if (_logger?.IsEnabled(_options.DistributedCacheSyntheticTimeoutsLogLevel) ?? false)
				_logger.Log(_options.DistributedCacheSyntheticTimeoutsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [DC] a synthetic timeout occurred while " + actionDescription, _options.CacheName, _options.InstanceId, operationId, key);
			return;
		}
		// increment breaker failure count and possibly open circuit
		RecordBreakerFailure(operationId, key);
		if (_logger?.IsEnabled(_options.DistributedCacheErrorsLogLevel) ?? false)
			_logger.Log(_options.DistributedCacheErrorsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [DC] an error occurred while " + actionDescription, _options.CacheName, _options.InstanceId, operationId, key);
	}
}
