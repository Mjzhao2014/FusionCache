namespace ZiggyCreatures.Caching.Fusion.Events;

using ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// The specific <see cref="EventArgs"/> object for events related to changes in a circuit breaker.
/// </summary>
public class FusionCacheCircuitBreakerChangeEventArgs : EventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="FusionCacheCircuitBreakerChangeEventArgs"/> class.
	/// </summary>
	/// <param name="state">The new state of the circuit breaker.</param>
	public FusionCacheCircuitBreakerChangeEventArgs(CircuitBreakerState state)
	{
		State = state;
	}

	/// <summary>
	/// The new state of the circuit breaker.
	/// </summary>
	public CircuitBreakerState State { get; }

	/// <summary>
	/// Indicates whether the circuit breaker is closed in the new state.
	/// </summary>
	public bool IsClosed => State == CircuitBreakerState.Closed;
}
