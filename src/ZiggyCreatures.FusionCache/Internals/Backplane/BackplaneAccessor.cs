using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Events;

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
		_breaker = new SimpleCircuitBreaker(options.BackplaneCircuitBreakerFailuresAllowedBeforeBreaking, options.BackplaneCircuitBreakerDuration, options.BackplaneCircuitBreakerJitterMaxDuration);
	}

	public IFusionCacheBackplane Backplane
	{
		get { return _backplane; }
	}

	private bool TryBeginOperation(string? operationId, string? key)
	{
		var canExecute = _breaker.TryExecute(out var stateChanged);
		if (stateChanged)
		{
			if (_logger?.IsEnabled(LogLevel.Warning) ?? false)
			{
				if (_breaker.State == CircuitBreakerState.Open)
				{
					_logger.Log(LogLevel.Warning, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [BP] backplane temporarily de-activated", _cache.CacheName, _cache.InstanceId, operationId, key);
				}
				else if (_breaker.State == CircuitBreakerState.HalfOpen)
				{
					_logger.Log(LogLevel.Warning, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [BP] backplane entering half-open state", _cache.CacheName, _cache.InstanceId, operationId, key);
				}
				else
				{
					_logger.Log(LogLevel.Warning, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [BP] backplane activated again", _cache.CacheName, _cache.InstanceId, operationId, key);
				}
			}
			_events.OnCircuitBreakerChange(operationId, key, _breaker.State);
		}
		return canExecute;
	}

	internal CircuitBreakerState CircuitBreakerState => _breaker.State;
	internal int CircuitBreakerFailureCount => _breaker.CurrentFailureCount;

	/// <summary>
	/// Exposes the previous semantics of checking whether the backplane is currently usable by deferring to <see cref="TryBeginOperation(string?, string?)"/>.
	/// </summary>
	public bool IsCurrentlyUsable(string? operationId, string? key)
	{
		return TryBeginOperation(operationId, key);
	}

	private void ProcessError(string operationId, string key, Exception exc, string actionDescription)
	{
		if (exc is SyntheticTimeoutException)
		{
			if (_logger?.IsEnabled(_options.BackplaneSyntheticTimeoutsLogLevel) ?? false)
				_logger.Log(_options.BackplaneSyntheticTimeoutsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [BP] a synthetic timeout occurred while " + actionDescription, _cache.CacheName, _cache.InstanceId, operationId, key);

			return;
		}
		_breaker.RecordFailure(out var stateChanged);
		if (stateChanged)
		{
			_events.OnCircuitBreakerChange(operationId, key, _breaker.State);
			if (_breaker.State == CircuitBreakerState.Open && (_logger?.IsEnabled(LogLevel.Warning) ?? false))
			{
				_logger.Log(LogLevel.Warning, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [BP] backplane temporarily de-activated", _cache.CacheName, _cache.InstanceId, operationId, key);
			}
		}
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
