using ZiggyCreatures.Caching.Fusion;
namespace ZiggyCreatures.Caching.Fusion.Events;

/// <summary>
/// The specific <see cref="EventArgs"/> object for events related to opening/closing of a circuit breaker.
/// </summary>
public class FusionCacheCircuitBreakerChangeEventArgs : EventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="FusionCacheCircuitBreakerChangeEventArgs"/> class with the current state.
	/// </summary>
	/// <param name="state">The new state of the circuit breaker.</param>
	public FusionCacheCircuitBreakerChangeEventArgs(CircuitBreakerState state)
	{
		State = state;
	}

	/// <summary>
	/// Gets the new state of the circuit breaker.
	/// </summary>
	public CircuitBreakerState State { get; }
}
