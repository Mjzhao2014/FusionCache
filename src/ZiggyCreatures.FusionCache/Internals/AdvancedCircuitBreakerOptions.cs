using System;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Options for configuring an advanced circuit breaker.
/// </summary>
internal sealed class AdvancedCircuitBreakerOptions
{
   /// <summary>
   /// The failure rate threshold, as a percentage between 0 and 100 (exclusive). When the percentage of failed calls
   /// in the sampling window equals or exceeds this value, and the total number of calls exceeds <see cref="MinimumThroughput"/>,
   /// the circuit will open.
   /// </summary>
   public int FailureThreshold { get; set; }

   /// <summary>
   /// The duration of the time window over which call outcomes are sampled to compute the current failure rate.
   /// </summary>
   public TimeSpan SamplingDuration { get; set; }

   /// <summary>
   /// Once opened, the circuit will remain open for this duration before transitioning to half-open.
   /// </summary>
   public TimeSpan DurationOfBreak { get; set; }

   /// <summary>
   /// The minimum total number of calls that must be made within <see cref="SamplingDuration"/>
   /// before the failure rate is evaluated against <see cref="FailureThreshold"/>.
   /// </summary>
   public int MinimumThroughput { get; set; }
}
