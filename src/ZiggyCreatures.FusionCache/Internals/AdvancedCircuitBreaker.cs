using System.Collections.Concurrent;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// An advanced circuit breaker with half-open state, failure counting, and sophisticated recovery logic.
/// </summary>
internal sealed class AdvancedCircuitBreaker : IFusionCacheCircuitBreaker
{
	private const int StateClosed = 0;
	private const int StateOpen = 1;
	private const int StateHalfOpen = 2;

	private readonly AdvancedCircuitBreakerOptions _options;
	private readonly ConcurrentQueue<long> _failureTimes;
	private readonly object _stateLock = new object();

	private volatile int _state = StateClosed;
	private long _openedAtTicks = 0;
	private volatile int _halfOpenCallCount = 0;

	/// <summary>
	/// Creates a new <see cref="AdvancedCircuitBreaker"/> instance.
	/// </summary>
	/// <param name="options">The configuration options for the circuit breaker.</param>
	public AdvancedCircuitBreaker(AdvancedCircuitBreakerOptions options)
	{
		_options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
		_failureTimes = new ConcurrentQueue<long>();
	}

	/// <inheritdoc/>
	public CircuitBreakerState State => (CircuitBreakerState)_state;

	/// <inheritdoc/>
	public int CurrentFailureCount
	{
		get
		{
			CleanupExpiredFailures();
			return _failureTimes.Count;
		}
	}

	/// <inheritdoc/>
	public bool TryExecute(out bool isStateChanged)
	{
		isStateChanged = false;

		var currentState = _state;

		switch (currentState)
		{
			case StateClosed:
				return true;

			case StateOpen:
				if (ShouldAttemptReset())
				{
					lock (_stateLock)
					{
						if (_state == StateOpen && ShouldAttemptReset())
						{
							_state = StateHalfOpen;
							_halfOpenCallCount = 0;
							isStateChanged = true;
						}
					}
					return _state == StateHalfOpen;
				}
				return false;

			case StateHalfOpen:
				lock (_stateLock)
				{
					if (_state == StateHalfOpen)
					{
						if (_halfOpenCallCount < 1)
						{
							_halfOpenCallCount++;
							return true;
						}
					}
				}
				return false;

			default:
				return false;
		}
	}

	/// <inheritdoc/>
	public void RecordSuccess(out bool isStateChanged)
	{
		isStateChanged = false;
		var currentState = _state;

		if (currentState == StateHalfOpen)
		{
			lock (_stateLock)
			{
				if (_state == StateHalfOpen)
				{
					if (_halfOpenCallCount >= 1)
					{
						_state = StateClosed;
						_halfOpenCallCount = 0;
						ClearFailures();
						isStateChanged = true;
					}
				}
			}
		}
	}

	/// <inheritdoc/>
	public void RecordFailure(out bool isStateChanged)
	{
		isStateChanged = false;
		var currentTicks = DateTimeOffset.UtcNow.Ticks;
		_failureTimes.Enqueue(currentTicks);

		var currentState = _state;

		if (currentState == StateHalfOpen)
		{
			lock (_stateLock)
			{
				if (_state == StateHalfOpen)
				{
					_state = StateOpen;
					Interlocked.Exchange(ref _openedAtTicks, currentTicks);
					_halfOpenCallCount = 0;
					isStateChanged = true;
				}
			}
		}
		else if (currentState == StateClosed)
		{
			CleanupExpiredFailures();
			
			if (_failureTimes.Count >= _options.FailureThreshold)
			{
				lock (_stateLock)
				{
					if (_state == StateClosed && _failureTimes.Count >= _options.FailureThreshold)
					{
						_state = StateOpen;
						Interlocked.Exchange(ref _openedAtTicks, currentTicks);
						isStateChanged = true;
					}
				}
			}
		}
	}

	/// <inheritdoc/>
	public void Close(out bool isStateChanged)
	{
		lock (_stateLock)
		{
			var oldState = _state;
			_state = StateClosed;
			_halfOpenCallCount = 0;
			ClearFailures();
			isStateChanged = oldState != StateClosed;
		}
	}

	private bool ShouldAttemptReset()
	{
		var currentTicks = DateTimeOffset.UtcNow.Ticks;
		var breakDurationTicks = _options.DurationOfBreak.Ticks;
		return currentTicks - Interlocked.Read(ref _openedAtTicks) >= breakDurationTicks;
	}

	private void CleanupExpiredFailures()
	{
		var cutoffTicks = DateTimeOffset.UtcNow.Ticks - _options.SamplingDuration.Ticks;
		
		while (_failureTimes.TryPeek(out var oldestFailure) && oldestFailure < cutoffTicks)
		{
			_failureTimes.TryDequeue(out _);
		}
	}

	private void ClearFailures()
	{
		while (_failureTimes.TryDequeue(out _))
		{
			// Clear all failure records
		}
	}
}