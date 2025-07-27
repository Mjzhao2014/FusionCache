namespace ZiggyCreatures.Caching.Fusion.Internals;

internal sealed class AdvancedCircuitBreaker : ICircuitBreaker
{
    private const int CircuitStateClosed = 0;
    private const int CircuitStateOpen = 1;
    private const int CircuitStateHalfOpen = 2;

    private int _circuitState;
    private long _gatewayTicks;
    private readonly long _breakDurationTicks;
    private readonly long _samplingDurationTicks;
    private readonly double _failureThreshold;

    private int _failureCount;
    private int _successCount;
    private long _windowStartTicks;

    public AdvancedCircuitBreaker(double failureThreshold, TimeSpan samplingDuration, TimeSpan breakDuration)
    {
        if (failureThreshold <= 0 || failureThreshold > 1)
            throw new ArgumentOutOfRangeException(nameof(failureThreshold));
        if (samplingDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(samplingDuration));

        _failureThreshold = failureThreshold;
        _samplingDurationTicks = samplingDuration.Ticks;
        BreakDuration = breakDuration;
        _breakDurationTicks = BreakDuration.Ticks;
        _gatewayTicks = DateTimeOffset.MinValue.Ticks;
        _windowStartTicks = DateTimeOffset.UtcNow.Ticks;
    }

    public TimeSpan BreakDuration { get; private set; }

    private void EnsureWindow()
    {
        var now = DateTimeOffset.UtcNow.Ticks;
        if (now - Interlocked.Read(ref _windowStartTicks) > _samplingDurationTicks)
        {
            Interlocked.Exchange(ref _failureCount, 0);
            Interlocked.Exchange(ref _successCount, 0);
            Interlocked.Exchange(ref _windowStartTicks, now);
        }
    }

    private double FailureRatio()
    {
        var failures = Volatile.Read(ref _failureCount);
        var successes = Volatile.Read(ref _successCount);
        var total = failures + successes;
        if (total == 0)
            return 0;
        return (double)failures / total;
    }

    public bool TryOpen(out bool isStateChanged)
    {
        if (_breakDurationTicks == 0)
        {
            isStateChanged = false;
            return false;
        }

        EnsureWindow();
        Interlocked.Increment(ref _failureCount);

        if (_circuitState == CircuitStateHalfOpen || FailureRatio() >= _failureThreshold)
        {
            Interlocked.Exchange(ref _gatewayTicks, DateTimeOffset.UtcNow.Ticks + _breakDurationTicks);
            var oldState = Interlocked.Exchange(ref _circuitState, CircuitStateOpen);
            isStateChanged = oldState != CircuitStateOpen;
            Interlocked.Exchange(ref _failureCount, 0);
            Interlocked.Exchange(ref _successCount, 0);
            Interlocked.Exchange(ref _windowStartTicks, DateTimeOffset.UtcNow.Ticks);
            return true;
        }

        isStateChanged = false;
        return _circuitState == CircuitStateOpen;
    }

    public void Close(out bool isStateChanged)
    {
        EnsureWindow();
        Interlocked.Increment(ref _successCount);

        var oldState = Interlocked.Exchange(ref _circuitState, CircuitStateClosed);
        isStateChanged = oldState != CircuitStateClosed;
        if (isStateChanged)
        {
            Interlocked.Exchange(ref _gatewayTicks, DateTimeOffset.MinValue.Ticks);
            Interlocked.Exchange(ref _failureCount, 0);
            Interlocked.Exchange(ref _successCount, 0);
            Interlocked.Exchange(ref _windowStartTicks, DateTimeOffset.UtcNow.Ticks);
        }
    }

    public bool IsClosed(out bool isStateChanged)
    {
        isStateChanged = false;

        if (_breakDurationTicks == 0)
            return true;

        if (_circuitState == CircuitStateOpen)
        {
            var now = DateTimeOffset.UtcNow.Ticks;
            if (now >= Volatile.Read(ref _gatewayTicks))
            {
                Interlocked.Exchange(ref _circuitState, CircuitStateHalfOpen);
                return true;
            }
            return false;
        }

        return true;
    }
}
