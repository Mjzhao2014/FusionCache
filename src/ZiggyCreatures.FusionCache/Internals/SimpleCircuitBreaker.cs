namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// A simple circuit breaker that tracks consecutive failures and opens the circuit after a configurable number of failures.
/// </summary>
internal sealed class SimpleCircuitBreaker
    : IFusionCacheCircuitBreaker
{
   private const int CircuitStateClosed = (int)CircuitBreakerState.Closed;
   private const int CircuitStateOpen = (int)CircuitBreakerState.Open;
   private const int CircuitStateHalfOpen = (int)CircuitBreakerState.HalfOpen;

   private readonly int _failuresAllowedBeforeBreaking;
   private readonly long _breakDurationTicks;
   private int _state;
   private int _failureCount;
   private long _blockedUntilTicks;
   private int _halfOpenAttempted;

   /// <summary>
   /// Creates a new <see cref="SimpleCircuitBreaker"/> instance.
   /// </summary>
   /// <param name="failuresAllowedBeforeBreaking">The number of consecutive failures that are allowed before opening the circuit. A value of 1 means the circuit will open after the first failure.</param>
   /// <param name="durationOfBreak">The amount of time the circuit will remain open after tripping.</param>
   public SimpleCircuitBreaker(int failuresAllowedBeforeBreaking, TimeSpan durationOfBreak)
   {
       _failuresAllowedBeforeBreaking = failuresAllowedBeforeBreaking <= 0 ? 1 : failuresAllowedBeforeBreaking;
       _breakDurationTicks = durationOfBreak.Ticks;
       _blockedUntilTicks = DateTimeOffset.MinValue.Ticks;
       _state = CircuitStateClosed;
   }

   public CircuitBreakerState State => (CircuitBreakerState)Volatile.Read(ref _state);

   public int CurrentFailureCount => Volatile.Read(ref _failureCount);

   public bool TryExecute(out bool isStateChanged)
   {
       isStateChanged = false;
       // circuit breaker disabled
       if (_breakDurationTicks == 0)
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
           // allow exactly one call through
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
       if (_breakDurationTicks == 0)
           return;
       var state = (CircuitBreakerState)Volatile.Read(ref _state);
       if (state == CircuitBreakerState.Closed)
       {
           Interlocked.Exchange(ref _failureCount, 0);
           return;
       }
       if (state == CircuitBreakerState.HalfOpen)
       {
           var prior = Interlocked.Exchange(ref _state, CircuitStateClosed);
           isStateChanged = prior != CircuitStateClosed;
           Interlocked.Exchange(ref _failureCount, 0);
       }
   }

   public void RecordFailure(out bool isStateChanged)
   {
       isStateChanged = false;
       if (_breakDurationTicks == 0)
           return;
       var state = (CircuitBreakerState)Volatile.Read(ref _state);
       if (state == CircuitBreakerState.Closed)
       {
           var count = Interlocked.Increment(ref _failureCount);
           if (count >= _failuresAllowedBeforeBreaking)
           {
               var prior = Interlocked.Exchange(ref _state, CircuitStateOpen);
               isStateChanged = prior != CircuitStateOpen;
               Interlocked.Exchange(ref _blockedUntilTicks, DateTimeOffset.UtcNow.Ticks + _breakDurationTicks);
           }
       }
       else if (state == CircuitBreakerState.HalfOpen)
       {
           var prior = Interlocked.Exchange(ref _state, CircuitStateOpen);
           isStateChanged = prior != CircuitStateOpen;
           Interlocked.Exchange(ref _blockedUntilTicks, DateTimeOffset.UtcNow.Ticks + _breakDurationTicks);
           Interlocked.Exchange(ref _failureCount, 0);
       }
   }

   public void Close(out bool isStateChanged)
   {
       isStateChanged = false;
       var prior = Interlocked.Exchange(ref _state, CircuitStateClosed);
       isStateChanged = prior != CircuitStateClosed;
       Interlocked.Exchange(ref _failureCount, 0);
       Interlocked.Exchange(ref _blockedUntilTicks, DateTimeOffset.MinValue.Ticks);
       Interlocked.Exchange(ref _halfOpenAttempted, 0);
   }
}
