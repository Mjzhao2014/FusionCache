namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Defines a circuit breaker abstraction to guard calls against repeated failures.
/// </summary>
internal interface IFusionCacheCircuitBreaker
{
   /// <summary>
   /// Gets the current state of the circuit breaker.
   /// </summary>
   CircuitBreakerState State { get; }

   /// <summary>
   /// Attempts to execute an operation protected by the circuit breaker.
   /// </summary>
   /// <param name="isStateChanged">Indicates if the circuit breaker state changed during this attempt.</param>
   /// <returns><see langword="true"/> if the operation should proceed; <see langword="false"/> if the circuit is open and the operation should be blocked.</returns>
   bool TryExecute(out bool isStateChanged);

   /// <summary>
   /// Records a successful operation execution.
   /// </summary>
   /// <param name="isStateChanged">Indicates if the circuit breaker state changed due to the success.</param>
   void RecordSuccess(out bool isStateChanged);

   /// <summary>
   /// Records a failed operation execution.
   /// </summary>
   /// <param name="isStateChanged">Indicates if the circuit breaker state changed due to the failure.</param>
   void RecordFailure(out bool isStateChanged);

   /// <summary>
   /// Manually closes the circuit breaker, resetting any failure counts and state.
   /// </summary>
   /// <param name="isStateChanged">Indicates if the circuit breaker state changed.</param>
   void Close(out bool isStateChanged);

   /// <summary>
   /// Gets the current failure count within the sampling window.
   /// For simple circuit breaker this is the consecutive failure count; for the advanced
   /// circuit breaker this is the number of failures in the current sampling window.
   /// </summary>
   int CurrentFailureCount { get; }
}
