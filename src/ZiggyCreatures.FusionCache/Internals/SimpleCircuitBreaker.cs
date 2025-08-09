namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// A simple, reusable circuit-breaker that trips after a configurable number of consecutive failures and supports a half-open state.
/// </summary>
internal sealed class SimpleCircuitBreaker : IFusionCacheCircuitBreaker
{
	private readonly int _failuresAllowedBeforeBreaking;
	private readonly TimeSpan _breakDuration;
	private readonly TimeSpan _jitterMaxDuration;
	private int _failureCount;
	private int _state;
	private long _blockedUntilTicks;
	private int _halfOpenActive;

	/// <summary>
	/// Creates a new <see cref="SimpleCircuitBreaker"/> instance.
	/// </summary>
	/// <param name="failuresAllowedBeforeBreaking">Number of consecutive failures that will trip the circuit.</param>
	/// <param name="breakDuration">How long the circuit should stay open when tripped.</param>
	/// <param name="jitterMaxDuration">Optional jitter to add to the break duration when opening.</param>
	public SimpleCircuitBreaker(int failuresAllowedBeforeBreaking, TimeSpan breakDuration, TimeSpan jitterMaxDuration = default)
	{
		if (failuresAllowedBeforeBreaking <= 0)
			failuresAllowedBeforeBreaking = 1;
		_failuresAllowedBeforeBreaking = failuresAllowedBeforeBreaking;
		_breakDuration = breakDuration;
		_jitterMaxDuration = jitterMaxDuration;
		_state = (int)CircuitBreakerState.Closed;
		_blockedUntilTicks = DateTimeOffset.MinValue.Ticks;
		_halfOpenActive = 0;
		_failureCount = 0;
	}

	/// <inheritdoc/>
	public CircuitBreakerState State => (CircuitBreakerState)Volatile.Read(ref _state);

	/// <inheritdoc/>
	public int CurrentFailureCount => Volatile.Read(ref _failureCount);

	/// <inheritdoc/>
	public bool TryExecute(out bool isStateChanged)
	{
		isStateChanged = false;
		if (_breakDuration <= TimeSpan.Zero)
		{
			return true;
		}
		while (true)
		{
			var currentState = (CircuitBreakerState)Volatile.Read(ref _state);
			if (currentState == CircuitBreakerState.Closed)
			{
				return true;
			}
			else if (currentState == CircuitBreakerState.Open)
			{
				var untilTicks = Interlocked.Read(ref _blockedUntilTicks);
				if (DateTimeOffset.UtcNow.Ticks >= untilTicks)
				{
					// transition to half-open
					if (Interlocked.CompareExchange(ref _state, (int)CircuitBreakerState.HalfOpen, (int)CircuitBreakerState.Open) == (int)CircuitBreakerState.Open)
					{
						Interlocked.Exchange(ref _halfOpenActive, 0);
						isStateChanged = true;
					}
					else
					{
						continue;
					}
				}
				else
				{
					return false;
				}
				currentState = (CircuitBreakerState)Volatile.Read(ref _state);
			}
			if (currentState == CircuitBreakerState.HalfOpen)
			{
				// allow exactly one thread through
				if (Interlocked.CompareExchange(ref _halfOpenActive, 1, 0) == 0)
				{
					return true;
				}
				else
				{
					return false;
				}
			}
		}
	}

	/// <inheritdoc/>
	public void RecordSuccess(out bool isStateChanged)
	{
		isStateChanged = false;
		var currentState = (CircuitBreakerState)Volatile.Read(ref _state);
		if (currentState == CircuitBreakerState.HalfOpen)
		{
			// half-open trial succeeded -> close
			Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
			Interlocked.Exchange(ref _failureCount, 0);
			Interlocked.Exchange(ref _halfOpenActive, 0);
			isStateChanged = true;
		}
		else if (currentState == CircuitBreakerState.Closed)
		{
			// reset consecutive failure count on any success
			Interlocked.Exchange(ref _failureCount, 0);
		}
	}

	/// <inheritdoc/>
	public void RecordFailure(out bool isStateChanged)
	{
		isStateChanged = false;
		var currentState = (CircuitBreakerState)Volatile.Read(ref _state);
		if (currentState == CircuitBreakerState.HalfOpen)
		{
			OpenInternal();
			Interlocked.Exchange(ref _failureCount, 0);
			Interlocked.Exchange(ref _halfOpenActive, 0);
			isStateChanged = true;
		}
		else if (currentState == CircuitBreakerState.Closed)
		{
			var failureCount = Interlocked.Increment(ref _failureCount);
			if (failureCount >= _failuresAllowedBeforeBreaking)
			{
				OpenInternal();
				Interlocked.Exchange(ref _failureCount, 0);
				isStateChanged = true;
			}
		}
	}

	private void OpenInternal()
	{
		var duration = _breakDuration + GetJitter();
		Interlocked.Exchange(ref _blockedUntilTicks, DateTimeOffset.UtcNow.Add(duration).Ticks);
		Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Open);
	}

	/// <inheritdoc/>
	public void Close(out bool isStateChanged)
	{
		var prev = (CircuitBreakerState)Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
		isStateChanged = prev != CircuitBreakerState.Closed;
		Interlocked.Exchange(ref _blockedUntilTicks, DateTimeOffset.MinValue.Ticks);
		Interlocked.Exchange(ref _failureCount, 0);
		Interlocked.Exchange(ref _halfOpenActive, 0);
	}

	private TimeSpan GetJitter()
	{
		if (_jitterMaxDuration <= TimeSpan.Zero)
			return TimeSpan.Zero;
		return TimeSpan.FromMilliseconds(ConcurrentRandom.NextDouble() * _jitterMaxDuration.TotalMilliseconds);
	}
}
