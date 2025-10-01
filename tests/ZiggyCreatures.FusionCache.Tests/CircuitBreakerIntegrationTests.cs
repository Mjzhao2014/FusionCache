using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Backplane.Memory;
using ZiggyCreatures.Caching.Fusion.Chaos;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Internals;
using FusionCacheTests.Stuff;

namespace FusionCacheTests;

// Helper class to track actual calls to the backplane
internal class CallTrackingBackplane : IFusionCacheBackplane
{
	private readonly IFusionCacheBackplane _innerBackplane;
	private int _publishCallCount;
	private int _subscribeCallCount;

	public CallTrackingBackplane(IFusionCacheBackplane innerBackplane)
	{
		_innerBackplane = innerBackplane;
	}

	public int PublishCallCount => _publishCallCount;
	public int SubscribeCallCount => _subscribeCallCount;

	public void ResetCallCount()
	{
		_publishCallCount = 0;
		_subscribeCallCount = 0;
	}


	public void Subscribe(BackplaneSubscriptionOptions options)
	{
		Interlocked.Increment(ref _subscribeCallCount);
		_innerBackplane.Subscribe(options);
	}

	public ValueTask SubscribeAsync(BackplaneSubscriptionOptions options)
	{
		Interlocked.Increment(ref _subscribeCallCount);
		return _innerBackplane.SubscribeAsync(options);
	}

	public void Unsubscribe()
	{
		_innerBackplane.Unsubscribe();
	}

	public ValueTask UnsubscribeAsync()
	{
		return _innerBackplane.UnsubscribeAsync();
	}

	public void Publish(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default)
	{
		Interlocked.Increment(ref _publishCallCount);
		_innerBackplane.Publish(message, options, token);
	}

	public ValueTask PublishAsync(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default)
	{
		Interlocked.Increment(ref _publishCallCount);
		return _innerBackplane.PublishAsync(message, options, token);
	}
}

public class CircuitBreakerIntegrationTests : AbstractTests
{
	public CircuitBreakerIntegrationTests(ITestOutputHelper output)
		: base(output, null)
	{
	}

	private FusionCacheOptions CreateFusionCacheOptions()
	{
		var res = new FusionCacheOptions
		{
			CacheKeyPrefix = TestingCacheKeyPrefix
		};

		return res;
	}

	protected string CreateRandomCacheKey(string prefix = "test")
	{
		return $"{prefix}:{Guid.NewGuid():N}";
	}

	[Theory]
	[ClassData(typeof(SerializerTypesClassData))]
	public async Task SimpleCircuitBreaker_IntegratesWithDistributedCache(SerializerType serializerType)
	{
		var duration = TimeSpan.FromMilliseconds(2_000);
		var maxFailuresBeforeBreaking = 2;
		var durationOfBreak = TimeSpan.FromMilliseconds(200);

		using var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var chaosDistributedCache = new ChaosDistributedCache(distributedCache);

		var options = CreateFusionCacheOptions();
		options.EnableAutoRecovery = false;
		options.DistributedCacheCircuitBreakerFailuresAllowedBeforeBreaking = maxFailuresBeforeBreaking;
		options.DistributedCacheCircuitBreakerDuration = durationOfBreak;
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundDistributedCacheOperations = false;

		// Configure chaos cache to always fail
		chaosDistributedCache.SetAlwaysThrow();

		fusionCache.SetupDistributedCache(chaosDistributedCache, TestsUtils.GetSerializer(serializerType));

		var key = "foo";
		var value = 42;

		// Verify initial state
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
		Assert.Equal(0, TestsUtils.GetDistributedCacheCircuitBreakerFailureCount(fusionCache));

		// First set should fail but won't open circuit yet (failure count = 1)
		await fusionCache.SetAsync(key, value);
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
		Assert.Equal(1, TestsUtils.GetDistributedCacheCircuitBreakerFailureCount(fusionCache));

		// Second set should fail and open circuit (failure count = 2)
		await fusionCache.SetAsync(key + "2", value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
		Assert.Equal(2, TestsUtils.GetDistributedCacheCircuitBreakerFailureCount(fusionCache));

		// Third set won't be passed through since the circut breaer is open, and it's not counted as failure.
		await fusionCache.SetAsync(key + "3", value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
		Assert.Equal(2, TestsUtils.GetDistributedCacheCircuitBreakerFailureCount(fusionCache));

		// Wait for break duration and verify half-open behavior
		await Task.Delay(durationOfBreak.PlusALittleBit());

		// Fix the distributed cache to allow successful operations
		chaosDistributedCache.SetNeverThrow();

		// Next operation should transition to half-open and open after the setasync completed.
		await fusionCache.SetAsync(key + "4", value);
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
		Assert.Equal(0, TestsUtils.GetDistributedCacheCircuitBreakerFailureCount(fusionCache));
	}

	[Theory]
	[ClassData(typeof(SerializerTypesClassData))]
	public async Task AdvancedCircuitBreaker_IntegratesWithDistributedCache(SerializerType serializerType)
	{
		var duration = TimeSpan.FromMilliseconds(2_000);
		var failureThreshold = 0.5; // 50%
		var samplingDuration = TimeSpan.FromMilliseconds(500);
		var minimumThroughput = 2;
		var durationOfBreak = TimeSpan.FromMilliseconds(200);

		using var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var chaosDistributedCache = new ChaosDistributedCache(distributedCache);

		var options = CreateFusionCacheOptions();
		options.EnableAutoRecovery = false;
		options.DistributedCacheCircuitBreakerFailureThreshold = failureThreshold;
		options.DistributedCacheCircuitBreakerSamplingDuration = samplingDuration;
		options.DistributedCacheCircuitBreakerMinimumThroughput = minimumThroughput;
		options.DistributedCacheCircuitBreakerDuration = durationOfBreak;
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundDistributedCacheOperations = false;

		fusionCache.SetupDistributedCache(chaosDistributedCache, TestsUtils.GetSerializer(serializerType));

		var key = "foo";
		var value = 42;

		// Verify initial state
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
		Assert.Equal(0, TestsUtils.GetDistributedCacheCircuitBreakerFailureCount(fusionCache));

		// Configure chaos to allow first operation to succeed, then fail
		chaosDistributedCache.SetNeverThrow();
		await fusionCache.SetAsync(key, value);
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		Thread.Sleep(samplingDuration.PlusALittleBit());
		// Now make it fail
		chaosDistributedCache.SetAlwaysThrow();
		await fusionCache.SetAsync(key + "2", value);
		// first pass, circuit breaker keep close
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// Should open because we have 50% failure rate with 2 calls (meets minimum throughput)
		chaosDistributedCache.SetNeverThrow();
		await fusionCache.SetAsync(key + "3", value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
	}

	[Fact]
	public async Task SimpleCircuitBreaker_IntegratesWithBackplane()
	{
		var duration = TimeSpan.FromMilliseconds(2_000);
		var maxFailuresBeforeBreaking = 3;
		var durationOfBreak = TimeSpan.FromMilliseconds(200);
		var backplaneConnectionId = "test-connection";

		using var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions() { ConnectionId = backplaneConnectionId });
		var chaosBackplane = new ChaosBackplane(backplane);

		var options = CreateFusionCacheOptions();
		options.EnableAutoRecovery = false;
		options.BackplaneCircuitBreakerFailuresAllowedBeforeBreaking = maxFailuresBeforeBreaking;
		options.BackplaneCircuitBreakerDuration = durationOfBreak;
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundBackplaneOperations = false;

		// Configure chaos backplane to always fail
		chaosBackplane.SetAlwaysThrow();

		fusionCache.SetupBackplane(chaosBackplane);

		var key = "foo";
		var value = 42;

		// Verify initial state
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));
		Assert.Equal(0, TestsUtils.GetBackplaneCircuitBreakerFailureCount(fusionCache));

		// Operations that trigger backplane notifications should fail but not immediately open circuit
		await fusionCache.SetAsync(key, value);
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));
		Assert.Equal(1, TestsUtils.GetBackplaneCircuitBreakerFailureCount(fusionCache));

		await fusionCache.SetAsync(key + "2", value);
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));
		Assert.Equal(2, TestsUtils.GetBackplaneCircuitBreakerFailureCount(fusionCache));

		await fusionCache.SetAsync(key + "3", value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));
		Assert.Equal(3, TestsUtils.GetBackplaneCircuitBreakerFailureCount(fusionCache));
	}

	[Fact]
	public async Task AdvancedCircuitBreaker_IntegratesWithBackplane()
	{
		var duration = TimeSpan.FromMilliseconds(2_000);
		var failureThreshold = 0.5; // 50%
		var samplingDuration = TimeSpan.FromSeconds(10);
		var minimumThroughput = 2;
		var durationOfBreak = TimeSpan.FromMilliseconds(200);
		var backplaneConnectionId = "test-connection";

		using var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions() { ConnectionId = backplaneConnectionId });
		var chaosBackplane = new ChaosBackplane(backplane);

		var options = CreateFusionCacheOptions();
		options.EnableAutoRecovery = false;
		options.BackplaneCircuitBreakerFailureThreshold = failureThreshold;
		options.BackplaneCircuitBreakerSamplingDuration = samplingDuration;
		options.BackplaneCircuitBreakerMinimumThroughput = minimumThroughput;
		options.BackplaneCircuitBreakerDuration = durationOfBreak;
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundBackplaneOperations = false;

		fusionCache.SetupBackplane(chaosBackplane);

		var key = "foo";
		var value = 42;

		// Verify initial state
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));

		// Configure chaos to allow first operation to succeed
		chaosBackplane.SetNeverThrow();
		await fusionCache.SetAsync(key, value);
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));

		// Now make it fail
		chaosBackplane.SetAlwaysThrow();
		await fusionCache.SetAsync(key + "2", value);

		// Should open because we have 50% failure rate with 2 calls
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));
	}

	[Fact]
	public async Task CircuitBreaker_JitterWorks_DistributedCacheAndBackplane()
	{
		// Test that distributed cache and backplane circuit breakers have independent jitter
		var maxFailuresBeforeBreaking = 1;
		var distributedCacheDurationOfBreak = TimeSpan.FromMilliseconds(200);
		var backplaneDurationOfBreak = TimeSpan.FromMilliseconds(500);
		var distributedCacheJitter = TimeSpan.FromMilliseconds(50);
		var backplaneJitter = TimeSpan.FromMilliseconds(500);
		var backplaneConnectionId = "test-connection";

		using var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var chaosDistributedCache = new ChaosDistributedCache(distributedCache);
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions() { ConnectionId = backplaneConnectionId });
		var chaosBackplane = new ChaosBackplane(backplane);

		var options = CreateFusionCacheOptions();
		options.EnableAutoRecovery = false;
		// Different jitter for each circuit breaker
		options.DistributedCacheCircuitBreakerFailuresAllowedBeforeBreaking = maxFailuresBeforeBreaking;
		options.DistributedCacheCircuitBreakerDuration = distributedCacheDurationOfBreak;
		options.DistributedCacheCircuitBreakerJitterMaxDuration = distributedCacheJitter;
		options.BackplaneCircuitBreakerFailuresAllowedBeforeBreaking = maxFailuresBeforeBreaking;
		options.BackplaneCircuitBreakerDuration = backplaneDurationOfBreak;
		options.BackplaneCircuitBreakerJitterMaxDuration = backplaneJitter;

		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundDistributedCacheOperations = false;
		fusionCache.DefaultEntryOptions.AllowBackgroundBackplaneOperations = false;

		fusionCache.SetupDistributedCache(chaosDistributedCache, TestsUtils.GetSerializer(SerializerType.SystemTextJson));
		fusionCache.SetupBackplane(chaosBackplane);

		var key = "foo";
		var value = 42;

		// Open both circuits simultaneously
		chaosDistributedCache.SetAlwaysThrow();
		chaosBackplane.SetAlwaysThrow();
		await fusionCache.SetAsync(key, value);

		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));

		// Fix both systems
		chaosDistributedCache.SetNeverThrow();
		chaosBackplane.SetNeverThrow();

		// Wait for base duration only given 50ms leeway - both should still be open due to jitter
		await Task.Delay(distributedCacheDurationOfBreak.Add(TimeSpan.FromMilliseconds(-50)));
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));

		// Wait for distributed cache jitter to potentially expire but not backplane
		await Task.Delay(distributedCacheJitter.Add(TimeSpan.FromMilliseconds(50)));

		// Test that jitter configuration is respected (operations should complete without errors)
		await fusionCache.SetAsync(key + "2", value);
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));

		// Wait for backplane jitter to potentially expire
		await Task.Delay(backplaneDurationOfBreak.Add(backplaneJitter).PlusALittleBit());
		await fusionCache.SetAsync(key + "3", value);
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));
	}

	[Theory]
	[ClassData(typeof(SerializerTypesClassData))]
	public async Task CircuitBreaker_FiresEventsOnStateTransitions_DistributedCache(SerializerType serializerType)
	{
		var maxFailuresBeforeBreaking = 1;
		var durationOfBreak = TimeSpan.FromMilliseconds(100);

		using var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var chaosDistributedCache = new ChaosDistributedCache(distributedCache);

		var options = CreateFusionCacheOptions();
		options.EnableAutoRecovery = false;
		options.DistributedCacheCircuitBreakerFailuresAllowedBeforeBreaking = maxFailuresBeforeBreaking;
		options.DistributedCacheCircuitBreakerDuration = durationOfBreak;
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundDistributedCacheOperations = false;

		fusionCache.SetupDistributedCache(chaosDistributedCache, TestsUtils.GetSerializer(serializerType));

		Dictionary<CircuitBreakerState, int> stateCounts = new Dictionary<CircuitBreakerState, int>
		{
			{ CircuitBreakerState.Closed, 0 },
			{ CircuitBreakerState.Open, 0 },
			{ CircuitBreakerState.HalfOpen, 0 }
		};

		fusionCache.Events.Distributed.CircuitBreakerChange += (sender, e) =>
		{
			lock (stateCounts)
			{
				stateCounts[e.State] = stateCounts[e.State] + 1;
			}
		};

		var key = "test-key";
		var value = 42;

		// Initial state: Closed (no events yet)
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// STEP 1: Open the circuit (should fire event with isActive=false)
		chaosDistributedCache.SetAlwaysThrow();
		await fusionCache.SetAsync(key, value);
		
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		Assert.True(TestsUtils.WaitForCircuitBreakerStateCount(stateCounts, CircuitBreakerState.Open, 1, TimeSpan.FromSeconds(2)), "Circuit breaker open event not received in time");
		Assert.Equal(1, TestsUtils.GetCircuitBreakerStateCount(stateCounts, CircuitBreakerState.Open));

		// STEP 2: Wait for break duration, then trigger half-open transition
		await Task.Delay(durationOfBreak.PlusALittleBit());
		chaosDistributedCache.SetNeverThrow(); // Fix the cache for success

		// 1. This should transition to half-open  and fire half-open event
		// 2. After SetAsync succesfully, the circuit breaker will be closed and fire closed event
		await fusionCache.SetAsync(key + "_recovery", value);
		
		Assert.True(TestsUtils.WaitForCircuitBreakerStateCount(stateCounts, CircuitBreakerState.HalfOpen, 1, TimeSpan.FromSeconds(2)), "Circuit breaker half-open event not received in time");
		Assert.True(TestsUtils.WaitForCircuitBreakerStateCount(stateCounts, CircuitBreakerState.Closed, 1, TimeSpan.FromSeconds(2)), "Circuit breaker closed event not received in time");
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
		Assert.Equal(1, TestsUtils.GetCircuitBreakerStateCount(stateCounts, CircuitBreakerState.HalfOpen));
		Assert.Equal(1, TestsUtils.GetCircuitBreakerStateCount(stateCounts, CircuitBreakerState.Closed));
		Assert.Equal(1, TestsUtils.GetCircuitBreakerStateCount(stateCounts, CircuitBreakerState.Open)); //unchanged
	}

	[Fact]
	public async Task CircuitBreaker_FiresEventsOnStateTransitions_Backplane()
	{
		var maxFailuresBeforeBreaking = 1;
		var durationOfBreak = TimeSpan.FromMilliseconds(100);
		var backplaneConnectionId = "test-connection";

		using var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions() { ConnectionId = backplaneConnectionId });
		var chaosBackplane = new ChaosBackplane(backplane);

		var options = CreateFusionCacheOptions();
		options.EnableAutoRecovery = false;
		options.BackplaneCircuitBreakerFailuresAllowedBeforeBreaking = maxFailuresBeforeBreaking;
		options.BackplaneCircuitBreakerDuration = durationOfBreak;
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundBackplaneOperations = false;

		fusionCache.SetupBackplane(chaosBackplane);

		Dictionary<CircuitBreakerState, int> stateCounts = new Dictionary<CircuitBreakerState, int>
		{
			{ CircuitBreakerState.Closed, 0 },
			{ CircuitBreakerState.Open, 0 },
			{ CircuitBreakerState.HalfOpen, 0 }
		};

		fusionCache.Events.Backplane.CircuitBreakerChange += (sender, e) =>
		{
			lock (stateCounts)
			{
				stateCounts[e.State] = stateCounts[e.State] + 1;
			}
		};

		var key = "test-key";
		var value = 42;

		// Initial state: Closed (no events yet)
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));

		// STEP 1: Open the circuit (should fire event with isActive=false)
		chaosBackplane.SetAlwaysThrow();
		await fusionCache.SetAsync(key, value);

		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));

		Assert.True(TestsUtils.WaitForCircuitBreakerStateCount(stateCounts, CircuitBreakerState.Open, 1, TimeSpan.FromSeconds(2)), "Circuit breaker open event not received in time");
		Assert.Equal(1, TestsUtils.GetCircuitBreakerStateCount(stateCounts, CircuitBreakerState.Open));

		// STEP 2: Wait for break duration, then trigger half-open transition
		await Task.Delay(durationOfBreak.PlusALittleBit());
		chaosBackplane.SetNeverThrow(); // Fix the backplane for success

		// 1. This should transition to half-open  and fire half-open event
		// 2. The setasync will be successful and circuit will be closed and fire closed event
		await fusionCache.SetAsync(key + "_recovery", value);

		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));
		Assert.True(TestsUtils.WaitForCircuitBreakerStateCount(stateCounts, CircuitBreakerState.HalfOpen, 1, TimeSpan.FromSeconds(2)), "Circuit breaker half-open event not received in time");
		Assert.True(TestsUtils.WaitForCircuitBreakerStateCount(stateCounts, CircuitBreakerState.Closed, 1, TimeSpan.FromSeconds(2)), "Circuit breaker closed event not received in time");
		Assert.Equal(1, TestsUtils.GetCircuitBreakerStateCount(stateCounts, CircuitBreakerState.HalfOpen));
		Assert.Equal(1, TestsUtils.GetCircuitBreakerStateCount(stateCounts, CircuitBreakerState.Closed)); // Still one open event
		Assert.Equal(1, TestsUtils.GetCircuitBreakerStateCount(stateCounts, CircuitBreakerState.Open)); //unchanged
	}

}
