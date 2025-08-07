namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Represents the current state of a circuit breaker.
/// </summary>
internal enum CircuitBreakerState
{
	/// <summary>
	/// The circuit is closed and operations can proceed.
	/// </summary>
	Closed,
	/// <summary>
	/// The circuit is open and operations will be blocked.
	/// </summary>
	Open,
	/// <summary>
	/// The circuit has transitioned from open and is allowing a single test call to
	/// determine whether it should close or open again.
	/// </summary>
	HalfOpen
}
