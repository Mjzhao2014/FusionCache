namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Abstraction for a circuit breaker used by FusionCache to protect external dependencies like the distributed cache or backplane.
/// </summary>
internal interface IFusionCacheCircuitBreaker
{
	/// <summary>
	/// The current state of the circuit breaker.
	/// </summary>
	CircuitBreakerState State { get; }

	/// <summary>
	/// Tries to execute an operation through the circuit breaker.
	/// </summary>
	/// <param name="isStateChanged">Indicates if the circuit breaker state changed during this operation.</param>
	/// <returns><see langword="true"/> if the operation can proceed; <see langword="false"/> if the circuit is open and the operation should be skipped.</returns>
	bool TryExecute(out bool isStateChanged);

	/// <summary>
	/// Records a successful operation execution.
	/// </summary>
	/// <param name="isStateChanged">Indicates if the circuit breaker state changed due to this success.</param>
	void RecordSuccess(out bool isStateChanged);

	/// <summary>
	/// Records a failed operation execution.
	/// </summary>
	/// <param name="isStateChanged">Indicates if the circuit breaker state changed due to this failure.</param>
	void RecordFailure(out bool isStateChanged);

	/// <summary>
	/// Manually closes the circuit breaker.
	/// </summary>
	/// <param name="isStateChanged">Indicates if the circuit breaker state changed during this operation.</param>
	void Close(out bool isStateChanged);

	/// <summary>
	/// Gets the current failure count within the sampling window.
	/// </summary>
	int CurrentFailureCount { get; }
}
