namespace ZiggyCreatures.Caching.Fusion.Events;

using ZiggyCreatures.Caching.Fusion;
/// <summary>
/// The specific <see cref="EventArgs"/> object for events related to circuit-breaker state changes.
/// </summary>
public class FusionCacheCircuitBreakerChangeEventArgs : EventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="FusionCacheCircuitBreakerChangeEventArgs"/> class.
	/// </summary>
	/// <param name="state">The current state of the circuit breaker.</param>
	public FusionCacheCircuitBreakerChangeEventArgs(CircuitBreakerState state)
	{
		State = state;
	}

	/// <summary>
	/// The new state of the circuit breaker.
	/// </summary>
	public CircuitBreakerState State { get; }

	/// <summary>
	/// A convenience flag that indicates if the circuit breaker is in the closed state.
	/// </summary>
	public bool IsClosed => State == CircuitBreakerState.Closed;
}
