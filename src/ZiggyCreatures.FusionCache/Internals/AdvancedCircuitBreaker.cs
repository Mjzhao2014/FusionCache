using System;
using System.Collections.Generic;
using System.Threading;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// An advanced circuit breaker implementation that tracks failure rate within a sampling window.
/// When failures exceed a configured error rate threshold beyond a minimum throughput, the circuit will open.
/// After a break duration, the circuit will transition to half-open, allowing a single test call to decide if the circuit can close again.
/// </summary>
internal sealed class AdvancedCircuitBreaker
   : IFusionCacheCircuitBreaker
{
   private readonly AdvancedCircuitBreakerOptions _options;
   private readonly object _lock = new();
   private readonly Queue<long> _callTimestamps = new();
   private readonly Queue<long> _failureTimestamps = new();
   private long _openedTicks;
   private int _state;
   private int _halfOpenGate;
   private int _currentFailureCount;

   public AdvancedCircuitBreaker(AdvancedCircuitBreakerOptions options)
   {
       if (options is null)
           throw new ArgumentNullException(nameof(options));
       _options = options;
       _state = (int)CircuitBreakerState.Closed;
       _openedTicks = DateTimeOffset.MinValue.Ticks;
       _halfOpenGate = 0;
       _currentFailureCount = 0;
   }

   /// <inheritdoc/>
   public CircuitBreakerState State => (CircuitBreakerState)_state;

   /// <inheritdoc/>
   public int CurrentFailureCount => Volatile.Read(ref _currentFailureCount);

   private void PurgeOld(long nowTicks)
   {
       var windowStart = nowTicks - _options.SamplingDuration.Ticks;
       // remove old call timestamps
       while (_callTimestamps.Count > 0 && _callTimestamps.Peek() <= windowStart)
       {
           _callTimestamps.Dequeue();
       }
       // remove old failure timestamps
       while (_failureTimestamps.Count > 0 && _failureTimestamps.Peek() <= windowStart)
       {
           _failureTimestamps.Dequeue();
       }
       // update current failure count
       _currentFailureCount = _failureTimestamps.Count;
   }

   /// <inheritdoc/>
   public bool TryExecute(out bool isStateChanged)
   {
       isStateChanged = false;
       if (_options.DurationOfBreak.Ticks == 0)
           return true;
       var nowTicks = DateTimeOffset.UtcNow.Ticks;
       var state = (CircuitBreakerState)_state;
       if (state == CircuitBreakerState.Open)
       {
           // check if break duration elapsed
           if (nowTicks >= Volatile.Read(ref _openedTicks) + _options.DurationOfBreak.Ticks)
           {
               var old = Interlocked.CompareExchange(ref _state, (int)CircuitBreakerState.HalfOpen, (int)CircuitBreakerState.Open);
               if (old == (int)CircuitBreakerState.Open)
               {
                   isStateChanged = true;
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
           if (Interlocked.CompareExchange(ref _halfOpenGate, 1, 0) == 0)
           {
               return true;
           }
           return false;
       }
       // closed: update counts window
       lock (_lock)
       {
           PurgeOld(nowTicks);
       }
       return true;
   }

   /// <inheritdoc/>
   public void RecordSuccess(out bool isStateChanged)
   {
       isStateChanged = false;
       if (_options.DurationOfBreak.Ticks == 0)
           return;
       var nowTicks = DateTimeOffset.UtcNow.Ticks;
       if ((CircuitBreakerState)_state == CircuitBreakerState.HalfOpen)
       {
           // test call succeeded => close
           var old = Interlocked.CompareExchange(ref _state, (int)CircuitBreakerState.Closed, (int)CircuitBreakerState.HalfOpen);
           isStateChanged = old == (int)CircuitBreakerState.HalfOpen;
           Interlocked.Exchange(ref _halfOpenGate, 0);
           lock (_lock)
           {
               _callTimestamps.Clear();
               _failureTimestamps.Clear();
               _currentFailureCount = 0;
           }
       }
       else
       {
           // closed => record success in sampling
           lock (_lock)
           {
               PurgeOld(nowTicks);
               _callTimestamps.Enqueue(nowTicks);
               // no failure increment
           }
       }
   }

   /// <inheritdoc/>
   public void RecordFailure(out bool isStateChanged)
   {
       isStateChanged = false;
       if (_options.DurationOfBreak.Ticks == 0)
           return;
       var nowTicks = DateTimeOffset.UtcNow.Ticks;
       var state = (CircuitBreakerState)_state;
       if (state == CircuitBreakerState.HalfOpen)
       {
           // test call failed => reopen
           var old = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Open);
           isStateChanged = old != (int)CircuitBreakerState.Open;
           Interlocked.Exchange(ref _openedTicks, nowTicks);
           // reset window
           lock (_lock)
           {
               _callTimestamps.Clear();
               _failureTimestamps.Clear();
               _currentFailureCount = 0;
           }
           Interlocked.Exchange(ref _halfOpenGate, 0);
           return;
       }
       lock (_lock)
       {
           PurgeOld(nowTicks);
           _callTimestamps.Enqueue(nowTicks);
           _failureTimestamps.Enqueue(nowTicks);
           _currentFailureCount = _failureTimestamps.Count;
           var total = _callTimestamps.Count;
           if (total >= _options.MinimumThroughput)
           {
               var failureRatePercent = (_currentFailureCount / (double)total) * 100.0;
               if (failureRatePercent >= _options.FailureThreshold)
               {
                   // open circuit
                   var old = Interlocked.CompareExchange(ref _state, (int)CircuitBreakerState.Open, (int)CircuitBreakerState.Closed);
                   // if we were still closed (prevent open from closed)
                   if (old == (int)CircuitBreakerState.Closed)
                   {
                       isStateChanged = true;
                       Interlocked.Exchange(ref _openedTicks, nowTicks);
                       Interlocked.Exchange(ref _halfOpenGate, 0);
                   }
               }
           }
       }
   }

   /// <inheritdoc/>
   public void Close(out bool isStateChanged)
   {
       isStateChanged = false;
       Interlocked.Exchange(ref _halfOpenGate, 0);
       lock (_lock)
       {
           _callTimestamps.Clear();
           _failureTimestamps.Clear();
           _currentFailureCount = 0;
       }
       var old = Interlocked.Exchange(ref _state, (int)CircuitBreakerState.Closed);
       isStateChanged = old != (int)CircuitBreakerState.Closed;
   }
}
