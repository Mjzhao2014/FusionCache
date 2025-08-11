namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// A simple, reusable circuit-breaker that uses a consecutive failure count policy.
/// </summary>
internal sealed class SimpleCircuitBreaker : IFusionCacheCircuitBreaker
{
	private readonly int _failuresAllowedBeforeBreaking;
	private readonly TimeSpan _breakDuration;
	private readonly TimeSpan _jitterMaxDuration;
	private int _failureCount;
	private long _blockedUntilTicks;
	private int _state;
	private int _halfOpenAllowed;

	/// <summary>
	/// Creates a new <see cref="SimpleCircuitBreaker"/> instance.
	/// </summary>
	/// <param name="failuresAllowedBeforeBreaking">How many consecutive failures are allowed before opening the circuit.</param>
	/// <param name="breakDuration">The amount of time the circuit will remain open when opened.</param>
	/// <param name="jitterMaxDuration">Optional max jitter duration to add to the break duration.</param>
	public SimpleCircuitBreaker(int failuresAllowedBeforeBreaking, TimeSpan breakDuration, TimeSpan jitterMaxDuration = default)
	{
		_failuresAllowedBeforeBreaking = failuresAllowedBeforeBreaking <= 0 ? 1 : failuresAllowedBeforeBreaking;
		_breakDuration = breakDuration;
		_jitterMaxDuration = jitterMaxDuration;
		_state = (int)CircuitBreakerState.Closed;
		_failureCount = 0;
		_blockedUntilTicks = DateTimeOffset.MinValue.Ticks;
		_halfOpenAllowed = 0;
	}

	private TimeSpan GetBreakDurationWithJitter()
	{
		if (_jitterMaxDuration <= TimeSpan.Zero)
			return _breakDuration;
		return _breakDuration + TimeSpan.FromMilliseconds(ConcurrentRandom.NextDouble() * _jitterMaxDuration.TotalMilliseconds);
	}

	private void OpenCircuit()
	{
		var dur = GetBreakDurationWithJitter();
		Interlocked.Exchange(ref _blockedUntilTicks, DateTimeOffset.UtcNow.Add(dur).Ticks);
		Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Open);
		Interlocked.Exchange(ref _halfOpenAllowed, 0);
		// reset failure count so we don't misinterpret failures during open window
		Interlocked.Exchange(ref _failureCount, 0);
	}

	public CircuitBreakerState State => (CircuitBreakerState)Volatile.Read(ref _state);

	public bool TryExecute(out bool isStateChanged)
	{
		isStateChanged = false;
		// if we never break
		if (_breakDuration.Ticks <= 0)
			return true;

		var curState = (CircuitBreakerState)Volatile.Read(ref _state);
		if (curState == CircuitBreakerState.Closed)
		{
			return true;
		}
		if (curState == CircuitBreakerState.Open)
		{
			long blockedUntil = Volatile.Read(ref _blockedUntilTicks);
			if (DateTimeOffset.UtcNow.Ticks < blockedUntil)
			{
				return false;
			}
			// try transition to half-open if time elapsed
			if (Interlocked.CompareExchange(ref _state, (int)CircuitBreakerState.HalfOpen, (int)CircuitBreakerState.Open) == (int)CircuitBreakerState.Open)
			{
				isStateChanged = true;
				Interlocked.Exchange(ref _halfOpenAllowed, 1);
			}
			curState = (CircuitBreakerState)Volatile.Read(ref _state);
		}
		if (curState == CircuitBreakerState.HalfOpen)
		{
			// allow exactly one call through
			if (Interlocked.CompareExchange(ref _halfOpenAllowed, 0, 1) == 1)
			{
				return true;
			}
			return false;
		}
		return false;
	}

	public void RecordSuccess(out bool isStateChanged)
	{
		isStateChanged = false;
		var curState = (CircuitBreakerState)Volatile.Read(ref _state);
		if (curState == CircuitBreakerState.Closed)
		{
			// reset failure count on any success
			Interlocked.Exchange(ref _failureCount, 0);
			return;
		}
		if (curState == CircuitBreakerState.HalfOpen)
		{
			// on a successful test call, close the circuit
			Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
			Interlocked.Exchange(ref _failureCount, 0);
			isStateChanged = true;
			return;
		}
	}

	public void RecordFailure(out bool isStateChanged)
	{
		isStateChanged = false;
		var curState = (CircuitBreakerState)Volatile.Read(ref _state);
		if (curState == CircuitBreakerState.Closed)
		{
			var newCount = Interlocked.Increment(ref _failureCount);
			if (newCount >= _failuresAllowedBeforeBreaking)
			{
				OpenCircuit();
				isStateChanged = true;
			}
			return;
		}
		if (curState == CircuitBreakerState.HalfOpen)
		{
			// failed test call: re-open
			OpenCircuit();
			isStateChanged = true;
			return;
		}
	}

	public void Close(out bool isStateChanged)
	{
		var prev = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
		isStateChanged = prev != (int)CircuitBreakerState.Closed;
		Interlocked.Exchange(ref _failureCount, 0);
		Interlocked.Exchange(ref _blockedUntilTicks, DateTimeOffset.MinValue.Ticks);
		Interlocked.Exchange(ref _halfOpenAllowed, 0);
	}

	public int CurrentFailureCount => Volatile.Read(ref _failureCount);
}
