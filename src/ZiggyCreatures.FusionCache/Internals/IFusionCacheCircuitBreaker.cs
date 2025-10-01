using System;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Common interface for circuit breaker implementations used by FusionCache.
/// </summary>
internal interface IFusionCacheCircuitBreaker
{
	/// <summary>
	/// Gets the current state of the circuit breaker.
	/// </summary>
	CircuitBreakerState State { get; }

	/// <summary>
	/// Try to execute an operation guarded by this circuit breaker.
	/// </summary>
	/// <param name="isStateChanged">True when the call caused a state transition.</param>
	/// <returns>True if the operation should proceed, false when the circuit is open.</returns>
	bool TryExecute(out bool isStateChanged);

	/// <summary>
	/// Records a successful execution through the breaker.
	/// </summary>
	/// <param name="isStateChanged">True when the call caused a state transition.</param>
	void RecordSuccess(out bool isStateChanged);

	/// <summary>
	/// Records a failed execution through the breaker.
	/// </summary>
	/// <param name="isStateChanged">True when the call caused a state transition.</param>
	void RecordFailure(out bool isStateChanged);

	/// <summary>
	/// Closes the circuit and resets internal counts.
	/// </summary>
	/// <param name="isStateChanged">True when the call caused a state transition.</param>
	void Close(out bool isStateChanged);

	/// <summary>
	/// Gets the current failure count used by the breaker. This value resets when the breaker closes.
	/// For simple circuit breakers it is the number of consecutive failures, for advanced circuit breakers
	/// it is the number of failures in the current sampling window.
	/// </summary>
	int CurrentFailureCount { get; }
}
