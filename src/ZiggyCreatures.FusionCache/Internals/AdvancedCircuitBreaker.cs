using System;
using System.Threading;

namespace ZiggyCreatures.Caching.Fusion.Internals
{
	/// <summary>
	/// An advanced circuit breaker implementation that will open when a threshold of failed calls
	/// within a sampling window exceeds a given error rate.
	/// </summary>
	internal sealed class AdvancedCircuitBreaker
		: IFusionCacheCircuitBreaker
	{
		private readonly double _failureThreshold;
		private readonly TimeSpan _samplingDuration;
		private readonly int _minimumThroughput;
		private readonly TimeSpan _breakDuration;
		private readonly TimeSpan _jitterMaxDuration;
		// counts of calls and failures within the current window
		private int _callsCount;
		private int _failureCount;
		private long _windowStartTicks;
		// state and control similar to SimpleCircuitBreaker
		private int _state;
		private long _openUntilTicks;
		private int _halfOpenTestInProgress;
		private readonly object _lock = new object();

		/// <summary>
		/// Constructs an advanced circuit breaker.
		/// </summary>
		/// <param name="failureThreshold">The error rate threshold (between 0 and 1) that will trip the circuit.</param>
		/// <param name="samplingDuration">The duration of the sliding window over which calls are counted.</param>
		/// <param name="minimumThroughput">The minimum number of calls in the window before the error rate will be evaluated.</param>
		/// <param name="breakDuration">The duration the circuit stays open.</param>
		/// <param name="jitterMaxDuration">Optional jitter to add to the break duration.</param>
		public AdvancedCircuitBreaker(double failureThreshold, TimeSpan samplingDuration, int minimumThroughput, TimeSpan breakDuration, TimeSpan jitterMaxDuration = default)
		{
			if (failureThreshold < 0 || failureThreshold > 1)
				throw new ArgumentOutOfRangeException(nameof(failureThreshold));
			_failureThreshold = failureThreshold;
			_samplingDuration = samplingDuration;
			_minimumThroughput = minimumThroughput < 1 ? 1 : minimumThroughput;
			_breakDuration = breakDuration;
			_jitterMaxDuration = jitterMaxDuration;
			_state = (int)CircuitBreakerState.Closed;
			_callsCount = 0;
			_failureCount = 0;
			_windowStartTicks = DateTimeOffset.UtcNow.Ticks;
			_openUntilTicks = DateTimeOffset.MinValue.Ticks;
		}

		public CircuitBreakerState State => (CircuitBreakerState)Volatile.Read(ref _state);
		public int CurrentFailureCount => Volatile.Read(ref _failureCount);

		private void EnsureWindowLocked(DateTimeOffset now)
		{
			// if the sampling duration has elapsed, reset counts
			if (now - new DateTimeOffset(Volatile.Read(ref _windowStartTicks), TimeSpan.Zero) >= _samplingDuration)
			{
				_callsCount = 0;
				_failureCount = 0;
				_windowStartTicks = now.Ticks;
			}
		}

		private TimeSpan GetBreakDurationWithJitter()
		{
			var duration = _breakDuration;
			if (_jitterMaxDuration > TimeSpan.Zero)
			{
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
			lock (_lock)
			{
				_callsCount = 0;
				_failureCount = 0;
				_windowStartTicks = DateTimeOffset.UtcNow.Ticks;
			}
			var oldState = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Open);
			isStateChanged = oldState != (int)CircuitBreakerState.Open;
		}

		public bool TryExecute(out bool isStateChanged)
		{
			isStateChanged = false;
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
						if (DateTimeOffset.UtcNow.Ticks >= Volatile.Read(ref _openUntilTicks))
						{
							if (Interlocked.CompareExchange(ref _state, (int)CircuitBreakerState.HalfOpen, (int)CircuitBreakerState.Open) == (int)CircuitBreakerState.Open)
							{
								Interlocked.Exchange(ref _halfOpenTestInProgress, 0);
								isStateChanged = true;
							}
							continue;
						}
						return false;
					case CircuitBreakerState.HalfOpen:
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

		public void RecordSuccess(out bool isStateChanged)
		{
			isStateChanged = false;
			var state = (CircuitBreakerState)Volatile.Read(ref _state);
			if (state == CircuitBreakerState.Closed)
			{
				lock (_lock)
				{
					EnsureWindowLocked(DateTimeOffset.UtcNow);
					_callsCount++;
				}
			}
			else if (state == CircuitBreakerState.HalfOpen)
			{
				lock (_lock)
				{
					// reset window counts when closing
					_callsCount = 0;
					_failureCount = 0;
					_windowStartTicks = DateTimeOffset.UtcNow.Ticks;
				}
				var old = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
				isStateChanged = old != (int)CircuitBreakerState.Closed;
			}
		}

		public void RecordFailure(out bool isStateChanged)
		{
			isStateChanged = false;
			var state = (CircuitBreakerState)Volatile.Read(ref _state);
			if (state == CircuitBreakerState.Closed)
			{
				var now = DateTimeOffset.UtcNow;
				lock (_lock)
				{
					EnsureWindowLocked(now);
					_callsCount++;
					_failureCount++;
					if (_callsCount >= _minimumThroughput)
					{
						double errorRate = (double)_failureCount / (double)_callsCount;
						if (errorRate >= _failureThreshold)
						{
							// open the circuit
							OpenCircuit(out isStateChanged);
						}
					}
				}
			}
			else if (state == CircuitBreakerState.HalfOpen)
			{
				OpenCircuit(out isStateChanged);
			}
		}

		public void Close(out bool isStateChanged)
		{
			lock (_lock)
			{
				_callsCount = 0;
				_failureCount = 0;
				_windowStartTicks = DateTimeOffset.UtcNow.Ticks;
			}
			var old = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
			isStateChanged = old != (int)CircuitBreakerState.Closed;
		}
	}
}
