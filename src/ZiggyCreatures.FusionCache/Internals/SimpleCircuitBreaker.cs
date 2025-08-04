using System.Threading;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// A simple, reusable circuit-breaker.
/// </summary>
internal sealed class SimpleCircuitBreaker
   : IFusionCacheCircuitBreaker
{
   private readonly int _allowedFailuresBeforeBreaking;
   private readonly long _breakDurationTicks;
   private int _failureCount;
   private long _openedTicks;
   private int _halfOpenGate;
   private int _state;

   /// <summary>
   /// Creates a new <see cref="SimpleCircuitBreaker"/> instance.
   /// </summary>
   /// <param name="breakDuration">The amount of time the circuit will remain open once opened.</param>
   /// <param name="allowedFailuresBeforeBreaking">The number of consecutive failures allowed before opening the circuit. Default is 1 (open on first failure).</param>
   public SimpleCircuitBreaker(TimeSpan breakDuration, int allowedFailuresBeforeBreaking = 1)
   {
       BreakDuration = breakDuration;
       _breakDurationTicks = BreakDuration.Ticks;
       _allowedFailuresBeforeBreaking = allowedFailuresBeforeBreaking < 1 ? 1 : allowedFailuresBeforeBreaking;
       _openedTicks = DateTimeOffset.MinValue.Ticks;
       _halfOpenGate = 0;
       _state = (int)CircuitBreakerState.Closed;
       _failureCount = 0;
   }

   /// <summary>
   /// The amount of time the circuit will remain open when opened.
   /// </summary>
   public TimeSpan BreakDuration { get; }

   /// <inheritdoc/>
   public CircuitBreakerState State => (CircuitBreakerState)_state;

   /// <inheritdoc/>
   public int CurrentFailureCount => Volatile.Read(ref _failureCount);

   /// <inheritdoc/>
   public bool TryExecute(out bool isStateChanged)
   {
       isStateChanged = false;
       // If breaker disabled
       if (_breakDurationTicks == 0)
           return true;

       var nowTicks = DateTimeOffset.UtcNow.Ticks;
       var state = (CircuitBreakerState)_state;
       if (state == CircuitBreakerState.Open)
       {
           // If break duration has passed, transition to half-open
           if (nowTicks >= Volatile.Read(ref _openedTicks) + _breakDurationTicks)
           {
               var oldState = Interlocked.CompareExchange(ref _state, (int)CircuitBreakerState.HalfOpen, (int)CircuitBreakerState.Open);
               if (oldState == (int)CircuitBreakerState.Open)
               {
                   isStateChanged = true;
                   // reset half-open gate
                   Interlocked.Exchange(ref _halfOpenGate, 0);
               }
               state = (CircuitBreakerState)_state;
           }
           else
           {
               return false;
           }
       }
       if (state == CircuitBreakerState.HalfOpen)
       {
           // allow exactly one caller through
           if (Interlocked.CompareExchange(ref _halfOpenGate, 1, 0) == 0)
           {
               return true;
           }
           else
           {
               return false;
           }
       }
       // closed
       return true;
   }

   /// <inheritdoc/>
   public void RecordSuccess(out bool isStateChanged)
   {
       isStateChanged = false;
       if (_breakDurationTicks == 0)
       {
           return;
       }
       // reset consecutive failures
       Interlocked.Exchange(ref _failureCount, 0);
       if ((CircuitBreakerState)_state == CircuitBreakerState.HalfOpen)
       {
           var old = Interlocked.CompareExchange(ref _state, (int)CircuitBreakerState.Closed, (int)CircuitBreakerState.HalfOpen);
           isStateChanged = old == (int)CircuitBreakerState.HalfOpen;
           // allow future half-open test calls
           Interlocked.Exchange(ref _halfOpenGate, 0);
       }
   }

   /// <inheritdoc/>
   public void RecordFailure(out bool isStateChanged)
   {
       isStateChanged = false;
       if (_breakDurationTicks == 0)
       {
           return;
       }
       var nowTicks = DateTimeOffset.UtcNow.Ticks;
       var state = (CircuitBreakerState)_state;
       if (state == CircuitBreakerState.HalfOpen)
       {
           // test call failed – reopen
           var old = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Open);
           isStateChanged = old != (int)CircuitBreakerState.Open;
           Interlocked.Exchange(ref _openedTicks, nowTicks);
           // reset failure count and gate
           Interlocked.Exchange(ref _failureCount, 0);
           Interlocked.Exchange(ref _halfOpenGate, 0);
           return;
       }
       var newCount = Interlocked.Increment(ref _failureCount);
       if (newCount >= _allowedFailuresBeforeBreaking)
       {
           var old = Interlocked.CompareExchange(ref _state, (int)CircuitBreakerState.Open, (int)CircuitBreakerState.Closed);
           if (old == (int)CircuitBreakerState.Closed)
           {
               isStateChanged = true;
               Interlocked.Exchange(ref _openedTicks, nowTicks);
               // reset half-open gate
               Interlocked.Exchange(ref _halfOpenGate, 0);
           }
       }
   }

   /// <inheritdoc/>
   public void Close(out bool isStateChanged)
   {
       isStateChanged = false;
       Interlocked.Exchange(ref _failureCount, 0);
       Interlocked.Exchange(ref _openedTicks, DateTimeOffset.MinValue.Ticks);
       Interlocked.Exchange(ref _halfOpenGate, 0);
       var old = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
       isStateChanged = old != (int)CircuitBreakerState.Closed;
   }
}
