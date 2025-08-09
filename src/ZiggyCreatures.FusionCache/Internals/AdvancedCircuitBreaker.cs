using System;
using System.Threading;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// An advanced circuit breaker which opens when the error rate over a moving sampling window exceeds a configured threshold.
/// It requires a minimum throughput of calls in the sampling window before evaluating the failure rate. When opened, it will
/// remain open for a defined duration before transitioning to half-open, where a single trial call determines whether to
/// close or open the circuit again.
/// </summary>
internal sealed class AdvancedCircuitBreaker : IFusionCacheCircuitBreaker
{
	private readonly double _failureThreshold;
	private readonly long _samplingDurationTicks;
	private readonly int _minimumThroughput;
	private readonly long _breakDurationTicks;
	private readonly long _jitterMaxDurationTicks;
	private static readonly Random _random = new Random();

	private int _state;
	private int _callCount;
	private int _failureCount;
	private long _windowStartTicks;
	private long _openEndTicks;
	private int _halfOpenTrialInProgress;

	public AdvancedCircuitBreaker(double failureThreshold, TimeSpan samplingDuration, int minimumThroughput, TimeSpan breakDuration, TimeSpan jitterMaxDuration = default)
	{
		if (failureThreshold <= 0.0 || failureThreshold > 1.0)
			throw new ArgumentOutOfRangeException(nameof(failureThreshold), "Failure threshold must be between 0 (exclusive) and 1 (inclusive)");
		if (minimumThroughput <= 0)
			throw new ArgumentOutOfRangeException(nameof(minimumThroughput), "Minimum throughput must be greater than 0");

		_failureThreshold = failureThreshold;
		_samplingDurationTicks = samplingDuration.Ticks;
		_minimumThroughput = minimumThroughput;
		_breakDurationTicks = breakDuration.Ticks;
		_jitterMaxDurationTicks = jitterMaxDuration.Ticks;
		_state = (int)CircuitBreakerState.Closed;
		_callCount = 0;
		_failureCount = 0;
		_windowStartTicks = 0L;
		_openEndTicks = DateTimeOffset.MinValue.Ticks;
		_halfOpenTrialInProgress = 0;
	}

	public CircuitBreakerState State => (CircuitBreakerState)Volatile.Read(ref _state);

	public int CurrentFailureCount => Volatile.Read(ref _failureCount);

	private long ApplyJitter(long baseTicks)
	{
		if (_jitterMaxDurationTicks <= 0)
			return baseTicks;

		lock (_random)
		{
			var jitterTicks = (long)(_jitterMaxDurationTicks * _random.NextDouble());
			return baseTicks + jitterTicks;
		}
	}

	private void ResetIfWindowExpired(long nowTicks)
	{
		var windowStart = Volatile.Read(ref _windowStartTicks);
		if (windowStart == 0)
		{
			// initialize window start
			Interlocked.CompareExchange(ref _windowStartTicks, nowTicks, 0);
			return;
		}
		if (nowTicks - windowStart >= _samplingDurationTicks)
		{
			Interlocked.Exchange(ref _windowStartTicks, nowTicks);
			Interlocked.Exchange(ref _callCount, 0);
			Interlocked.Exchange(ref _failureCount, 0);
		}
	}

	public bool TryExecute(out bool isStateChanged)
	{
		isStateChanged = false;
		if (_breakDurationTicks == 0)
		{
			return true;
		}
		var nowTicks = DateTimeOffset.UtcNow.Ticks;
		var current = (CircuitBreakerState)Volatile.Read(ref _state);
		if (current == CircuitBreakerState.Closed)
		{
			ResetIfWindowExpired(nowTicks);
			return true;
		}
		if (current == CircuitBreakerState.Open)
		{
			if (nowTicks >= Volatile.Read(ref _openEndTicks))
			{
				var prev = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.HalfOpen);
				Interlocked.Exchange(ref _halfOpenTrialInProgress, 0);
				isStateChanged = prev != (int)CircuitBreakerState.HalfOpen;
				return Interlocked.CompareExchange(ref _halfOpenTrialInProgress, 1, 0) == 0;
			}
			return false;
		}
		// half-open
		return Interlocked.CompareExchange(ref _halfOpenTrialInProgress, 1, 0) == 0;
	}

	public void RecordSuccess(out bool isStateChanged)
	{
		isStateChanged = false;
		if (_breakDurationTicks == 0)
		{
			return;
		}
		var nowTicks = DateTimeOffset.UtcNow.Ticks;
		var current = (CircuitBreakerState)Volatile.Read(ref _state);
		if (current == CircuitBreakerState.Closed)
		{
			ResetIfWindowExpired(nowTicks);
			Interlocked.Increment(ref _callCount);
			return;
		}
		if (current == CircuitBreakerState.HalfOpen)
		{
			// trial call succeeded -> close circuit and reset counts
			Interlocked.Exchange(ref _callCount, 0);
			Interlocked.Exchange(ref _failureCount, 0);
			Interlocked.Exchange(ref _windowStartTicks, nowTicks);
			Interlocked.Exchange(ref _halfOpenTrialInProgress, 0);
			var prev = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
			isStateChanged = prev != (int)CircuitBreakerState.Closed;
		}
	}

	public void RecordFailure(out bool isStateChanged)
	{
		isStateChanged = false;
		if (_breakDurationTicks == 0)
		{
			return;
		}
		var nowTicks = DateTimeOffset.UtcNow.Ticks;
		var current = (CircuitBreakerState)Volatile.Read(ref _state);
		if (current == CircuitBreakerState.Closed)
		{
			ResetIfWindowExpired(nowTicks);
			var total = Interlocked.Increment(ref _callCount);
			var failures = Interlocked.Increment(ref _failureCount);
			if (total >= _minimumThroughput)
			{
				var failureRate = (double)failures / total;
				if (failureRate >= _failureThreshold)
				{
					Interlocked.Exchange(ref _openEndTicks, nowTicks + ApplyJitter(_breakDurationTicks));
					Interlocked.Exchange(ref _halfOpenTrialInProgress, 0);
					var prev = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Open);
					isStateChanged = prev != (int)CircuitBreakerState.Open;
				}
			}
			return;
		}
		if (current == CircuitBreakerState.HalfOpen)
		{
			// trial call failed -> open again
			Interlocked.Exchange(ref _openEndTicks, nowTicks + ApplyJitter(_breakDurationTicks));
			Interlocked.Exchange(ref _halfOpenTrialInProgress, 0);
			var prev = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Open);
			isStateChanged = prev != (int)CircuitBreakerState.Open;
		}
	}

	public void Close(out bool isStateChanged)
	{
		Interlocked.Exchange(ref _callCount, 0);
		Interlocked.Exchange(ref _failureCount, 0);
		Interlocked.Exchange(ref _halfOpenTrialInProgress, 0);
		Interlocked.Exchange(ref _windowStartTicks, 0);
		Interlocked.Exchange(ref _openEndTicks, DateTimeOffset.MinValue.Ticks);
		var prev = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
		isStateChanged = prev != (int)CircuitBreakerState.Closed;
	}
}
