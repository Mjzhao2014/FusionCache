namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// Represents the current state of a circuit breaker.
	/// </summary>
	public enum CircuitBreakerState
	{
		/// <summary>
		/// The circuit is closed and operational; all calls are allowed through.
		/// </summary>
		Closed = 0,
		/// <summary>
		/// The circuit is open and tripped; calls will not be executed until the break duration has expired.
		/// </summary>
		Open = 1,
		/// <summary>
		/// The circuit is half–open: one call is being tested to determine whether the circuit can be closed again.
		/// </summary>
		HalfOpen = 2
	}
}
