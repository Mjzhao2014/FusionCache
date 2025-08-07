using System.Threading;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// An advanced circuit breaker that will open if the ratio of failures over a sampling window exceeds a configured threshold.
/// Once open the circuit will stay open for the configured break duration, after which a single half-open test call will be allowed to
/// determine if the circuit should close or open again.
/// </summary>
internal sealed class AdvancedCircuitBreaker
	: IFusionCacheCircuitBreaker
{
	private readonly double _failureThreshold;
	private readonly int _minimumThroughput;
	private readonly long _samplingDurationTicks;
	private readonly long _breakDurationTicks;
	private int _circuitState;
	private long _openUntilTicks;
	private int _halfOpenExecutionAllowed;
	private long _samplingWindowStartTicks;
	private long _totalCount;
	private long _failureCount;

	public AdvancedCircuitBreaker(double failureThreshold, TimeSpan samplingDuration, int minimumThroughput, TimeSpan breakDuration)
	{
		_failureThreshold = failureThreshold;
		_samplingDurationTicks = samplingDuration.Ticks;
		_minimumThroughput = minimumThroughput;
		_breakDurationTicks = breakDuration.Ticks;
		_circuitState = (int)CircuitBreakerState.Closed;
		_samplingWindowStartTicks = DateTimeOffset.UtcNow.Ticks;
		_halfOpenExecutionAllowed = 1;
	}

	public CircuitBreakerState State => (CircuitBreakerState)Volatile.Read(ref _circuitState);

	public int CurrentFailureCount => (int)Volatile.Read(ref _failureCount);

	private void EnsureSamplingWindow()
	{
		if (_samplingDurationTicks <= 0)
		{
			// no sampling window => treat as infinite window
			return;
		}
		var nowTicks = DateTimeOffset.UtcNow.Ticks;
		var windowStart = Volatile.Read(ref _samplingWindowStartTicks);
		if (nowTicks - windowStart > _samplingDurationTicks)
		{
			// reset counts
			Interlocked.Exchange(ref _samplingWindowStartTicks, nowTicks);
			Interlocked.Exchange(ref _totalCount, 0);
			Interlocked.Exchange(ref _failureCount, 0);
		}
	}

	public bool TryExecute(out bool isStateChanged)
	{
		isStateChanged = false;
		// circuit disabled if break duration 0
		if (_breakDurationTicks == 0)
			return true;
		var state = (CircuitBreakerState)Volatile.Read(ref _circuitState);
		var nowTicks = DateTimeOffset.UtcNow.Ticks;
		if (state == CircuitBreakerState.Closed)
		{
			return true;
		}
		if (state == CircuitBreakerState.Open)
		{
			// still open?
			if (nowTicks < Volatile.Read(ref _openUntilTicks))
				return false;
			// move to half-open
			var old = Interlocked.CompareExchange(ref _circuitState, (int)CircuitBreakerState.HalfOpen, (int)CircuitBreakerState.Open);
			if (old == (int)CircuitBreakerState.Open)
			{
				isStateChanged = true;
				Interlocked.Exchange(ref _halfOpenExecutionAllowed, 1);
			}
			return Interlocked.Exchange(ref _halfOpenExecutionAllowed, 0) == 1;
		}
		// Half-open: allow at most one execution
		return Interlocked.Exchange(ref _halfOpenExecutionAllowed, 0) == 1;
	}

	public void RecordSuccess(out bool isStateChanged)
	{
		isStateChanged = false;
		var state = (CircuitBreakerState)Volatile.Read(ref _circuitState);
		if (state == CircuitBreakerState.Closed)
		{
			EnsureSamplingWindow();
			Interlocked.Increment(ref _totalCount);
			// nothing else on success
		}
		else if (state == CircuitBreakerState.HalfOpen)
		{
			Interlocked.Exchange(ref _circuitState, (int)CircuitBreakerState.Closed);
			// reset counts and window
			Interlocked.Exchange(ref _totalCount, 0);
			Interlocked.Exchange(ref _failureCount, 0);
			Interlocked.Exchange(ref _samplingWindowStartTicks, DateTimeOffset.UtcNow.Ticks);
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
			EnsureSamplingWindow();
			var tot = Interlocked.Increment(ref _totalCount);
			var failures = Interlocked.Increment(ref _failureCount);
			if (tot >= _minimumThroughput)
			{
				double errorRate = (double)failures / tot;
				if (errorRate >= _failureThreshold)
				{
					if (_breakDurationTicks == 0)
						return;
					Interlocked.Exchange(ref _openUntilTicks, nowTicks + _breakDurationTicks);
					var old = Interlocked.Exchange(ref _circuitState, (int)CircuitBreakerState.Open);
					isStateChanged = old != (int)CircuitBreakerState.Open;
					// reset counts when open
					Interlocked.Exchange(ref _totalCount, 0);
					Interlocked.Exchange(ref _failureCount, 0);
				}
			}
		}
		else if (state == CircuitBreakerState.HalfOpen)
		{
			if (_breakDurationTicks == 0)
				return;
			Interlocked.Exchange(ref _openUntilTicks, nowTicks + _breakDurationTicks);
			var old = Interlocked.Exchange(ref _circuitState, (int)CircuitBreakerState.Open);
			isStateChanged = old != (int)CircuitBreakerState.Open;
		}
	}

	public void Close(out bool isStateChanged)
	{
		var old = Interlocked.Exchange(ref _circuitState, (int)CircuitBreakerState.Closed);
		Interlocked.Exchange(ref _totalCount, 0);
		Interlocked.Exchange(ref _failureCount, 0);
		Interlocked.Exchange(ref _samplingWindowStartTicks, DateTimeOffset.UtcNow.Ticks);
		Interlocked.Exchange(ref _halfOpenExecutionAllowed, 1);
		isStateChanged = old != (int)CircuitBreakerState.Closed;
	}
}
