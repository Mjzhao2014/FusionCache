using System;

namespace ZiggyCreatures.Caching.Fusion.Internals
{
	/// <summary>
	/// Unified interface implemented by circuit breaker implementations used in FusionCache.
	/// </summary>
	internal interface IFusionCacheCircuitBreaker
	{
		/// <summary>
		/// Gets the current circuit breaker state.
		/// </summary>
		CircuitBreakerState State { get; }
		/// <summary>
		/// Attempts to start an operation under this circuit breaker.
		/// This checks the circuit breaker state and will transition from open to half-open when the break duration expires.
		/// If the returned value is <c>true</c> the caller is allowed to proceed. If <c>false</c>, the caller must not proceed.
		/// </summary>
		/// <param name="isStateChanged">Outputs <c>true</c> if the circuit changed state as a result of this call.</param>
		/// <returns><c>true</c> if the circuit allows the call to proceed; <c>false</c> if the circuit is open or half-open but an allowed call is already in progress.</returns>
		bool TryExecute(out bool isStateChanged);
		/// <summary>
		/// Records a successful completion of an operation protected by this circuit breaker.
		/// This is used to reset failure counts and transition from half-open back to closed.
		/// </summary>
		/// <param name="isStateChanged">Outputs <c>true</c> if the circuit changed state as a result of this call (e.g. half-open to closed).</param>
		void RecordSuccess(out bool isStateChanged);
		/// <summary>
		/// Records a failed completion of an operation protected by this circuit breaker.
		/// This will increment failure counts and may cause the circuit to open.
		/// </summary>
		/// <param name="isStateChanged">Outputs <c>true</c> if the circuit changed state as a result of this call.</param>
		void RecordFailure(out bool isStateChanged);
		/// <summary>
		/// Forcibly closes the circuit, resetting failure counts and state.
		/// </summary>
		/// <param name="isStateChanged">Outputs <c>true</c> if the circuit changed state (e.g. was previously open or half-open).</param>
		void Close(out bool isStateChanged);
		/// <summary>
		/// Gets the current failure count as maintained by the circuit breaker.
		/// For a simple circuit breaker this will be consecutive failures; for an advanced circuit breaker this will be the failures counted within the current sampling window.
		/// </summary>
		int CurrentFailureCount { get; }
	}
}
