namespace ZiggyCreatures.Caching.Fusion.Internals
{
	/// <summary>
	/// The possible states for a circuit-breaker.
	/// </summary>
	public enum CircuitBreakerState
	{
		/// <summary>
		/// The circuit is closed and operations are allowed to proceed.
		/// </summary>
		Closed = 0,
		/// <summary>
		/// The circuit is open and operations are blocked until the break duration has elapsed.
		/// </summary>
		Open = 1,
		/// <summary>
		/// The circuit is half-open and exactly one trial operation is allowed to determine if the dependency has recovered.
		/// </summary>
		HalfOpen = 2
	}
}
