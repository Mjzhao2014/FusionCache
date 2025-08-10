using System;

namespace ZiggyCreatures.Caching.Fusion.Internals
{
	/// <summary>
	/// Defines a common interface for circuit breaker implementations.
	/// </summary>
	internal interface IFusionCacheCircuitBreaker
	{
		/// <summary>
		/// The current state of the circuit breaker.
		/// </summary>
		CircuitBreakerState State { get; }

		/// <summary>
		/// Tries to execute an operation through the circuit breaker.
		/// </summary>
		/// <param name="isStateChanged">Indicates if the circuit breaker state changed during this operation.</param>
		/// <returns><see langword="true"/> if the operation can proceed; <see langword="false"/> if the circuit is open and the operation should be skipped.</returns>
		bool TryExecute(out bool isStateChanged);

		/// <summary>
		/// Records a successful operation execution.
		/// </summary>
		/// <param name="isStateChanged">Indicates if the circuit breaker state changed due to this success.</param>
		void RecordSuccess(out bool isStateChanged);

		/// <summary>
		/// Records a failed operation execution.
		/// </summary>
		/// <param name="isStateChanged">Indicates if the circuit breaker state changed due to this failure.</param>
		void RecordFailure(out bool isStateChanged);

		/// <summary>
		/// Manually closes the circuit breaker.
		/// </summary>
		/// <param name="isStateChanged">Indicates if the circuit breaker state changed during this operation.</param>
		void Close(out bool isStateChanged);

		/// <summary>
		/// Gets the current failure count within the sampling window.
		/// </summary>
		int CurrentFailureCount { get; }
	}

	/// <summary>
	/// Represents the basic state of a circuit breaker.
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
