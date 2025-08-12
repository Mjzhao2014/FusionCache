using System.Threading;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// An advanced circuit breaker that trips when a failure rate exceeds a threshold within a sampling window.
/// It will remain open for a configured break duration (plus optional jitter) before transitioning to a half–open state
/// to test recovery.
/// </summary>
internal sealed class AdvancedCircuitBreaker
	: IFusionCacheCircuitBreaker
{
	private readonly double _failureThreshold;
	private readonly long _samplingDurationTicks;
	private readonly int _minimumThroughput;
	private readonly long _breakDurationTicks;
	private readonly long _jitterMaxDurationTicks;
	private int _circuitState;
	private long _openUntilTicks;
	private long _windowStartTicks;
	private int _failureCount;
	private int _throughputCount;
	private int _halfOpenAttempt;

	public AdvancedCircuitBreaker(double failureThreshold, TimeSpan samplingDuration, int minimumThroughput, TimeSpan breakDuration, TimeSpan jitterMaxDuration = default)
	{
		_failureThreshold = failureThreshold;
		_samplingDurationTicks = samplingDuration.Ticks;
		_minimumThroughput = minimumThroughput;
		_breakDurationTicks = breakDuration.Ticks;
		_jitterMaxDurationTicks = jitterMaxDuration.Ticks;
		BreakDuration = breakDuration;
		_circuitState = (int)CircuitBreakerState.Closed;
		_openUntilTicks = DateTimeOffset.MinValue.Ticks;
		_windowStartTicks = DateTimeOffset.UtcNow.Ticks;
	}

	/// <summary>
	/// The base break duration used when opening the circuit.
	/// </summary>
	public TimeSpan BreakDuration { get; }

	public CircuitBreakerState State => (CircuitBreakerState)Volatile.Read(ref _circuitState);

	public int CurrentFailureCount => Volatile.Read(ref _failureCount);

	private void EnsureWindow()
	{
		var nowTicks = DateTimeOffset.UtcNow.Ticks;
		var windowStart = Volatile.Read(ref _windowStartTicks);
		if (nowTicks - windowStart >= _samplingDurationTicks)
		{
			// reset counts
			Interlocked.Exchange(ref _windowStartTicks, nowTicks);
			Interlocked.Exchange(ref _failureCount, 0);
			Interlocked.Exchange(ref _throughputCount, 0);
		}
	}

	public bool TryExecute(out bool isStateChanged)
	{
		isStateChanged = false;
		if (_breakDurationTicks == 0)
		{
			return true;
		}
		var state = State;
		if (state == CircuitBreakerState.Open)
		{
			if (DateTimeOffset.UtcNow.Ticks >= Volatile.Read(ref _openUntilTicks))
			{
				// transition to half–open
				var old = Interlocked.Exchange(ref _circuitState, (int)CircuitBreakerState.HalfOpen);
				isStateChanged = old != (int)CircuitBreakerState.HalfOpen;
				Interlocked.Exchange(ref _halfOpenAttempt, 0);
				return Interlocked.CompareExchange(ref _halfOpenAttempt, 1, 0) == 0;
			}
			return false;
		}
		if (state == CircuitBreakerState.HalfOpen)
		{
			return Interlocked.CompareExchange(ref _halfOpenAttempt, 1, 0) == 0;
		}
		// closed
		return true;
	}

	public void RecordSuccess(out bool isStateChanged)
	{
		isStateChanged = false;
		var state = State;
		if (state == CircuitBreakerState.Closed)
		{
			EnsureWindow();
			Interlocked.Increment(ref _throughputCount);
		}
		else if (state == CircuitBreakerState.HalfOpen)
		{
			// test succeeded: close the circuit and reset window
			var old = Interlocked.Exchange(ref _circuitState, (int)CircuitBreakerState.Closed);
			isStateChanged = old != (int)CircuitBreakerState.Closed;
			Interlocked.Exchange(ref _failureCount, 0);
			Interlocked.Exchange(ref _throughputCount, 0);
			Interlocked.Exchange(ref _windowStartTicks, DateTimeOffset.UtcNow.Ticks);
		}
	}

	public void RecordFailure(out bool isStateChanged)
	{
		isStateChanged = false;
		var state = State;
		if (state == CircuitBreakerState.Closed)
		{
			EnsureWindow();
			Interlocked.Increment(ref _throughputCount);
			Interlocked.Increment(ref _failureCount);
			var throughput = Volatile.Read(ref _throughputCount);
			if (throughput >= _minimumThroughput)
			{
				var failureRate = (double)Volatile.Read(ref _failureCount) / throughput;
				if (failureRate >= _failureThreshold)
				{
					Open(out isStateChanged);
				}
			}
		}
		else if (state == CircuitBreakerState.HalfOpen)
		{
			Open(out isStateChanged);
		}
	}

	private void Open(out bool isStateChanged)
	{
		var breakTicks = _breakDurationTicks;
		if (_jitterMaxDurationTicks > 0)
		{
			var jitterMs = ConcurrentRandom.NextDouble() * TimeSpan.FromTicks(_jitterMaxDurationTicks).TotalMilliseconds;
			breakTicks += TimeSpan.FromMilliseconds(jitterMs).Ticks;
		}
		Interlocked.Exchange(ref _openUntilTicks, DateTimeOffset.UtcNow.Ticks + breakTicks);
		Interlocked.Exchange(ref _halfOpenAttempt, 0);
		var old = Interlocked.Exchange(ref _circuitState, (int)CircuitBreakerState.Open);
		isStateChanged = old != (int)CircuitBreakerState.Open;
	}

	public void Close(out bool isStateChanged)
	{
		Interlocked.Exchange(ref _openUntilTicks, DateTimeOffset.MinValue.Ticks);
		Interlocked.Exchange(ref _failureCount, 0);
		Interlocked.Exchange(ref _throughputCount, 0);
		Interlocked.Exchange(ref _windowStartTicks, DateTimeOffset.UtcNow.Ticks);
		var old = Interlocked.Exchange(ref _circuitState, (int)CircuitBreakerState.Closed);
		isStateChanged = old != (int)CircuitBreakerState.Closed;
	}
}
