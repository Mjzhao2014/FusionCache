using System;
using System.Threading;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// A simple circuit-breaker that opens after a configurable number of consecutive failures and remains open for a fixed duration.
/// Once the break duration elapses, the circuit transitions to half-open and allows a single test call:
/// if it succeeds, the circuit closes; if it fails, the circuit opens again.
/// </summary>
internal sealed class SimpleCircuitBreaker : IFusionCacheCircuitBreaker
{
	private readonly int _failuresAllowedBeforeBreaking;
	private readonly long _breakDurationTicks;
	private readonly long _jitterMaxDurationTicks;
	private static readonly Random _random = new Random();
	private int _state;
	private int _failureCount;
	private long _openEndTicks;
	private int _halfOpenTrialInProgress;

	public SimpleCircuitBreaker(int failuresAllowedBeforeBreaking, TimeSpan breakDuration, TimeSpan jitterMaxDuration = default)
	{
		_failuresAllowedBeforeBreaking = failuresAllowedBeforeBreaking <= 0 ? 1 : failuresAllowedBeforeBreaking;
		_breakDurationTicks = breakDuration.Ticks;
		_jitterMaxDurationTicks = jitterMaxDuration.Ticks;
		_state = (int)CircuitBreakerState.Closed;
		_failureCount = 0;
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
			return true;
		}
		if (current == CircuitBreakerState.Open)
		{
			if (nowTicks >= Volatile.Read(ref _openEndTicks))
			{
				// transition to half-open and allow a single call
				var prev = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.HalfOpen);
				// reset trial flag
				Interlocked.Exchange(ref _halfOpenTrialInProgress, 0);
				isStateChanged = prev != (int)CircuitBreakerState.HalfOpen;
				// mark trial in progress
				return Interlocked.CompareExchange(ref _halfOpenTrialInProgress, 1, 0) == 0;
			}
			return false;
		}
		// half-open: see if a trial call is already in progress
		return Interlocked.CompareExchange(ref _halfOpenTrialInProgress, 1, 0) == 0;
	}

	public void RecordSuccess(out bool isStateChanged)
	{
		isStateChanged = false;
		if (_breakDurationTicks == 0)
		{
			Interlocked.Exchange(ref _failureCount, 0);
			return;
		}
		var current = (CircuitBreakerState)Volatile.Read(ref _state);
		if (current == CircuitBreakerState.Closed)
		{
			Interlocked.Exchange(ref _failureCount, 0);
			return;
		}
		if (current == CircuitBreakerState.HalfOpen)
		{
			// trial call succeeded: close circuit
			Interlocked.Exchange(ref _failureCount, 0);
			Interlocked.Exchange(ref _halfOpenTrialInProgress, 0);
			var prev = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
			isStateChanged = prev != (int)CircuitBreakerState.Closed;
			return;
		}
	}

	public void RecordFailure(out bool isStateChanged)
	{
		isStateChanged = false;
		if (_breakDurationTicks == 0)
		{
			Interlocked.Increment(ref _failureCount);
			return;
		}
		var nowTicks = DateTimeOffset.UtcNow.Ticks;
		var current = (CircuitBreakerState)Volatile.Read(ref _state);
		if (current == CircuitBreakerState.Closed)
		{
			var newCount = Interlocked.Increment(ref _failureCount);
			if (newCount >= _failuresAllowedBeforeBreaking)
			{
				// open circuit
				Interlocked.Exchange(ref _openEndTicks, nowTicks + ApplyJitter(_breakDurationTicks));
				Interlocked.Exchange(ref _halfOpenTrialInProgress, 0);
				var prev = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Open);
				isStateChanged = prev != (int)CircuitBreakerState.Open;
			}
			return;
		}
		if (current == CircuitBreakerState.HalfOpen)
		{
			// trial call failed: open again
			Interlocked.Exchange(ref _openEndTicks, nowTicks + ApplyJitter(_breakDurationTicks));
			Interlocked.Exchange(ref _halfOpenTrialInProgress, 0);
			var prev = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Open);
			isStateChanged = prev != (int)CircuitBreakerState.Open;
			return;
		}
		// if open, ignore extra failures
	}

	public void Close(out bool isStateChanged)
	{
		Interlocked.Exchange(ref _failureCount, 0);
		Interlocked.Exchange(ref _halfOpenTrialInProgress, 0);
		Interlocked.Exchange(ref _openEndTicks, DateTimeOffset.MinValue.Ticks);
		var prev = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
		isStateChanged = prev != (int)CircuitBreakerState.Closed;
	}
}
