namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Represents the current state of an <see cref="IFusionCacheCircuitBreaker"/>.
/// </summary>
internal enum CircuitBreakerState
{
    /// <summary>
    /// The breaker is closed and all calls are allowed.
    /// </summary>
    Closed = 0,
    /// <summary>
    /// The breaker is open and calls are blocked until the configured break duration expires.
    /// </summary>
    Open = 1,
    /// <summary>
    /// The breaker is half-open and exactly one test call is allowed to determine whether to close or re-open.
    /// </summary>
    HalfOpen = 2
}
