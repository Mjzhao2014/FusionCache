namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Configuration options for the advanced circuit breaker.
/// </summary>
internal sealed class AdvancedCircuitBreakerOptions
{
	/// <summary>
	/// Creates a new instance of <see cref="AdvancedCircuitBreakerOptions"/> with default values.
	/// </summary>
	public AdvancedCircuitBreakerOptions()
	{
		FailureThreshold = 5;
		SamplingDuration = TimeSpan.FromMinutes(1);
		DurationOfBreak = TimeSpan.FromSeconds(30);
	}

	/// <summary>
	/// The number of failures within the sampling duration that will cause the circuit to open.
	/// Default is 5.
	/// </summary>
	public int FailureThreshold { get; set; }

	/// <summary>
	/// The time window for counting failures. Failures older than this duration are ignored.
	/// Default is 1 minute.
	/// </summary>
	public TimeSpan SamplingDuration { get; set; }

	/// <summary>
	/// The duration the circuit will remain open before transitioning to half-open.
	/// Default is 30 seconds.
	/// </summary>
	public TimeSpan DurationOfBreak { get; set; }

	/// <summary>
	/// The minimum number of calls that must occur during the sampling window 
	/// before it evaluates whether the failure rate is too high.
	/// </summary>
	public int MinimumThroughput { get; set; } = 10;
	/// <summary>
	/// Creates a copy of the current options.
	/// </summary>
	/// <returns>A new instance with the same values.</returns>
	public AdvancedCircuitBreakerOptions Clone()
	{
		return new AdvancedCircuitBreakerOptions
		{
			FailureThreshold = FailureThreshold,
			SamplingDuration = SamplingDuration,
			DurationOfBreak = DurationOfBreak,
			MinimumThroughput = MinimumThroughput
		};
	}
}