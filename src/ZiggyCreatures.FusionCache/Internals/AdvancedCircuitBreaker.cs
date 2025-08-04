using System.Diagnostics.CodeAnalysis;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// An advanced circuit breaker implementation that trips based on error rate thresholds over a sliding sampling window.
/// </summary>
internal sealed class AdvancedCircuitBreaker
    : IFusionCacheCircuitBreaker
{
    private const int CircuitStateClosed = (int)CircuitBreakerState.Closed;
    private const int CircuitStateOpen = (int)CircuitBreakerState.Open;
    private const int CircuitStateHalfOpen = (int)CircuitBreakerState.HalfOpen;

    private readonly double _failureThreshold;
    private readonly int _minimumThroughput;
    private readonly long _samplingDurationTicks;
    private readonly long _durationOfBreakTicks;
    private int _state;
    private int _halfOpenAttempted;
    private long _blockedUntilTicks;

    private readonly object _lock = new();
    private long _windowStartTicks;
    private int _callCount;
    private int _failureCount;

    /// <summary>
    /// Creates a new <see cref="AdvancedCircuitBreaker"/> instance.
    /// </summary>
    /// <param name="failureThreshold">The error rate threshold (0 &lt; x ≤ 1) above which the circuit will open.</param>
    /// <param name="samplingDuration">The duration over which to sample calls to compute the error rate.</param>
    /// <param name="minimumThroughput">The minimum number of calls in the sampling window before the failure rate is evaluated.</param>
    /// <param name="durationOfBreak">The amount of time the circuit will remain open after tripping.</param>
    public AdvancedCircuitBreaker(double failureThreshold, TimeSpan samplingDuration, int minimumThroughput, TimeSpan durationOfBreak)
    {
        if (failureThreshold <= 0 || failureThreshold > 1)
            throw new ArgumentOutOfRangeException(nameof(failureThreshold), "Failure threshold must be in (0,1].");
        if (minimumThroughput < 1)
            throw new ArgumentOutOfRangeException(nameof(minimumThroughput), "Minimum throughput must be >= 1.");

        _failureThreshold = failureThreshold;
        _minimumThroughput = minimumThroughput;
        _samplingDurationTicks = samplingDuration.Ticks;
        _durationOfBreakTicks = durationOfBreak.Ticks;
        _windowStartTicks = DateTimeOffset.UtcNow.Ticks;
        _state = CircuitStateClosed;
    }

    public CircuitBreakerState State => (CircuitBreakerState)Volatile.Read(ref _state);

    public int CurrentFailureCount => Volatile.Read(ref _failureCount);

    public bool TryExecute(out bool isStateChanged)
    {
        isStateChanged = false;
        if (_durationOfBreakTicks == 0)
            return true;
        var state = (CircuitBreakerState)Volatile.Read(ref _state);
        if (state == CircuitBreakerState.Closed)
        {
            return true;
        }
        if (state == CircuitBreakerState.Open)
        {
            var blockedUntil = Volatile.Read(ref _blockedUntilTicks);
            if (DateTimeOffset.UtcNow.Ticks >= blockedUntil)
            {
                var prior = Interlocked.CompareExchange(ref _state, CircuitStateHalfOpen, CircuitStateOpen);
                if (prior == CircuitStateOpen)
                {
                    isStateChanged = true;
                    Interlocked.Exchange(ref _halfOpenAttempted, 0);
                }
                state = CircuitBreakerState.HalfOpen;
            }
            else
            {
                return false;
            }
        }
        if (state == CircuitBreakerState.HalfOpen)
        {
            if (Interlocked.CompareExchange(ref _halfOpenAttempted, 1, 0) == 0)
            {
                return true;
            }
            return false;
        }
        return true;
    }

    public void RecordSuccess(out bool isStateChanged)
    {
        isStateChanged = false;
        if (_durationOfBreakTicks == 0)
            return;
        var state = (CircuitBreakerState)Volatile.Read(ref _state);
        if (state == CircuitBreakerState.Closed)
        {
            lock (_lock)
            {
                RefreshWindowIfExpired();
                _callCount++;
            }
            return;
        }
        if (state == CircuitBreakerState.HalfOpen)
        {
            var prior = Interlocked.Exchange(ref _state, CircuitStateClosed);
            isStateChanged = prior != CircuitStateClosed;
            lock (_lock)
            {
                // reset sampling window on success after half-open
                _windowStartTicks = DateTimeOffset.UtcNow.Ticks;
                _callCount = 0;
                _failureCount = 0;
            }
        }
    }

    public void RecordFailure(out bool isStateChanged)
    {
        isStateChanged = false;
        if (_durationOfBreakTicks == 0)
            return;
        var state = (CircuitBreakerState)Volatile.Read(ref _state);
        if (state == CircuitBreakerState.Closed)
        {
            lock (_lock)
            {
                RefreshWindowIfExpired();
                _callCount++;
                _failureCount++;
                if (_callCount >= _minimumThroughput)
                {
                    var failureRate = (double)_failureCount / _callCount;
                    if (failureRate >= _failureThreshold)
                    {
                        var prior = Interlocked.Exchange(ref _state, CircuitStateOpen);
                        isStateChanged = prior != CircuitStateOpen;
                        Interlocked.Exchange(ref _blockedUntilTicks, DateTimeOffset.UtcNow.Ticks + _durationOfBreakTicks);
                    }
                }
            }
        }
        else if (state == CircuitBreakerState.HalfOpen)
        {
            var prior = Interlocked.Exchange(ref _state, CircuitStateOpen);
            isStateChanged = prior != CircuitStateOpen;
            Interlocked.Exchange(ref _blockedUntilTicks, DateTimeOffset.UtcNow.Ticks + _durationOfBreakTicks);
            lock (_lock)
            {
                _callCount = 0;
                _failureCount = 0;
                _windowStartTicks = DateTimeOffset.UtcNow.Ticks;
            }
        }
    }

    public void Close(out bool isStateChanged)
    {
        var prior = Interlocked.Exchange(ref _state, CircuitStateClosed);
        isStateChanged = prior != CircuitStateClosed;
        lock (_lock)
        {
            _callCount = 0;
            _failureCount = 0;
            _windowStartTicks = DateTimeOffset.UtcNow.Ticks;
        }
        Interlocked.Exchange(ref _blockedUntilTicks, DateTimeOffset.MinValue.Ticks);
        Interlocked.Exchange(ref _halfOpenAttempted, 0);
    }

    private void RefreshWindowIfExpired()
    {
        var nowTicks = DateTimeOffset.UtcNow.Ticks;
        if (_samplingDurationTicks <= 0)
        {
            // treat as infinite sampling period, do nothing
            return;
        }
        var windowStart = _windowStartTicks;
        if (nowTicks - windowStart >= _samplingDurationTicks)
        {
            _windowStartTicks = nowTicks;
            _callCount = 0;
            _failureCount = 0;
        }
    }
}
