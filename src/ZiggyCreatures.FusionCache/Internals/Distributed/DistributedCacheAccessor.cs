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
		_breaker = new SimpleCircuitBreaker(options.DistributedCacheCircuitBreakerFailuresAllowedBeforeBreaking, options.DistributedCacheCircuitBreakerDuration, options.DistributedCacheCircuitBreakerJitterMaxDuration);

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

	private bool TryBeginOperation(string? operationId, string? key)
	{
		var canExecute = _breaker.TryExecute(out var stateChanged);
		if (stateChanged)
		{
			// log state change
			if (_logger?.IsEnabled(LogLevel.Warning) ?? false)
			{
				if (_breaker.State == CircuitBreakerState.Open)
				{
					_logger.Log(LogLevel.Warning, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [DC] distributed cache temporarily de-activated", _options.CacheName, _options.InstanceId, operationId, key);
				}
				else if (_breaker.State == CircuitBreakerState.HalfOpen)
				{
					_logger.Log(LogLevel.Warning, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [DC] distributed cache entering half-open state", _options.CacheName, _options.InstanceId, operationId, key);
				}
				else
				{
					_logger.Log(LogLevel.Warning, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [DC] distributed cache activated again", _options.CacheName, _options.InstanceId, operationId, key);
				}
			}
			_events.OnCircuitBreakerChange(operationId, key, _breaker.State);
		}
		return canExecute;
	}

	public CircuitBreakerState CircuitBreakerState => _breaker.State;
	public int CircuitBreakerFailureCount => _breaker.CurrentFailureCount;

	/// <summary>
	/// Exposes the previous semantics of checking whether the circuit breaker is closed and the distributed cache can be used.
	/// This method simply defers to <see cref="TryBeginOperation(string?, string?)"/>.
	/// </summary>
	public bool IsCurrentlyUsable(string? operationId, string? key)
	{
		return TryBeginOperation(operationId, key);
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

		// record a failure and check for state transition
		_breaker.RecordFailure(out var stateChanged);
		if (stateChanged)
		{
			_events.OnCircuitBreakerChange(operationId, key, _breaker.State);
			if (_breaker.State == CircuitBreakerState.Open && (_logger?.IsEnabled(LogLevel.Warning) ?? false))
			{
				_logger.Log(LogLevel.Warning, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [DC] distributed cache temporarily de-activated", _options.CacheName, _options.InstanceId, operationId, key);
			}
		}

		if (_logger?.IsEnabled(_options.DistributedCacheErrorsLogLevel) ?? false)
			_logger.Log(_options.DistributedCacheErrorsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [DC] an error occurred while " + actionDescription, _options.CacheName, _options.InstanceId, operationId, key);
	}
}
