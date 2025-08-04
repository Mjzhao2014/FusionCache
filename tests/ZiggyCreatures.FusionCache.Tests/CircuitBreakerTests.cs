using System;
using System.Threading;
using Xunit;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace FusionCacheTests
{
	public class CircuitBreakerTests
	{
		[Fact]
		public void SimpleBreaker_Opens_After_AllowedFailures()
		{
			var breaker = new SimpleCircuitBreaker(TimeSpan.FromMilliseconds(200), allowedFailuresBeforeBreaking: 3);
			bool stateChanged;
			// simulate three consecutive failures
			Assert.True(breaker.TryExecute(out stateChanged));
			breaker.RecordFailure(out stateChanged);
			Assert.Equal(1, breaker.CurrentFailureCount);
			Assert.True(breaker.TryExecute(out stateChanged));
			breaker.RecordFailure(out stateChanged);
			Assert.Equal(2, breaker.CurrentFailureCount);
			Assert.True(breaker.TryExecute(out stateChanged));
			breaker.RecordFailure(out stateChanged);
			Assert.Equal(CircuitBreakerState.Open, breaker.State);
			Assert.False(breaker.TryExecute(out stateChanged));
			// wait for break duration
			Thread.Sleep(250);
			// circuit should now allow one half-open call
			Assert.True(breaker.TryExecute(out stateChanged));
			Assert.Equal(CircuitBreakerState.HalfOpen, breaker.State);
			// simulate a success to close the circuit
			breaker.RecordSuccess(out stateChanged);
			Assert.Equal(CircuitBreakerState.Closed, breaker.State);
		}

		[Fact]
		public void AdvancedBreaker_Opens_When_FailureRate_Exceeds_Threshold()
		{
			var options = new AdvancedCircuitBreakerOptions
			{
				FailureThreshold = 50, // percent
				MinimumThroughput = 4,
				SamplingDuration = TimeSpan.FromSeconds(1),
				DurationOfBreak = TimeSpan.FromMilliseconds(200)
			};
			var breaker = new AdvancedCircuitBreaker(options);
			bool tmp;
			// call sequence: F, F, S, F => total=4, failures=3 => 75% >50 => open
			Assert.True(breaker.TryExecute(out tmp));
			breaker.RecordFailure(out tmp);
			Assert.True(breaker.TryExecute(out tmp));
			breaker.RecordFailure(out tmp);
			Assert.True(breaker.TryExecute(out tmp));
			breaker.RecordSuccess(out tmp);
			Assert.True(breaker.TryExecute(out tmp));
			breaker.RecordFailure(out tmp);
			Assert.Equal(CircuitBreakerState.Open, breaker.State);
			Assert.False(breaker.TryExecute(out tmp));
			// wait for break duration
			Thread.Sleep(250);
			// now half-open
			Assert.True(breaker.TryExecute(out tmp));
			Assert.Equal(CircuitBreakerState.HalfOpen, breaker.State);
			// success closes the circuit
			breaker.RecordSuccess(out tmp);
			Assert.Equal(CircuitBreakerState.Closed, breaker.State);
		}
	}
}
