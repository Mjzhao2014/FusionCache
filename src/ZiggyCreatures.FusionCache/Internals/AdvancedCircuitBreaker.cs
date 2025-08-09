using System;
using System.Threading;

namespace ZiggyCreatures.Caching.Fusion.Internals
{
	/// <summary>
	/// An advanced circuit breaker implementation that trips when the failure rate over a sampling window exceeds a threshold.
	/// After opening, the circuit remains open for the configured break duration (plus optional jitter) and then transitions
	/// to half-open, where exactly one trial call is allowed to probe recovery.
	/// </summary>
	internal sealed class AdvancedCircuitBreaker : IFusionCacheCircuitBreaker
	{
		private readonly double _failureThreshold;
		private readonly TimeSpan _samplingDuration;
		private readonly int _minimumThroughput;
		private readonly TimeSpan _breakDuration;
		private readonly TimeSpan _jitterMaxDuration;
		private int _state;
		private long _blockedUntilTicks;
		private int _halfOpenActive;
		private readonly object _lockObj = new object();
		private int _failureCount;
		private int _successCount;
		private long _windowStartTicks;

		/// <summary>
		/// Creates a new <see cref="AdvancedCircuitBreaker"/> instance.
		/// </summary>
		/// <param name="failureThreshold">The failure threshold (0-1) that, when exceeded within the sampling window, will open the circuit.</param>
		/// <param name="samplingDuration">The duration over which call outcomes are sampled to compute the failure rate.</param>
		/// <param name="minimumThroughput">The minimum number of calls that must occur in the sampling window before evaluating the failure rate.</param>
		/// <param name="breakDuration">How long the circuit should stay open when tripped.</param>
		/// <param name="jitterMaxDuration">Optional jitter to add to the break duration when opening.</param>
		public AdvancedCircuitBreaker(double failureThreshold, TimeSpan samplingDuration, int minimumThroughput, TimeSpan breakDuration, TimeSpan jitterMaxDuration = default)
		{
			_failureThreshold = failureThreshold;
			_samplingDuration = samplingDuration;
			_minimumThroughput = minimumThroughput;
			_breakDuration = breakDuration;
			_jitterMaxDuration = jitterMaxDuration;
			_state = (int)CircuitBreakerState.Closed;
			_blockedUntilTicks = DateTimeOffset.MinValue.Ticks;
			_halfOpenActive = 0;
			_failureCount = 0;
			_successCount = 0;
			_windowStartTicks = DateTimeOffset.UtcNow.Ticks;
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
				var current = (CircuitBreakerState)Volatile.Read(ref _state);
				if (current == CircuitBreakerState.Closed)
				{
					return true;
				}
				else if (current == CircuitBreakerState.Open)
				{
					var untilTicks = Interlocked.Read(ref _blockedUntilTicks);
					if (DateTimeOffset.UtcNow.Ticks >= untilTicks)
					{
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
					current = (CircuitBreakerState)Volatile.Read(ref _state);
				}
				if (current == CircuitBreakerState.HalfOpen)
				{
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
			var current = (CircuitBreakerState)Volatile.Read(ref _state);
			if (current == CircuitBreakerState.HalfOpen)
			{
				Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
				ResetWindow();
				Interlocked.Exchange(ref _halfOpenActive, 0);
				isStateChanged = true;
			}
			else if (current == CircuitBreakerState.Closed)
			{
				lock (_lockObj)
				{
					EnsureWindow();
					_successCount++;
				}
			}
		}

		/// <inheritdoc/>
		public void RecordFailure(out bool isStateChanged)
		{
			isStateChanged = false;
			var current = (CircuitBreakerState)Volatile.Read(ref _state);
			if (current == CircuitBreakerState.HalfOpen)
			{
				OpenInternal();
				ResetWindow();
				Interlocked.Exchange(ref _halfOpenActive, 0);
				isStateChanged = true;
			}
			else if (current == CircuitBreakerState.Closed)
			{
				bool open = false;
				lock (_lockObj)
				{
					EnsureWindow();
					_failureCount++;
					var total = _failureCount + _successCount;
					if (total >= _minimumThroughput)
					{
						var failureRate = (double)_failureCount / total;
						if (failureRate >= _failureThreshold)
						{
							open = true;
						}
					}
				}
				if (open)
				{
					OpenInternal();
					ResetWindow();
					isStateChanged = true;
				}
			}
		}

		/// <inheritdoc/>
		public void Close(out bool isStateChanged)
		{
			var prev = (CircuitBreakerState)Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
			isStateChanged = prev != CircuitBreakerState.Closed;
			Interlocked.Exchange(ref _blockedUntilTicks, DateTimeOffset.MinValue.Ticks);
			Interlocked.Exchange(ref _halfOpenActive, 0);
			ResetWindow();
		}

		private void EnsureWindow()
		{
			var nowTicks = DateTimeOffset.UtcNow.Ticks;
			var windowTicks = Volatile.Read(ref _windowStartTicks);
			if (nowTicks - windowTicks >= _samplingDuration.Ticks)
			{
				_windowStartTicks = nowTicks;
				_failureCount = 0;
				_successCount = 0;
			}
		}

		private void ResetWindow()
		{
			lock (_lockObj)
			{
				_failureCount = 0;
				_successCount = 0;
				_windowStartTicks = DateTimeOffset.UtcNow.Ticks;
			}
		}

		private void OpenInternal()
		{
			var duration = _breakDuration + GetJitter();
			Interlocked.Exchange(ref _blockedUntilTicks, DateTimeOffset.UtcNow.Add(duration).Ticks);
			Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Open);
		}

		private TimeSpan GetJitter()
		{
			if (_jitterMaxDuration <= TimeSpan.Zero)
				return TimeSpan.Zero;
			return TimeSpan.FromMilliseconds(ConcurrentRandom.NextDouble() * _jitterMaxDuration.TotalMilliseconds);
		}
	}
}
