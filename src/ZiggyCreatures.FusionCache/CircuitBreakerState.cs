namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Represents the current state of a circuit breaker.
/// </summary>
public enum CircuitBreakerState
{
	/// <summary>
	/// The circuit is closed: all calls are allowed.
	/// </summary>
	Closed = 0,
	/// <summary>
	/// The circuit is open: no calls are allowed until the configured break duration has elapsed.
	/// </summary>
	Open = 1,
	/// <summary>
	/// The circuit is half open: exactly one call is allowed to test if the circuit can be closed again.
	/// </summary>
	HalfOpen = 2,
}
