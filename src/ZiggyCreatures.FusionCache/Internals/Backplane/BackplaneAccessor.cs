using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion.Internals.Backplane;

internal sealed partial class BackplaneAccessor
{
	private readonly FusionCache _cache;
	private readonly IFusionCacheBackplane _backplane;
	private readonly FusionCacheOptions _options;
	private readonly ILogger? _logger;
	private readonly FusionCacheBackplaneEventsHub _events;
	private readonly IFusionCacheCircuitBreaker _breaker;

	public BackplaneAccessor(FusionCache cache, IFusionCacheBackplane backplane, FusionCacheOptions options, ILogger? logger)
	{
		if (cache is null)
			throw new ArgumentNullException(nameof(cache));

		if (backplane is null)
			throw new ArgumentNullException(nameof(backplane));

		_cache = cache;
		_backplane = backplane;

		_options = options;

		_logger = logger;
		_events = _cache.Events.Backplane;

		// CIRCUIT-BREAKER
		if (options.BackplaneCircuitBreakerFailureThreshold > 0)
		{
			_breaker = new AdvancedCircuitBreaker(options.BackplaneCircuitBreakerFailureThreshold, options.BackplaneCircuitBreakerSamplingDuration, options.BackplaneCircuitBreakerMinimumThroughput, options.BackplaneCircuitBreakerDuration, options.BackplaneCircuitBreakerJitterMaxDuration);
		}
		else
		{
			_breaker = new SimpleCircuitBreaker(options.BackplaneCircuitBreakerFailuresAllowedBeforeBreaking, options.BackplaneCircuitBreakerDuration, options.BackplaneCircuitBreakerJitterMaxDuration);
		}
	}

	public IFusionCacheBackplane Backplane
	{
		get { return _backplane; }
	}

	/// <summary>
	/// Gets the current circuit breaker state for testing purposes.
	/// </summary>
	internal CircuitBreakerState CircuitBreakerState => _breaker.State;

	/// <summary>
	/// Gets the current failure count for testing purposes.
	/// </summary>
	internal int CircuitBreakerFailureCount => _breaker.CurrentFailureCount;

	private bool TryBeginOperation(string? operationId, string? key)
	{
		if (_backplane is null)
			return false;
		var canExec = _breaker.TryExecute(out var stateChanged);
		if (stateChanged)
		{
			if (_logger?.IsEnabled(LogLevel.Warning) ?? false)
			{
				if (_breaker.State == CircuitBreakerState.Open)
				{
					_logger.Log(LogLevel.Warning, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [BP] backplane temporarily de-activated for {BreakDuration}", _cache.CacheName, _cache.InstanceId, operationId, key, _options.BackplaneCircuitBreakerDuration);
				}
				else if (_breaker.State == CircuitBreakerState.HalfOpen)
				{
					_logger.Log(LogLevel.Warning, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [BP] backplane circuit breaker half-open", _cache.CacheName, _cache.InstanceId, operationId, key);
				}
			}
			_events.OnCircuitBreakerChange(operationId, key, _breaker.State);
		}
		return canExec;
	}

	public bool IsCurrentlyUsable(string? operationId, string? key)
	{
		var res = _breaker.State == CircuitBreakerState.Closed;
		if (res == false)
		{
			res = _breaker.TryExecute(out var stateChanged);
			if (stateChanged)
			{
				_events.OnCircuitBreakerChange(operationId, key, _breaker.State);
			}
		}
		return res;
	}

	private void ProcessError(string operationId, string key, Exception exc, string actionDescription)
	{
		if (exc is SyntheticTimeoutException)
		{
			if (_logger?.IsEnabled(_options.BackplaneSyntheticTimeoutsLogLevel) ?? false)
				_logger.Log(_options.BackplaneSyntheticTimeoutsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [BP] a synthetic timeout occurred while " + actionDescription, _cache.CacheName, _cache.InstanceId, operationId, key);

			return;
		}

		// circuit breaker failure already recorded in Publish
		if (_logger?.IsEnabled(_options.BackplaneErrorsLogLevel) ?? false)
			_logger.Log(_options.BackplaneErrorsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [BP] an error occurred while " + actionDescription, _cache.CacheName, _cache.InstanceId, operationId, key);
	}

	private bool CheckMessage(string operationId, BackplaneMessage message, bool isAutoRecovery)
	{
		// CHECK: IGNORE NULL
		if (message is null)
		{
			if (_logger?.IsEnabled(_options.BackplaneErrorsLogLevel) ?? false)
				_logger.Log(_options.BackplaneErrorsLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): [BP] cannot send a null backplane message (what!?)", _cache.CacheName, _cache.InstanceId, operationId);

			return false;
		}

		// CHECK: IS VALID
		if (message.IsValid() == false)
		{
			// IGNORE INVALID MESSAGES
			if (_logger?.IsEnabled(LogLevel.Warning) ?? false)
				_logger.Log(LogLevel.Warning, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [BP] cannot send an invalid backplane message" + isAutoRecovery.ToString(" (auto-recovery)"), _cache.CacheName, _cache.InstanceId, operationId, message.CacheKey);

			return false;
		}

		// CHECK: WRONG SOURCE ID
		if (message.SourceId != _cache.InstanceId)
		{
			// IGNORE MESSAGES -NOT- FROM THIS SOURCE
			if (_logger?.IsEnabled(LogLevel.Warning) ?? false)
				_logger.Log(LogLevel.Warning, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [BP] cannot send a backplane message" + isAutoRecovery.ToString(" (auto-recovery)") + " with a SourceId different than the local one (IFusionCache.InstanceId)", _cache.CacheName, _cache.InstanceId, operationId, message.CacheKey);

			return false;
		}

		return true;
	}
}
