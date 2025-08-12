namespace ZiggyCreatures.Caching.Fusion.Internals
{
	/// <summary>
	/// Represents the current state of a circuit breaker.
	/// </summary>
public enum CircuitBreakerState
	{
		/// <summary>
		/// The circuit is closed and operations are allowed.
		/// </summary>
		Closed = 0,
		/// <summary>
		/// The circuit is open and operations are blocked.
		/// </summary>
		Open = 1,
		/// <summary>
		/// The circuit is half-open and exactly one operation is allowed to test whether the underlying system has recovered.
		/// </summary>
		HalfOpen = 2,
	}
}
