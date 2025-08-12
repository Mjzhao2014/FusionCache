namespace ZiggyCreatures.Caching.Fusion.Internals
{
	/// <summary>
	/// Defines the minimal contract for a circuit breaker used within FusionCache.
	/// </summary>
	internal interface IFusionCacheCircuitBreaker
	{
		/// <summary>
		/// Gets the current state of the circuit breaker.
		/// </summary>
		CircuitBreakerState State { get; }

		/// <summary>
		/// Attempt to execute an operation subject to the breaker.
		/// Returns <see langword="false"/> if the breaker is open and no call should be attempted.
		/// </summary>
		/// <param name="isStateChanged">Will be set to true if the call caused the breaker state to change.</param>
		/// <returns><see langword="true"/> if the operation is permitted to execute.</returns>
		bool TryExecute(out bool isStateChanged);

		/// <summary>
		/// Record a successful call against the breaker, potentially closing a half–open breaker.
		/// </summary>
		void RecordSuccess(out bool isStateChanged);

		/// <summary>
		/// Record a failed call against the breaker, potentially opening it.
		/// </summary>
		void RecordFailure(out bool isStateChanged);

		/// <summary>
		/// Close the breaker immediately, resetting failure counts and state.
		/// </summary>
		void Close(out bool isStateChanged);

		/// <summary>
		/// Gets the current failure count maintained by the breaker.
		/// For a simple breaker this is the count of consecutive failures; for an advanced breaker, failures in the current window.
		/// </summary>
		int CurrentFailureCount { get; }
	}
}
