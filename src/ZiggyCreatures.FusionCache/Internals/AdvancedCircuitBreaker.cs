using System;
using System.Threading;

namespace ZiggyCreatures.Caching.Fusion.Internals
{
	/// <summary>
	/// A circuit breaker that opens based on an error rate over a sampling window.
	/// </summary>
	internal sealed class AdvancedCircuitBreaker : IFusionCacheCircuitBreaker
	{
		private readonly double _failureThreshold;
		private readonly TimeSpan _samplingDuration;
		private readonly int _minimumThroughput;
		private readonly TimeSpan _breakDuration;
		private readonly TimeSpan _jitterMaxDuration;
		private long _windowStartTicks;
		private int _failureCount;
		private int _callCount;
		private long _blockedUntilTicks;
		private int _state;
		private int _halfOpenAllowed;

		/// <summary>
		/// Creates a new <see cref="AdvancedCircuitBreaker"/> instance.
		/// </summary>
		/// <param name="failureThreshold">The failure rate (0-1) that, when exceeded over the sampling window, will open the circuit.</param>
		/// <param name="samplingDuration">The duration of the sampling window.</param>
		/// <param name="minimumThroughput">The minimum number of calls before failure rate evaluation.</param>
		/// <param name="breakDuration">The duration to keep the circuit open once tripped.</param>
		/// <param name="jitterMaxDuration">Optional max jitter duration to add to break duration.</param>
		public AdvancedCircuitBreaker(double failureThreshold, TimeSpan samplingDuration, int minimumThroughput, TimeSpan breakDuration, TimeSpan jitterMaxDuration = default)
		{
			if (failureThreshold <= 0d || failureThreshold > 1d)
				throw new ArgumentOutOfRangeException(nameof(failureThreshold));
			_failureThreshold = failureThreshold;
			_samplingDuration = samplingDuration;
			_minimumThroughput = minimumThroughput <= 0 ? 1 : minimumThroughput;
			_breakDuration = breakDuration;
			_jitterMaxDuration = jitterMaxDuration;
			_state = (int)CircuitBreakerState.Closed;
			_windowStartTicks = DateTimeOffset.UtcNow.Ticks;
			_failureCount = 0;
			_callCount = 0;
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
			// reset counts
			Interlocked.Exchange(ref _failureCount, 0);
			Interlocked.Exchange(ref _callCount, 0);
			Interlocked.Exchange(ref _windowStartTicks, DateTimeOffset.UtcNow.Ticks);
		}

		private void EnsureWindow()
		{
			var nowTicks = DateTimeOffset.UtcNow.Ticks;
			var start = Volatile.Read(ref _windowStartTicks);
			if (TimeSpan.FromTicks(nowTicks - start) >= _samplingDuration)
			{
				// reset window
				Interlocked.Exchange(ref _windowStartTicks, nowTicks);
				Interlocked.Exchange(ref _failureCount, 0);
				Interlocked.Exchange(ref _callCount, 0);
			}
		}

		public CircuitBreakerState State => (CircuitBreakerState)Volatile.Read(ref _state);

		public bool TryExecute(out bool isStateChanged)
		{
			isStateChanged = false;
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
				if (Interlocked.CompareExchange(ref _state, (int)CircuitBreakerState.HalfOpen, (int)CircuitBreakerState.Open) == (int)CircuitBreakerState.Open)
				{
					isStateChanged = true;
					Interlocked.Exchange(ref _halfOpenAllowed, 1);
				}
				curState = (CircuitBreakerState)Volatile.Read(ref _state);
			}
			if (curState == CircuitBreakerState.HalfOpen)
			{
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
				EnsureWindow();
				Interlocked.Increment(ref _callCount);
				return;
			}
			if (curState == CircuitBreakerState.HalfOpen)
			{
				Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
				Interlocked.Exchange(ref _failureCount, 0);
				Interlocked.Exchange(ref _callCount, 0);
				Interlocked.Exchange(ref _windowStartTicks, DateTimeOffset.UtcNow.Ticks);
				isStateChanged = true;
			}
		}

		public void RecordFailure(out bool isStateChanged)
		{
			isStateChanged = false;
			var curState = (CircuitBreakerState)Volatile.Read(ref _state);
			if (curState == CircuitBreakerState.Closed)
			{
				EnsureWindow();
				Interlocked.Increment(ref _callCount);
				var newFailures = Interlocked.Increment(ref _failureCount);
				var calls = Volatile.Read(ref _callCount);
				if (calls >= _minimumThroughput)
				{
					var failureRate = (double)newFailures / calls;
					if (failureRate >= _failureThreshold)
					{
						OpenCircuit();
						isStateChanged = true;
					}
				}
				return;
			}
			if (curState == CircuitBreakerState.HalfOpen)
			{
				OpenCircuit();
				isStateChanged = true;
			}
		}

		public void Close(out bool isStateChanged)
		{
			var prev = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
			isStateChanged = prev != (int)CircuitBreakerState.Closed;
			Interlocked.Exchange(ref _failureCount, 0);
			Interlocked.Exchange(ref _callCount, 0);
			Interlocked.Exchange(ref _windowStartTicks, DateTimeOffset.UtcNow.Ticks);
			Interlocked.Exchange(ref _halfOpenAllowed, 0);
			Interlocked.Exchange(ref _blockedUntilTicks, DateTimeOffset.MinValue.Ticks);
		}

		public int CurrentFailureCount => Volatile.Read(ref _failureCount);
	}
}
