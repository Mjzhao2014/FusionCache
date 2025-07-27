namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// A simple, reusable circuit-breaker.
/// </summary>
internal sealed class SimpleCircuitBreaker
{
        private const int CircuitStateClosed = 0;
        private const int CircuitStateOpen = 1;
        private const int CircuitStateHalfOpen = 2;

        private int _circuitState;
        private long _gatewayTicks;
        private readonly long _breakDurationTicks;

        private readonly double _failureThreshold;
        private readonly long _samplingDurationTicks;
        private readonly int _minimumThroughput;
        private long _windowStartTicks;
        private int _failureCount;
        private int _successCount;
        private int _halfOpenInUse;

	/// <summary>
	/// Creates a new <see cref="SimpleCircuitBreaker"/> instance.
	/// </summary>
        /// <param name="breakDuration">The amount of time the circuit will remain open, when told to.</param>
        /// <param name="failureThreshold">The failure threshold for the advanced mode.</param>
        /// <param name="samplingDuration">The sampling duration for the advanced mode.</param>
        /// <param name="minimumThroughput">The minimum throughput for the advanced mode.</param>
        public SimpleCircuitBreaker(TimeSpan breakDuration, double failureThreshold = 0d, TimeSpan? samplingDuration = null, int minimumThroughput = 0)
        {
                BreakDuration = breakDuration;
                _breakDurationTicks = BreakDuration.Ticks;
                _gatewayTicks = DateTimeOffset.MinValue.Ticks;

                _failureThreshold = failureThreshold;
                _samplingDurationTicks = samplingDuration?.Ticks ?? 0;
                _minimumThroughput = minimumThroughput;
                _windowStartTicks = DateTimeOffset.UtcNow.Ticks;
        }

	/// <summary>
	/// The amount of time the circuit will remain open, when told to.
	/// </summary>
	public TimeSpan BreakDuration { get; private set; }

	/// <summary>
	/// Tries to open the circuit.
	/// </summary>
	/// <param name="isStateChanged">Indicates if the circuit has been opened with this operation.</param>
	/// <returns><see langword="true"/> if the circuit is open, either because it was already or because it has been opened with this operation. <see langword="false"/> otherwise.</returns>
        public bool TryOpen(out bool isStateChanged)
        {
                // NO CIRCUIT-BREAKER DURATION
                if (_breakDurationTicks == 0)
                {
                        isStateChanged = false;
                        return false;
                }

                if (_samplingDurationTicks > 0 && _minimumThroughput > 0 && _failureThreshold > 0)
                {
                        var now = DateTimeOffset.UtcNow.Ticks;
                        UpdateWindow(now);
                        Interlocked.Increment(ref _failureCount);

                        // HALF-OPEN -> FAILURE
                        if (_circuitState == CircuitStateHalfOpen)
                        {
                                Interlocked.Exchange(ref _gatewayTicks, now + _breakDurationTicks);
                                var oldState = Interlocked.Exchange(ref _circuitState, CircuitStateOpen);
                                Interlocked.Exchange(ref _halfOpenInUse, 0);
                                ResetCounts(now);
                                isStateChanged = oldState != CircuitStateOpen;
                                return true;
                        }

                        var failures = Volatile.Read(ref _failureCount);
                        var successes = Volatile.Read(ref _successCount);
                        var total = failures + successes;

                        if (total >= _minimumThroughput && (double)failures / total >= _failureThreshold)
                        {
                                Interlocked.Exchange(ref _gatewayTicks, now + _breakDurationTicks);
                                var oldState = Interlocked.Exchange(ref _circuitState, CircuitStateOpen);
                                ResetCounts(now);
                                isStateChanged = oldState != CircuitStateOpen;
                                return true;
                        }

                        isStateChanged = false;
                        return _circuitState == CircuitStateOpen;
                }

                Interlocked.Exchange(ref _gatewayTicks, DateTimeOffset.UtcNow.Ticks + _breakDurationTicks);

		// DETECT CIRCUIT STATE CHANGE
		var oldCircuitState = Interlocked.Exchange(ref _circuitState, CircuitStateOpen);

		isStateChanged = oldCircuitState == CircuitStateClosed;
		return true;
	}

	/// <summary>
	/// Close the circuit.
	/// </summary>
	/// <param name="isStateChanged">Indicates if the circuit has been closed with this operation.</param>
        public void Close(out bool isStateChanged)
        {
                Interlocked.Exchange(ref _gatewayTicks, DateTimeOffset.MinValue.Ticks);

                // DETECT CIRCUIT STATE CHANGE
                var oldCircuitState = Interlocked.Exchange(ref _circuitState, CircuitStateClosed);
                ResetCounts(DateTimeOffset.UtcNow.Ticks);
                
                isStateChanged = oldCircuitState == CircuitStateOpen;
        }

	/// <summary>
	/// Check if the circuit is closed, or has been closed with this operation.
	/// </summary>
	/// <param name="isStateChanged">Indicates if the circuit has been closed with this operation.</param>
	/// <returns><see langword="true"/> if the circuit is closed, either because it was already closed or because it has been closed with this operation. <see langword="false"/> otherwise.</returns>
        public bool IsClosed(out bool isStateChanged)
        {
                isStateChanged = false;

                // NO CIRCUIT-BREAKER DURATION
                if (_breakDurationTicks == 0)
                        return true;

                var now = DateTimeOffset.UtcNow.Ticks;
                long gatewayTicksLocal = Interlocked.Read(ref _gatewayTicks);

                if (now < gatewayTicksLocal)
                        return false;

                if (_circuitState == CircuitStateOpen)
                {
                        if (_samplingDurationTicks > 0 && _minimumThroughput > 0 && _failureThreshold > 0)
                        {
                                var old = Interlocked.Exchange(ref _circuitState, CircuitStateHalfOpen);
                                isStateChanged = old != CircuitStateHalfOpen;
                                return Interlocked.CompareExchange(ref _halfOpenInUse, 1, 0) == 0;
                        }

                        var oldCircuitState = Interlocked.Exchange(ref _circuitState, CircuitStateClosed);
                        isStateChanged = oldCircuitState == CircuitStateOpen;
                        return true;
                }

                if (_circuitState == CircuitStateHalfOpen)
                {
                        return Interlocked.CompareExchange(ref _halfOpenInUse, 1, 0) == 0;
                }

                if (_samplingDurationTicks > 0 && _minimumThroughput > 0 && _failureThreshold > 0)
                        UpdateWindow(now);

                return true;
        }

        public void OnSuccess(out bool isStateChanged)
        {
                isStateChanged = false;

                if (_samplingDurationTicks > 0 && _minimumThroughput > 0 && _failureThreshold > 0)
                {
                        var now = DateTimeOffset.UtcNow.Ticks;
                        UpdateWindow(now);
                        Interlocked.Increment(ref _successCount);

                        if (_circuitState == CircuitStateHalfOpen)
                        {
                                var old = Interlocked.Exchange(ref _circuitState, CircuitStateClosed);
                                Interlocked.Exchange(ref _halfOpenInUse, 0);
                                ResetCounts(now);
                                isStateChanged = old != CircuitStateClosed;
                        }
                }
        }

        private void UpdateWindow(long now)
        {
                if (_samplingDurationTicks <= 0)
                        return;

                var start = Volatile.Read(ref _windowStartTicks);
                if (now - start > _samplingDurationTicks)
                {
                        ResetCounts(now);
                }
        }

        private void ResetCounts(long now)
        {
                Interlocked.Exchange(ref _windowStartTicks, now);
                Interlocked.Exchange(ref _failureCount, 0);
                Interlocked.Exchange(ref _successCount, 0);
        }
}
