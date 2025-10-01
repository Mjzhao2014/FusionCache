using ZiggyCreatures.Caching.Fusion;
namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// A simple, reusable circuit-breaker that tracks consecutive failures and opens when the allowed number of failures is exceeded.
/// </summary>

internal sealed class SimpleCircuitBreaker
	: IFusionCacheCircuitBreaker
{
	private readonly int _failuresAllowedBeforeBreaking;
	private readonly TimeSpan _breakDuration;
	private readonly TimeSpan _jitterMaxDuration;

	private int _state;
	private int _currentFailureCount;
	private long _openEndTicks;
	private int _halfOpenCalled;

	/// <summary>
	/// Creates a new <see cref="SimpleCircuitBreaker"/> instance.
	/// </summary>
	/// <param name="failuresAllowedBeforeBreaking">The number of consecutive failures that will trip the circuit to open.</param>
	/// <param name="breakDuration">The base duration the circuit will remain open once tripped.</param>
	/// <param name="jitterMaxDuration">An optional maximum duration to add jitter to the break duration to avoid thundering herds.</param>
	public SimpleCircuitBreaker(int failuresAllowedBeforeBreaking, TimeSpan breakDuration, TimeSpan jitterMaxDuration = default)
	{
		if (failuresAllowedBeforeBreaking <= 0)
			throw new ArgumentOutOfRangeException(nameof(failuresAllowedBeforeBreaking), failuresAllowedBeforeBreaking, "The number of allowed failures must be greater than zero.");

		if (jitterMaxDuration < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(jitterMaxDuration), jitterMaxDuration, "The jitter duration cannot be negative.");

		_failuresAllowedBeforeBreaking = failuresAllowedBeforeBreaking;
		_breakDuration = breakDuration;
		_jitterMaxDuration = jitterMaxDuration;
		_state = (int)CircuitBreakerState.Closed;
		_currentFailureCount = 0;
		_openEndTicks = DateTimeOffset.MinValue.Ticks;
		_halfOpenCalled = 0;
	}

	public CircuitBreakerState State => (CircuitBreakerState)Volatile.Read(ref _state);

	public int CurrentFailureCount => Volatile.Read(ref _currentFailureCount);

	public bool TryExecute(out bool isStateChanged)
	{
		isStateChanged = false;
		// no break duration means breaker disabled
		if (_breakDuration <= TimeSpan.Zero)
		{
			return true;
		}
		var currState = (CircuitBreakerState)Volatile.Read(ref _state);
		if (currState == CircuitBreakerState.Open)
		{
			var now = DateTimeOffset.UtcNow;
			// if break duration has elapsed, transition to half-open
			if (now.Ticks >= Volatile.Read(ref _openEndTicks))
			{
				var prev = Interlocked.CompareExchange(ref _state, (int)CircuitBreakerState.HalfOpen, (int)CircuitBreakerState.Open);
				if (prev == (int)CircuitBreakerState.Open)
				{
					isStateChanged = true;
					// reset half-open call flag
					Volatile.Write(ref _halfOpenCalled, 0);
				}
			}
			else
			{
				return false;
			}
			currState = (CircuitBreakerState)Volatile.Read(ref _state);
		}
		if (currState == CircuitBreakerState.HalfOpen)
		{
			// allow exactly one test call
			if (Interlocked.CompareExchange(ref _halfOpenCalled, 1, 0) != 0)
			{
				return false;
			}
		}
		return true;
	}

	public void RecordSuccess(out bool isStateChanged)
	{
		isStateChanged = false;
		var currState = (CircuitBreakerState)Volatile.Read(ref _state);
		if (currState == CircuitBreakerState.HalfOpen)
		{
			Close(out isStateChanged);
		}
		else if (currState == CircuitBreakerState.Closed)
		{
			// reset consecutive failure count on success
			Volatile.Write(ref _currentFailureCount, 0);
		}
	}

	public void RecordFailure(out bool isStateChanged)
	{
		isStateChanged = false;
		if (_breakDuration <= TimeSpan.Zero)
		{
			Interlocked.Increment(ref _currentFailureCount);
			return;
		}
		var currState = (CircuitBreakerState)Volatile.Read(ref _state);
		if (currState == CircuitBreakerState.HalfOpen)
		{
			Open(out isStateChanged);
		}
		else if (currState == CircuitBreakerState.Closed)
		{
			var newCount = Interlocked.Increment(ref _currentFailureCount);
			if (newCount >= _failuresAllowedBeforeBreaking)
			{
				Open(out isStateChanged);
			}
		}
	}

	public void Close(out bool isStateChanged)
	{
		var old = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
		// reset counts
		Volatile.Write(ref _currentFailureCount, 0);
		Volatile.Write(ref _openEndTicks, DateTimeOffset.MinValue.Ticks);
		Volatile.Write(ref _halfOpenCalled, 0);
		isStateChanged = old != (int)CircuitBreakerState.Closed;
	}

	private void Open(out bool isStateChanged)
	{
		if (_breakDuration <= TimeSpan.Zero)
		{
			isStateChanged = false;
			return;
		}
		// compose break duration with jitter
		var dur = _breakDuration;
		if (_jitterMaxDuration > TimeSpan.Zero)
		{
			var jitterMs = ConcurrentRandom.NextDouble() * _jitterMaxDuration.TotalMilliseconds;
			dur = dur.Add(TimeSpan.FromMilliseconds(jitterMs));
		}
		Volatile.Write(ref _openEndTicks, DateTimeOffset.UtcNow.Add(dur).Ticks);
		var prev = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Open);
		// reset half-open flag so next half-open will allow one call
		Volatile.Write(ref _halfOpenCalled, 0);
		isStateChanged = prev != (int)CircuitBreakerState.Open;
	}

	/// <summary>
	/// The amount of time the circuit will remain open, when told to.
	/// </summary>
	public TimeSpan BreakDuration => _breakDuration;
}
