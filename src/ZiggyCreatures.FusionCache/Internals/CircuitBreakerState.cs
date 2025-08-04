namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// The current state of a circuit breaker.
/// </summary>
internal enum CircuitBreakerState
{
   /// <summary>
   /// The circuit is closed – all calls are allowed.
   /// </summary>
   Closed = 0,

   /// <summary>
   /// The circuit is open – calls are blocked.
   /// </summary>
   Open = 1,

   /// <summary>
   /// The circuit is half-open – one test call is permitted to determine if the circuit can close again.
   /// </summary>
   HalfOpen = 2
}
