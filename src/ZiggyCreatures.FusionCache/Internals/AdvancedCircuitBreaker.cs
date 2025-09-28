using System;

using ZiggyCreatures.Caching.Fusion;
namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// An advanced circuit breaker that monitors the failure rate of calls in a sampling window and opens the circuit
/// if the error rate exceeds the configured threshold.
/// </summary>
internal sealed class AdvancedCircuitBreaker
	: IFusionCacheCircuitBreaker
{
	private readonly double _failureThreshold;
	private readonly TimeSpan _samplingDuration;
	private readonly int _minimumThroughput;
	private readonly TimeSpan _breakDuration;
	private readonly TimeSpan _jitterMaxDuration;

	private long _windowStartTicks;
	private int _failureCount;
	private int _successCount;
	private int _state;
	private long _openEndTicks;
	private int _halfOpenCalled;

	public AdvancedCircuitBreaker(double failureThreshold, TimeSpan samplingDuration, int minimumThroughput, TimeSpan breakDuration, TimeSpan jitterMaxDuration = default)
	{
		if (failureThreshold <= 0d || failureThreshold > 1d)
		{
			throw new ArgumentOutOfRangeException(nameof(failureThreshold), "Failure threshold must be in the range (0,1].");
		}
		_failureThreshold = failureThreshold;
		_samplingDuration = samplingDuration;
		_minimumThroughput = Math.Max(1, minimumThroughput);
		_breakDuration = breakDuration;
		_jitterMaxDuration = jitterMaxDuration;
		_state = (int)CircuitBreakerState.Closed;
		_failureCount = 0;
		_successCount = 0;
		_windowStartTicks = DateTimeOffset.UtcNow.Ticks;
		_openEndTicks = DateTimeOffset.MinValue.Ticks;
		_halfOpenCalled = 0;
	}

	public CircuitBreakerState State => (CircuitBreakerState)_state;

	public int CurrentFailureCount => Volatile.Read(ref _failureCount);

	public bool TryExecute(out bool isStateChanged)
	{
		isStateChanged = false;
		if (_breakDuration <= TimeSpan.Zero)
			return true;
		var currState = (CircuitBreakerState)Volatile.Read(ref _state);
		if (currState == CircuitBreakerState.Open)
		{
			var now = DateTimeOffset.UtcNow;
			if (now.Ticks >= Volatile.Read(ref _openEndTicks))
			{
				var prev = Interlocked.CompareExchange(ref _state, (int)CircuitBreakerState.HalfOpen, (int)CircuitBreakerState.Open);
				if (prev == (int)CircuitBreakerState.Open)
				{
					isStateChanged = true;
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
			if (Interlocked.CompareExchange(ref _halfOpenCalled, 1, 0) != 0)
			{
				return false;
			}
		}
		return true;
	}

	private void EnsureWindow()
	{
		var nowTicks = DateTimeOffset.UtcNow.Ticks;
		var start = Volatile.Read(ref _windowStartTicks);
		if (nowTicks - start >= _samplingDuration.Ticks)
		{
			// reset sampling window
			Interlocked.Exchange(ref _windowStartTicks, nowTicks);
			Volatile.Write(ref _failureCount, 0);
			Volatile.Write(ref _successCount, 0);
		}
	}

	private void Evaluate(out bool isStateChanged)
	{
		isStateChanged = false;
		var total = Volatile.Read(ref _successCount) + Volatile.Read(ref _failureCount);
		if (total < _minimumThroughput)
			return;
		var failureRate = total == 0 ? 0d : (double)Volatile.Read(ref _failureCount) / total;
		if (failureRate >= _failureThreshold)
		{
			Open(out isStateChanged);
		}
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
			EnsureWindow();
			Interlocked.Increment(ref _successCount);
			// evaluate on success to possibly close transition: success can bring down error rate, but will not trip open.
		}
	}

	public void RecordFailure(out bool isStateChanged)
	{
		isStateChanged = false;
		var currState = (CircuitBreakerState)Volatile.Read(ref _state);
		if (currState == CircuitBreakerState.HalfOpen)
		{
			Open(out isStateChanged);
		}
		else if (currState == CircuitBreakerState.Closed)
		{
			EnsureWindow();
			Interlocked.Increment(ref _failureCount);
			Evaluate(out isStateChanged);
		}
	}

	public void Close(out bool isStateChanged)
	{
		var old = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
		// reset counts and window
		Volatile.Write(ref _failureCount, 0);
		Volatile.Write(ref _successCount, 0);
		Volatile.Write(ref _windowStartTicks, DateTimeOffset.UtcNow.Ticks);
		Volatile.Write(ref _openEndTicks, DateTimeOffset.MinValue.Ticks);
		Volatile.Write(ref _halfOpenCalled, 0);
		isStateChanged = old != (int)CircuitBreakerState.Closed;
	}

	private void Open(out bool isStateChanged)
	{
		var dur = _breakDuration;
		if (_jitterMaxDuration > TimeSpan.Zero)
		{
			var jitterMs = ConcurrentRandom.NextDouble() * _jitterMaxDuration.TotalMilliseconds;
			dur = dur.Add(TimeSpan.FromMilliseconds(jitterMs));
		}
		Volatile.Write(ref _openEndTicks, DateTimeOffset.UtcNow.Add(dur).Ticks);
		var prev = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Open);
		Volatile.Write(ref _halfOpenCalled, 0);
		isStateChanged = prev != (int)CircuitBreakerState.Open;
	}
}
