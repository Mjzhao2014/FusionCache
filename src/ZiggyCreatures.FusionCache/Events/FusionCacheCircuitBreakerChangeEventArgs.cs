using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion.Events;

/// <summary>
/// The specific <see cref="EventArgs"/> object for events related to opening/closing of a circuit breaker.
/// </summary>
public class FusionCacheCircuitBreakerChangeEventArgs : EventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="FusionCacheCircuitBreakerChangeEventArgs"/> class.
	/// </summary>
	/// <param name="operationId">The operation id for the current operation.</param>
	/// <param name="key">The cache key involved in the circuit breaker state change.</param>
	/// <param name="state">The current state of the circuit breaker.</param>
	public FusionCacheCircuitBreakerChangeEventArgs(string? operationId, string? key, CircuitBreakerState state)
	{
		OperationId = operationId;
		Key = key;
		State = state;
	}

	/// <summary>
	/// The operation id for the current operation.
	/// </summary>
	public string? OperationId { get; }

	/// <summary>
	/// The cache key involved in the circuit breaker state change.
	/// </summary>
	public string? Key { get; }

	/// <summary>
	/// A flag that indicates if the circuit breaker has been opened or closed.
	/// </summary>
	[Obsolete("Use State property instead to get the exact circuit breaker state")]
	public bool IsClosed => State == CircuitBreakerState.Closed;

	/// <summary>
	/// A flag that indicates if the circuit breaker is active (closed or half-open).
	/// </summary>
	public bool IsActive => State != CircuitBreakerState.Open;

	/// <summary>
	/// The current state of the circuit breaker.
	/// </summary>
	public CircuitBreakerState State { get; }
}
