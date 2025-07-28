namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Represents the state of a circuit breaker.
/// </summary>
internal enum CircuitBreakerState
{
	/// <summary>
	/// Circuit is closed and operations are allowed.
	/// </summary>
	Closed = 0,

	/// <summary>
	/// Circuit is open and operations are blocked.
	/// </summary>
	Open = 1,

	/// <summary>
	/// Circuit is half-open and allowing limited operations to test if the service has recovered.
	/// </summary>
	HalfOpen = 2
}