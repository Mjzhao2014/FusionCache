namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
	/// A simple, reusable circuit-breaker that opens after a configured number of consecutive failures.
	/// It supports half-open behavior to probe for recovery after the break duration has elapsed.
	/// </summary>
internal sealed class SimpleCircuitBreaker
	: IFusionCacheCircuitBreaker
{
	private int _circuitState;
	private long _openUntilTicks;
	private readonly long _breakDurationTicks;
	private readonly int _failuresAllowedBeforeBreaking;
	private int _failureCount;
	private int _halfOpenExecutionAllowed;

	/// <summary>
	/// Creates a new <see cref="SimpleCircuitBreaker"/> instance.
	/// </summary>
	/// <param name="breakDuration">The amount of time the circuit will remain open once opened.</param>
	/// <param name="failuresAllowedBeforeBreaking">How many consecutive failures are allowed before opening.</param>
	public SimpleCircuitBreaker(TimeSpan breakDuration, int failuresAllowedBeforeBreaking)
	{
		BreakDuration = breakDuration;
		_breakDurationTicks = BreakDuration.Ticks;
		_openUntilTicks = DateTimeOffset.MinValue.Ticks;
		_circuitState = (int)CircuitBreakerState.Closed;
		_failuresAllowedBeforeBreaking = failuresAllowedBeforeBreaking < 1 ? 1 : failuresAllowedBeforeBreaking;
		_failureCount = 0;
		_halfOpenExecutionAllowed = 1;
	}

	/// <summary>
	/// The amount of time the circuit will remain open, when told to.
	/// </summary>
	public TimeSpan BreakDuration { get; }

	public CircuitBreakerState State => (CircuitBreakerState)Volatile.Read(ref _circuitState);

	public int CurrentFailureCount => Volatile.Read(ref _failureCount);

	public bool TryExecute(out bool isStateChanged)
	{
		isStateChanged = false;
		// no break duration means circuit disabled
		if (_breakDurationTicks == 0)
			return true;
		var nowTicks = DateTimeOffset.UtcNow.Ticks;
		var state = (CircuitBreakerState)Volatile.Read(ref _circuitState);
		if (state == CircuitBreakerState.Closed)
		{
			return true;
		}
		if (state == CircuitBreakerState.Open)
		{
			// if still within break window, deny
			if (nowTicks < Volatile.Read(ref _openUntilTicks))
				return false;
			// break has expired: transition to half-open
			var old = Interlocked.CompareExchange(ref _circuitState, (int)CircuitBreakerState.HalfOpen, (int)CircuitBreakerState.Open);
			if (old == (int)CircuitBreakerState.Open)
			{
				isStateChanged = true;
				// reset gating for single half-open execution
				Interlocked.Exchange(ref _halfOpenExecutionAllowed, 1);
			}
			// allow one test execution if not already taken
			return Interlocked.Exchange(ref _halfOpenExecutionAllowed, 0) == 1;
		}
		// half-open state: allow at most one execution concurrently
		return Interlocked.Exchange(ref _halfOpenExecutionAllowed, 0) == 1;
	}

	public void RecordSuccess(out bool isStateChanged)
	{
		isStateChanged = false;
		var state = (CircuitBreakerState)Volatile.Read(ref _circuitState);
		if (state == CircuitBreakerState.Closed)
		{
			// reset consecutive failure count on success
			Interlocked.Exchange(ref _failureCount, 0);
		}
		else if (state == CircuitBreakerState.HalfOpen)
		{
			// the test call succeeded: close circuit
			Interlocked.Exchange(ref _circuitState, (int)CircuitBreakerState.Closed);
			Interlocked.Exchange(ref _failureCount, 0);
			Interlocked.Exchange(ref _halfOpenExecutionAllowed, 1);
			isStateChanged = true;
		}
	}

	public void RecordFailure(out bool isStateChanged)
	{
		isStateChanged = false;
		var nowTicks = DateTimeOffset.UtcNow.Ticks;
		var state = (CircuitBreakerState)Volatile.Read(ref _circuitState);
		if (state == CircuitBreakerState.Closed)
		{
			var newCount = Interlocked.Increment(ref _failureCount);
			if (newCount >= _failuresAllowedBeforeBreaking)
			{
				if (_breakDurationTicks == 0)
					return;
				Interlocked.Exchange(ref _openUntilTicks, nowTicks + _breakDurationTicks);
				var old = Interlocked.Exchange(ref _circuitState, (int)CircuitBreakerState.Open);
				isStateChanged = old != (int)CircuitBreakerState.Open;
				// reset count when open
				Interlocked.Exchange(ref _failureCount, 0);
			}
		}
		else if (state == CircuitBreakerState.HalfOpen)
		{
			// test call failed: reopen
			if (_breakDurationTicks == 0)
				return;
			Interlocked.Exchange(ref _openUntilTicks, nowTicks + _breakDurationTicks);
			var old = Interlocked.Exchange(ref _circuitState, (int)CircuitBreakerState.Open);
			isStateChanged = old != (int)CircuitBreakerState.Open;
		}
	}

	public void Close(out bool isStateChanged)
	{
		Interlocked.Exchange(ref _openUntilTicks, DateTimeOffset.MinValue.Ticks);
		var oldState = Interlocked.Exchange(ref _circuitState, (int)CircuitBreakerState.Closed);
		Interlocked.Exchange(ref _failureCount, 0);
		Interlocked.Exchange(ref _halfOpenExecutionAllowed, 1);
		isStateChanged = oldState != (int)CircuitBreakerState.Closed;
	}
}
