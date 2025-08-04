using Microsoft.Extensions.Caching.Memory;
using Xunit;
using Xunit.Abstractions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Internals;
using FusionCacheTests.Stuff;

namespace FusionCacheTests;

public class CircuitBreakerTests : AbstractTests
{
	public CircuitBreakerTests(ITestOutputHelper output)
		: base(output, null)
	{
	}

	protected string CreateRandomCacheKey(string prefix = "test")
	{
		return $"{prefix}:{Guid.NewGuid():N}";
	}

	[Fact]
	public void SimpleCircuitBreaker_SupportsFailuresAllowedBeforeBreaking()
	{
		// Test that SimpleCircuitBreaker supports configurable failure threshold
		var failuresAllowedBeforeBreaking = 3;
		var durationOfBreak = TimeSpan.FromMilliseconds(500);
		
		var circuitBreaker = new SimpleCircuitBreaker(failuresAllowedBeforeBreaking, durationOfBreak);
		
		Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
		Assert.Equal(0, circuitBreaker.CurrentFailureCount);
		
		// Record 2 failures - should stay closed
		circuitBreaker.RecordFailure(out bool isStateChanged1);
		Assert.False(isStateChanged1);
		Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
		Assert.Equal(1, circuitBreaker.CurrentFailureCount);
		
		circuitBreaker.RecordFailure(out bool isStateChanged2);
		Assert.False(isStateChanged2);
		Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
		Assert.Equal(2, circuitBreaker.CurrentFailureCount);
		
		// Third failure should open the circuit
		circuitBreaker.RecordFailure(out bool isStateChanged3);
		Assert.True(isStateChanged3);
		Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
		Assert.Equal(0, circuitBreaker.CurrentFailureCount); // Reset after opening
	}

	[Fact]
	public void SimpleCircuitBreaker_ConsecutiveFailuresLogicWorks()
	{
		// Test that consecutive failures are required to open the circuit
		var failuresAllowedBeforeBreaking = 2;
		var durationOfBreak = TimeSpan.FromMilliseconds(500);
		
		var circuitBreaker = new SimpleCircuitBreaker(failuresAllowedBeforeBreaking, durationOfBreak);
		
		// Failure 1
		circuitBreaker.RecordFailure(out bool isStateChanged1);
		Assert.False(isStateChanged1);
		Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
		Assert.Equal(1, circuitBreaker.CurrentFailureCount);
		
		// Success resets counter
		circuitBreaker.RecordSuccess(out bool isStateChanged2);
		Assert.False(isStateChanged2);
		Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
		Assert.Equal(0, circuitBreaker.CurrentFailureCount);
		
		// Need 2 consecutive failures to open
		circuitBreaker.RecordFailure(out bool isStateChanged3);
		Assert.False(isStateChanged3);
		Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
		
		circuitBreaker.RecordFailure(out bool isStateChanged4);
		Assert.True(isStateChanged4);
		Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
	}

	[Fact]
	public void SimpleCircuitBreaker_HalfOpenStateWorksCorrectly()
	{
		// Test half-open state allows exactly one call
		var failuresAllowedBeforeBreaking = 1;
		var durationOfBreak = TimeSpan.FromMilliseconds(100);
		
		var circuitBreaker = new SimpleCircuitBreaker(failuresAllowedBeforeBreaking, durationOfBreak);
		
		// Open the circuit
		circuitBreaker.RecordFailure(out _);
		Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
		
		// Should not allow execution while open
		Assert.False(circuitBreaker.TryExecute(out _));
		
		// Wait for break duration to pass
		Thread.Sleep(durationOfBreak.PlusALittleBit());
		
		// First call should transition to half-open and be allowed
		Assert.True(circuitBreaker.TryExecute(out bool isStateChanged1));
		Assert.True(isStateChanged1);
		Assert.Equal(CircuitBreakerState.HalfOpen, circuitBreaker.State);
		
		// Second call should not be allowed in half-open state
		Assert.False(circuitBreaker.TryExecute(out bool isStateChanged2));
		Assert.False(isStateChanged2);
		Assert.Equal(CircuitBreakerState.HalfOpen, circuitBreaker.State);
		
		// Success should close the circuit
		circuitBreaker.RecordSuccess(out bool isStateChanged3);
		Assert.True(isStateChanged3);
		Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
	}

	[Fact]
	public void SimpleCircuitBreaker_HalfOpenFailureReopensCircuit()
	{
		// Test that failure in half-open state reopens the circuit
		var failuresAllowedBeforeBreaking = 1;
		var durationOfBreak = TimeSpan.FromMilliseconds(100);
		
		var circuitBreaker = new SimpleCircuitBreaker(failuresAllowedBeforeBreaking, durationOfBreak);
		
		// Open the circuit
		circuitBreaker.RecordFailure(out _);
		Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
		
		// Wait and transition to half-open
		Thread.Sleep(durationOfBreak.PlusALittleBit());
		circuitBreaker.TryExecute(out _);
		Assert.Equal(CircuitBreakerState.HalfOpen, circuitBreaker.State);
		
		// Failure in half-open should reopen the circuit
		circuitBreaker.RecordFailure(out bool isStateChanged);
		Assert.True(isStateChanged);
		Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
	}

	[Fact]
	public void AdvancedCircuitBreaker_FailureThresholdWorks()
	{
		// Test failure threshold functionality
		var failureThreshold = 0.5; // 50%
		var samplingDuration = TimeSpan.FromSeconds(10);
		var minimumThroughput = 2;
		var durationOfBreak = TimeSpan.FromMilliseconds(500);
		
		var circuitBreaker = new AdvancedCircuitBreaker(failureThreshold, samplingDuration, minimumThroughput, durationOfBreak);
		
		Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
		
		// Record 1 success, 1 failure (50% failure rate, meets threshold)
		circuitBreaker.RecordSuccess(out _);
		circuitBreaker.RecordFailure(out bool isStateChanged);
		
		// Should open because we've reached minimum throughput (2) and failure rate (50%) >= threshold (50%)
		Assert.True(isStateChanged);
		Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
	}

	[Fact]
	public void AdvancedCircuitBreaker_MinimumThroughputRequired()
	{
		// Test that minimum throughput is required before evaluating failure rate
		var failureThreshold = 0.5; // 50%
		var samplingDuration = TimeSpan.FromSeconds(10);
		var minimumThroughput = 3;
		var durationOfBreak = TimeSpan.FromMilliseconds(500);
		
		var circuitBreaker = new AdvancedCircuitBreaker(failureThreshold, samplingDuration, minimumThroughput, durationOfBreak);
		
		// Record 1 failure (100% failure rate but below minimum throughput)
		circuitBreaker.RecordFailure(out bool isStateChanged1);
		Assert.False(isStateChanged1);
		Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
		
		// Record another failure (still 100% failure rate but below minimum throughput)
		circuitBreaker.RecordFailure(out bool isStateChanged2);
		Assert.False(isStateChanged2);
		Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
		
		// Third failure should trigger circuit opening (3 calls, 100% failure rate > 50% threshold)
		circuitBreaker.RecordFailure(out bool isStateChanged3);
		Assert.True(isStateChanged3);
		Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
	}

	[Fact]
	public void AdvancedCircuitBreaker_SamplingWindowWorks()
	{
		// Test sampling window functionality
		var failureThreshold = 0.5; // 50%
		var samplingDuration = TimeSpan.FromMilliseconds(200);
		var minimumThroughput = 2;
		var durationOfBreak = TimeSpan.FromMilliseconds(500);
		
		var circuitBreaker = new AdvancedCircuitBreaker(failureThreshold, samplingDuration, minimumThroughput, durationOfBreak);
		
		// Record failures that would normally trigger opening
		circuitBreaker.RecordFailure(out _);
		circuitBreaker.RecordFailure(out bool isStateChanged1);
		Assert.True(isStateChanged1);
		Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
		
		// Wait for break duration and return to closed
		Thread.Sleep(durationOfBreak.PlusALittleBit());
		circuitBreaker.TryExecute(out _); // Move to half-open
		circuitBreaker.RecordSuccess(out _); // Move to closed
		
		// Wait for sampling window to expire
		Thread.Sleep(samplingDuration.PlusALittleBit());
		
		// New window should have reset stats
		circuitBreaker.RecordSuccess(out _);
		circuitBreaker.RecordFailure(out bool isStateChanged2);
		// Should stay closed because window was reset
		Assert.False(isStateChanged2);
		Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
	}

	[Fact]
	public void AdvancedCircuitBreaker_HalfOpenStateWorksCorrectly()
	{
		// Test advanced circuit breaker half-open state
		var failureThreshold = 0.5;
		var samplingDuration = TimeSpan.FromSeconds(10);
		var minimumThroughput = 1;
		var durationOfBreak = TimeSpan.FromMilliseconds(100);
		
		var circuitBreaker = new AdvancedCircuitBreaker(failureThreshold, samplingDuration, minimumThroughput, durationOfBreak);
		
		// Open the circuit
		circuitBreaker.RecordFailure(out _);
		Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
		
		// Wait for break duration
		Thread.Sleep(durationOfBreak.PlusALittleBit());
		
		// First call should be allowed and transition to half-open
		Assert.True(circuitBreaker.TryExecute(out bool isStateChanged1));
		Assert.True(isStateChanged1);
		Assert.Equal(CircuitBreakerState.HalfOpen, circuitBreaker.State);
		
		// Second call should not be allowed
		Assert.False(circuitBreaker.TryExecute(out bool isStateChanged2));
		Assert.False(isStateChanged2);
		
		// Success should close the circuit and reset sampling window
		circuitBreaker.RecordSuccess(out bool isStateChanged3);
		Assert.True(isStateChanged3);
		Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
	}

	[Fact]
	public void CircuitBreaker_DisabledWhenDurationIsZero()
	{
		// Test that circuit breaker is effectively disabled when duration is zero
		var simpleCircuitBreaker = new SimpleCircuitBreaker(1, TimeSpan.Zero);
		var advancedCircuitBreaker = new AdvancedCircuitBreaker(0.1, TimeSpan.FromSeconds(1), 1, TimeSpan.Zero);
		
		// Should always allow execution
		Assert.True(simpleCircuitBreaker.TryExecute(out _));
		Assert.True(advancedCircuitBreaker.TryExecute(out _));
		
		// Recording failures should not change state
		simpleCircuitBreaker.RecordFailure(out bool simpleStateChanged);
		advancedCircuitBreaker.RecordFailure(out bool advancedStateChanged);
		
		Assert.False(simpleStateChanged);
		Assert.False(advancedStateChanged);
		Assert.Equal(CircuitBreakerState.Closed, simpleCircuitBreaker.State);
		Assert.Equal(CircuitBreakerState.Closed, advancedCircuitBreaker.State);
		
		// Should still allow execution
		Assert.True(simpleCircuitBreaker.TryExecute(out _));
		Assert.True(advancedCircuitBreaker.TryExecute(out _));
	}

	[Fact]
	public void CircuitBreaker_ManualCloseWorks()
	{
		// Test manual close functionality
		var simpleCircuitBreaker = new SimpleCircuitBreaker(1, TimeSpan.FromMilliseconds(500));
		var advancedCircuitBreaker = new AdvancedCircuitBreaker(0.5, TimeSpan.FromSeconds(10), 1, TimeSpan.FromMilliseconds(500));
		
		// Open both circuits
		simpleCircuitBreaker.RecordFailure(out _);
		advancedCircuitBreaker.RecordFailure(out _);
		
		Assert.Equal(CircuitBreakerState.Open, simpleCircuitBreaker.State);
		Assert.Equal(CircuitBreakerState.Open, advancedCircuitBreaker.State);
		
		// Manual close should work
		simpleCircuitBreaker.Close(out bool simpleStateChanged);
		advancedCircuitBreaker.Close(out bool advancedStateChanged);
		
		Assert.True(simpleStateChanged);
		Assert.True(advancedStateChanged);
		Assert.Equal(CircuitBreakerState.Closed, simpleCircuitBreaker.State);
		Assert.Equal(CircuitBreakerState.Closed, advancedCircuitBreaker.State);
		
		// Should allow execution after manual close
		Assert.True(simpleCircuitBreaker.TryExecute(out _));
		Assert.True(advancedCircuitBreaker.TryExecute(out _));
	}

	[Theory]
	[InlineData(0.0)] // Invalid: 0
	[InlineData(-0.1)] // Invalid: negative
	[InlineData(1.1)] // Invalid: greater than 1
	public void AdvancedCircuitBreaker_ValidatesFailureThreshold(double invalidThreshold)
	{
		// Test that invalid failure thresholds are rejected
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new AdvancedCircuitBreaker(invalidThreshold, TimeSpan.FromSeconds(1), 1, TimeSpan.FromSeconds(1)));
	}

	[Theory]
	[InlineData(0)] // Invalid: 0
	[InlineData(-1)] // Invalid: negative
	public void AdvancedCircuitBreaker_ValidatesMinimumThroughput(int invalidThroughput)
	{
		// Test that invalid minimum throughput values are rejected
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new AdvancedCircuitBreaker(0.5, TimeSpan.FromSeconds(1), invalidThroughput, TimeSpan.FromSeconds(1)));
	}

	[Theory]
	[InlineData(0.1)]
	[InlineData(0.5)]
	[InlineData(1.0)]
	public void AdvancedCircuitBreaker_AcceptsValidFailureThresholds(double validThreshold)
	{
		// Test that valid failure thresholds are accepted
		var circuitBreaker = new AdvancedCircuitBreaker(validThreshold, TimeSpan.FromSeconds(1), 1, TimeSpan.FromSeconds(1));
		Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
	}

	[Theory]
	[InlineData(0)] // Should be normalized to 1
	[InlineData(-5)] // Should be normalized to 1
	public void SimpleCircuitBreaker_NormalizesFailuresAllowedBeforeBreaking(int invalidFailures)
	{
		// Test that invalid failures allowed values are normalized to 1
		var circuitBreaker = new SimpleCircuitBreaker(invalidFailures, TimeSpan.FromSeconds(1));
		
		// Should open after 1 failure (normalized value)
		circuitBreaker.RecordFailure(out bool isStateChanged);
		Assert.True(isStateChanged);
		Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
	}
}
