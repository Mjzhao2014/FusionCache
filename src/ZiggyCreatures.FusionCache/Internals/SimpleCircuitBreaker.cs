namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// A simple, reusable circuit-breaker.
/// </summary>
internal sealed class SimpleCircuitBreaker
	: IFusionCacheCircuitBreaker
{
	private readonly int _failuresAllowedBeforeBreaking;
	private readonly TimeSpan _breakDuration;
	private readonly TimeSpan _jitterMaxDuration;
	// count of consecutive failures while closed
	private int _failureCount;
	// when in Open state, the absolute ticks the circuit will remain open until transitioning to half-open
	private long _openUntilTicks;
	// an indicator of whether we have consumed the single allowed call in half-open state
	private int _halfOpenTestInProgress;
	// state of the circuit breaker (0=Closed,1=Open,2=HalfOpen)
	private int _state;

	/// <summary>
	/// Creates a new <see cref="SimpleCircuitBreaker"/> instance that will open after a certain number of consecutive failures.
	/// </summary>
	/// <param name="failuresAllowedBeforeBreaking">How many consecutive failures are allowed before the circuit will open.</param>
	/// <param name="breakDuration">The amount of time the circuit will remain open before transitioning to half-open.</param>
	/// <param name="jitterMaxDuration">An optional maximum duration to add as jitter to the break duration.</param>
	public SimpleCircuitBreaker(int failuresAllowedBeforeBreaking, TimeSpan breakDuration, TimeSpan jitterMaxDuration = default)
	{
		_failuresAllowedBeforeBreaking = failuresAllowedBeforeBreaking < 1 ? 1 : failuresAllowedBeforeBreaking;
		_breakDuration = breakDuration;
		_jitterMaxDuration = jitterMaxDuration;
		_state = (int)CircuitBreakerState.Closed;
		_failureCount = 0;
		_openUntilTicks = DateTimeOffset.MinValue.Ticks;
	}

	/// <inheritdoc />
	public CircuitBreakerState State => (CircuitBreakerState)Volatile.Read(ref _state);

	/// <inheritdoc />
	public int CurrentFailureCount => Volatile.Read(ref _failureCount);

	/// <inheritdoc />
	public bool TryExecute(out bool isStateChanged)
	{
		isStateChanged = false;
		// no circuit breaker configured
		if (_breakDuration.Ticks == 0)
		{
			return true;
		}
		while (true)
		{
			var state = (CircuitBreakerState)Volatile.Read(ref _state);
			switch (state)
			{
				case CircuitBreakerState.Closed:
					return true;
				case CircuitBreakerState.Open:
					// check if open period has elapsed
					if (DateTimeOffset.UtcNow.Ticks >= Volatile.Read(ref _openUntilTicks))
					{
						// transition to half-open if still open
						if (Interlocked.CompareExchange(ref _state, (int)CircuitBreakerState.HalfOpen, (int)CircuitBreakerState.Open) == (int)CircuitBreakerState.Open)
						{
							// reset half-open gating
							Interlocked.Exchange(ref _halfOpenTestInProgress, 0);
							isStateChanged = true;
						}
						// loop will handle half-open gating
						continue;
					}
					return false;
				case CircuitBreakerState.HalfOpen:
					// allow exactly one call through
					if (Interlocked.CompareExchange(ref _halfOpenTestInProgress, 1, 0) == 0)
					{
						return true;
					}
					return false;
				default:
					return true;
			}
		}
	}

	private TimeSpan GetBreakDurationWithJitter()
	{
		var duration = _breakDuration;
		if (_jitterMaxDuration > TimeSpan.Zero)
		{
			// guard against overflow
			if (duration > (TimeSpan.MaxValue - _jitterMaxDuration))
				return TimeSpan.MaxValue;
			duration += TimeSpan.FromMilliseconds(ConcurrentRandom.NextDouble() * _jitterMaxDuration.TotalMilliseconds);
		}
		return duration;
	}

	private void OpenCircuit(out bool isStateChanged)
	{
		var breakDuration = GetBreakDurationWithJitter();
		Interlocked.Exchange(ref _openUntilTicks, DateTimeOffset.UtcNow.Add(breakDuration).Ticks);
		var oldState = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Open);
		isStateChanged = oldState != (int)CircuitBreakerState.Open;
	}

	/// <inheritdoc />
	public void RecordFailure(out bool isStateChanged)
	{
		isStateChanged = false;
		var state = (CircuitBreakerState)Volatile.Read(ref _state);
		if (state == CircuitBreakerState.Closed)
		{
			var newCount = Interlocked.Increment(ref _failureCount);
			if (newCount >= _failuresAllowedBeforeBreaking)
			{
				OpenCircuit(out isStateChanged);
			}
		}
		else if (state == CircuitBreakerState.HalfOpen)
		{
			// a failure during half-open will trip to open
			OpenCircuit(out isStateChanged);
		}
	}

	/// <inheritdoc />
	public void RecordSuccess(out bool isStateChanged)
	{
		isStateChanged = false;
		var state = (CircuitBreakerState)Volatile.Read(ref _state);
		if (state == CircuitBreakerState.Closed)
		{
			// reset consecutive failure count
			Interlocked.Exchange(ref _failureCount, 0);
		}
		else if (state == CircuitBreakerState.HalfOpen)
		{
			// success in half-open closes the circuit
			Interlocked.Exchange(ref _failureCount, 0);
			var oldState = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
			isStateChanged = oldState != (int)CircuitBreakerState.Closed;
		}
	}

	/// <inheritdoc />
	public void Close(out bool isStateChanged)
	{
		Interlocked.Exchange(ref _failureCount, 0);
		var oldState = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
		isStateChanged = oldState != (int)CircuitBreakerState.Closed;
	}

	/// <summary>
	/// For backward compatibility, expose the configured break duration.
	/// </summary>
	public TimeSpan BreakDuration => _breakDuration;
}
