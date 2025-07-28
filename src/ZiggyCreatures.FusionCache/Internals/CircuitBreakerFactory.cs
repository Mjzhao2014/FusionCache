namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Factory for creating circuit breaker instances based on configuration.
/// </summary>
internal static class CircuitBreakerFactory
{
	/// <summary>
	/// Creates a circuit breaker instance based on the provided options.
	/// </summary>
	/// <param name="options">The FusionCache options containing circuit breaker configuration.</param>
	/// <returns>A circuit breaker instance (either simple or advanced based on configuration).</returns>
	public static IFusionCacheCircuitBreaker CreateDistributedCacheCircuitBreaker(FusionCacheOptions options)
	{
		if (options.EnableAdvancedDistributedCacheCircuitBreaker)
		{
			var advancedOptions = new AdvancedCircuitBreakerOptions
			{
				FailureThreshold = options.DistributedCacheCircuitBreakerFailureThreshold,
				SamplingDuration = options.DistributedCacheCircuitBreakerSamplingDuration,
				DurationOfBreak = options.DistributedCacheCircuitBreakerDuration,
				MinimumThroughput = options.DistributedCacheCircuitBreakerMinimumThroughput
			};
			
			return new AdvancedCircuitBreaker(advancedOptions);
		}
		
		return new SimpleCircuitBreaker(options.DistributedCacheCircuitBreakerDuration);
	}
}