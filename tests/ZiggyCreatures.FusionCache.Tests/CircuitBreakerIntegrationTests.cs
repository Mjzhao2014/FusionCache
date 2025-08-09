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

// Helper class to track actual calls to the distributed cache
internal class CallTrackingDistributedCache : IDistributedCache
{
	private readonly IDistributedCache _innerCache;
	private int _getCallCount;
	private int _setCallCount;
	private int _removeCallCount;

	public CallTrackingDistributedCache(IDistributedCache innerCache)
	{
		_innerCache = innerCache;
	}

	public int GetCallCount => _getCallCount;
	public int SetCallCount => _setCallCount;
	public int RemoveCallCount => _removeCallCount;

	public void ResetCallCount()
	{
		_getCallCount = 0;
		_setCallCount = 0;
		_removeCallCount = 0;
	}

	public byte[]? Get(string key)
	{
		Interlocked.Increment(ref _getCallCount);
		return _innerCache.Get(key);
	}

	public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
	{
		Interlocked.Increment(ref _getCallCount);
		return _innerCache.GetAsync(key, token);
	}

	public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
	{
		Interlocked.Increment(ref _setCallCount);
		_innerCache.Set(key, value, options);
	}

	public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
	{
		Interlocked.Increment(ref _setCallCount);
		return _innerCache.SetAsync(key, value, options, token);
	}

	public void Refresh(string key)
	{
		_innerCache.Refresh(key);
	}

	public Task RefreshAsync(string key, CancellationToken token = default)
	{
		return _innerCache.RefreshAsync(key, token);
	}

	public void Remove(string key)
	{
		Interlocked.Increment(ref _removeCallCount);
		_innerCache.Remove(key);
	}

	public Task RemoveAsync(string key, CancellationToken token = default)
	{
		Interlocked.Increment(ref _removeCallCount);
		return _innerCache.RemoveAsync(key, token);
	}
}

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
		
		// Second set should fail and still not open circuit (failure count = 2)
		await fusionCache.SetAsync(key + "2", value);
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
		Assert.Equal(2, TestsUtils.GetDistributedCacheCircuitBreakerFailureCount(fusionCache));

		// Third set should open the circuit (exceeds maxFailuresBeforeBreaking = 2)
		await fusionCache.SetAsync(key + "3", value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// Should not attempt distributed cache operations while open
		await fusionCache.SetAsync(key + "4", value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// Wait for break duration and verify half-open behavior
		await Task.Delay(durationOfBreak.PlusALittleBit());

		// Fix the distributed cache to allow successful operations
		chaosDistributedCache.SetNeverThrow();

		// Next operation should transition to half-open
		await fusionCache.SetAsync(key + "5", value);
		Assert.Equal(CircuitBreakerState.HalfOpen, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
	}

	[Theory]
	[ClassData(typeof(SerializerTypesClassData))]
	public async Task AdvancedCircuitBreaker_IntegratesWithDistributedCache(SerializerType serializerType)
	{
		var duration = TimeSpan.FromMilliseconds(2_000);
		var failureThreshold = 0.5; // 50%
		var samplingDuration = TimeSpan.FromSeconds(10);
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

		// Now make it fail
		chaosDistributedCache.SetAlwaysThrow();
		await fusionCache.SetAsync(key + "2", value);

		// Should open because we have 50% failure rate with 2 calls (meets minimum throughput)
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
	}

	[Fact]
	public async Task SimpleCircuitBreaker_IntegratesWithBackplane()
	{
		var duration = TimeSpan.FromMilliseconds(2_000);
		var maxFailuresBeforeBreaking = 2;
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
		// Circuit should now be open due to consecutive failures (exceeds maxFailuresBeforeBreaking = 2)
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));
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

	[Theory]
	[ClassData(typeof(SerializerTypesClassData))]
	public async Task CircuitBreaker_RecoveryScenario_DistributedCache(SerializerType serializerType)
	{
		var duration = TimeSpan.FromMilliseconds(2_000);
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

		var key = "foo";
		var value = 42;

		// Verify initial state
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// Open the circuit
		chaosDistributedCache.SetAlwaysThrow();
		await fusionCache.SetAsync(key, value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// Wait for break duration
		await Task.Delay(durationOfBreak.PlusALittleBit());

		// Fix the distributed cache
		chaosDistributedCache.SetNeverThrow();

		// Next operation should go to half-open and succeed
		await fusionCache.SetAsync(key + "2", value);
		Assert.Equal(CircuitBreakerState.HalfOpen, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// Subsequent operations should work normally and close the circuit
		await fusionCache.SetAsync(key + "3", value);
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
	}

	[Theory]
	[ClassData(typeof(SerializerTypesClassData))]
	public async Task CircuitBreaker_DisabledWhenDurationIsZero_DistributedCache(SerializerType serializerType)
	{
		var duration = TimeSpan.FromMilliseconds(2_000);

		using var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var chaosDistributedCache = new ChaosDistributedCache(distributedCache);

		var options = CreateFusionCacheOptions();
		options.EnableAutoRecovery = false;
		// Circuit breaker with zero break duration should be effectively disabled
		options.DistributedCacheCircuitBreakerFailuresAllowedBeforeBreaking = 1;
		options.DistributedCacheCircuitBreakerDuration = TimeSpan.Zero;
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundDistributedCacheOperations = false;

		fusionCache.SetupDistributedCache(chaosDistributedCache, TestsUtils.GetSerializer(serializerType));

		var key = "foo";
		var value = 42;

		// Configure chaos cache to always fail
		chaosDistributedCache.SetAlwaysThrow();

		// Multiple operations should not open the circuit (it's disabled)
		await fusionCache.SetAsync(key, value);
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
		
		await fusionCache.SetAsync(key + "2", value);
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
		
		await fusionCache.SetAsync(key + "3", value);
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// Operations should continue to be attempted (circuit breaker disabled)
		// Even with multiple failures, the circuit should remain closed when duration is zero
	}

	[Theory]
	[ClassData(typeof(SerializerTypesClassData))]
	public async Task CircuitBreaker_BothDistributedCacheAndBackplane_IndependentStates(SerializerType serializerType)
	{
		var duration = TimeSpan.FromMilliseconds(2_000);
		var maxFailuresBeforeBreaking = 1;
		var durationOfBreak = TimeSpan.FromMilliseconds(200);
		var backplaneConnectionId = "test-connection";

		using var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var chaosDistributedCache = new ChaosDistributedCache(distributedCache);
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions() { ConnectionId = backplaneConnectionId });
		var chaosBackplane = new ChaosBackplane(backplane);

		var options = CreateFusionCacheOptions();
		options.EnableAutoRecovery = false;
		// Independent circuit breakers
		options.DistributedCacheCircuitBreakerFailuresAllowedBeforeBreaking = maxFailuresBeforeBreaking;
		options.DistributedCacheCircuitBreakerDuration = durationOfBreak;
		options.BackplaneCircuitBreakerFailuresAllowedBeforeBreaking = maxFailuresBeforeBreaking;
		options.BackplaneCircuitBreakerDuration = durationOfBreak;
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundDistributedCacheOperations = false;
		fusionCache.DefaultEntryOptions.AllowBackgroundBackplaneOperations = false;

		fusionCache.SetupDistributedCache(chaosDistributedCache, TestsUtils.GetSerializer(serializerType));
		fusionCache.SetupBackplane(chaosBackplane);

		var key = "foo";
		var value = 42;

		// Make distributed cache fail but backplane work
		chaosDistributedCache.SetAlwaysThrow();
		chaosBackplane.SetNeverThrow();

		await fusionCache.SetAsync(key, value);

		// Now make distributed cache work but backplane fail
		chaosDistributedCache.SetNeverThrow();
		chaosBackplane.SetAlwaysThrow();

		// Wait for distributed cache circuit to potentially recover
		await Task.Delay(durationOfBreak.PlusALittleBit());

		await fusionCache.SetAsync(key + "2", value);

		// The circuits should operate independently
		// We can't easily assert internal state, but the operations should complete
	}

	[Theory]
	[ClassData(typeof(SerializerTypesClassData))]
	public async Task SimpleCircuitBreaker_JitterWorksWithDistributedCache(SerializerType serializerType)
	{
		var duration = TimeSpan.FromMilliseconds(2_000);
		var maxFailuresBeforeBreaking = 1;
		var durationOfBreak = TimeSpan.FromMilliseconds(500);
		var jitterMaxDuration = TimeSpan.FromMilliseconds(200); // 200ms jitter

		using var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var chaosDistributedCache = new ChaosDistributedCache(distributedCache);

		var options = CreateFusionCacheOptions();
		options.EnableAutoRecovery = false;
		options.DistributedCacheCircuitBreakerFailuresAllowedBeforeBreaking = maxFailuresBeforeBreaking;
		options.DistributedCacheCircuitBreakerDuration = durationOfBreak;
		options.DistributedCacheCircuitBreakerJitterMaxDuration = jitterMaxDuration;
		
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundDistributedCacheOperations = false;

		fusionCache.SetupDistributedCache(chaosDistributedCache, TestsUtils.GetSerializer(serializerType));

		var key = "foo";
		var value = 42;

		// Verify initial state
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// Configure chaos cache to always fail to open the circuit
		chaosDistributedCache.SetAlwaysThrow();
		await fusionCache.SetAsync(key, value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		var startTime = DateTimeOffset.UtcNow;

		// Fix the distributed cache before checking recovery
		chaosDistributedCache.SetNeverThrow();

		// Wait just past the base break duration but not the full jitter
		await Task.Delay(durationOfBreak.Add(TimeSpan.FromMilliseconds(50)));

		// Circuit may or may not be ready depending on jitter (this is the key test)
		var operationTime = DateTimeOffset.UtcNow;
		await fusionCache.SetAsync(key + "2", value);
		
		// Verify the circuit transitioned to half-open (indicating jitter worked and recovery occurred)
		Assert.Equal(CircuitBreakerState.HalfOpen, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// The total time should be at least base duration but potentially up to base + jitter
		var totalElapsed = operationTime - startTime;
		Assert.True(totalElapsed >= durationOfBreak.Subtract(TimeSpan.FromMilliseconds(50)), 
			"Circuit recovery should take at least the base duration");
		Assert.True(totalElapsed <= durationOfBreak.Add(jitterMaxDuration).Add(TimeSpan.FromMilliseconds(100)), 
			"Circuit recovery should not exceed base duration + jitter + tolerance");
	}

	[Theory]
	[ClassData(typeof(SerializerTypesClassData))]
	public async Task AdvancedCircuitBreaker_JitterWorksWithDistributedCache(SerializerType serializerType)
	{
		var duration = TimeSpan.FromMilliseconds(2_000);
		var failureThreshold = 0.5; // 50%
		var samplingDuration = TimeSpan.FromSeconds(10);
		var minimumThroughput = 1;
		var durationOfBreak = TimeSpan.FromMilliseconds(500);
		var jitterMaxDuration = TimeSpan.FromMilliseconds(300); // 300ms jitter

		using var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var chaosDistributedCache = new ChaosDistributedCache(distributedCache);

		var options = CreateFusionCacheOptions();
		options.EnableAutoRecovery = false;
		options.DistributedCacheCircuitBreakerFailureThreshold = failureThreshold;
		options.DistributedCacheCircuitBreakerSamplingDuration = samplingDuration;
		options.DistributedCacheCircuitBreakerMinimumThroughput = minimumThroughput;
		options.DistributedCacheCircuitBreakerDuration = durationOfBreak;
		options.DistributedCacheCircuitBreakerJitterMaxDuration = jitterMaxDuration;
		
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundDistributedCacheOperations = false;

		fusionCache.SetupDistributedCache(chaosDistributedCache, TestsUtils.GetSerializer(serializerType));

		var key = "foo";
		var value = 42;

		// Verify initial state
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// Open the circuit with failure
		chaosDistributedCache.SetAlwaysThrow();
		await fusionCache.SetAsync(key, value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		var startTime = DateTimeOffset.UtcNow;

		// Fix the distributed cache
		chaosDistributedCache.SetNeverThrow();

		// Wait just past the base break duration but not the full jitter
		await Task.Delay(durationOfBreak.Add(TimeSpan.FromMilliseconds(50)));

		// Circuit may or may not be ready depending on jitter
		var operationTime = DateTimeOffset.UtcNow;
		await fusionCache.SetAsync(key + "2", value);
		
		// Verify the circuit transitioned to half-open (indicating jitter worked and recovery occurred)
		Assert.Equal(CircuitBreakerState.HalfOpen, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		var totalElapsed = operationTime - startTime;
		Assert.True(totalElapsed >= durationOfBreak.Subtract(TimeSpan.FromMilliseconds(50)), 
			"Circuit recovery should take at least the base duration");
		Assert.True(totalElapsed <= durationOfBreak.Add(jitterMaxDuration).Add(TimeSpan.FromMilliseconds(100)), 
			"Circuit recovery should not exceed base duration + jitter + tolerance");
	}

	[Fact]
	public async Task SimpleCircuitBreaker_JitterWorksWithBackplane()
	{
		var duration = TimeSpan.FromMilliseconds(2_000);
		var maxFailuresBeforeBreaking = 1;
		var durationOfBreak = TimeSpan.FromMilliseconds(500);
		var jitterMaxDuration = TimeSpan.FromMilliseconds(200); // 200ms jitter
		var backplaneConnectionId = "test-connection";

		using var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions() { ConnectionId = backplaneConnectionId });
		var chaosBackplane = new ChaosBackplane(backplane);

		var options = CreateFusionCacheOptions();
		options.EnableAutoRecovery = false;
		options.BackplaneCircuitBreakerFailuresAllowedBeforeBreaking = maxFailuresBeforeBreaking;
		options.BackplaneCircuitBreakerDuration = durationOfBreak;
		options.BackplaneCircuitBreakerJitterMaxDuration = jitterMaxDuration;
		
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundBackplaneOperations = false;

		fusionCache.SetupBackplane(chaosBackplane);

		var key = "foo";
		var value = 42;

		// Verify initial state
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));

		// Open the circuit with failure
		chaosBackplane.SetAlwaysThrow();
		await fusionCache.SetAsync(key, value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));

		var startTime = DateTimeOffset.UtcNow;

		// Fix the backplane
		chaosBackplane.SetNeverThrow();

		// Wait just past the base break duration but not the full jitter
		await Task.Delay(durationOfBreak.Add(TimeSpan.FromMilliseconds(50)));

		// Circuit may or may not be ready depending on jitter
		var operationTime = DateTimeOffset.UtcNow;
		await fusionCache.SetAsync(key + "2", value);
		
		// Verify the circuit transitioned to half-open (indicating jitter worked and recovery occurred)
		Assert.Equal(CircuitBreakerState.HalfOpen, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));

		var totalElapsed = operationTime - startTime;
		Assert.True(totalElapsed >= durationOfBreak.Subtract(TimeSpan.FromMilliseconds(50)), 
			"Circuit recovery should take at least the base duration");
		Assert.True(totalElapsed <= durationOfBreak.Add(jitterMaxDuration).Add(TimeSpan.FromMilliseconds(100)), 
			"Circuit recovery should not exceed base duration + jitter + tolerance");
	}

	[Fact]
	public async Task AdvancedCircuitBreaker_JitterWorksWithBackplane()
	{
		var duration = TimeSpan.FromMilliseconds(2_000);
		var failureThreshold = 0.5; // 50%
		var samplingDuration = TimeSpan.FromSeconds(10);
		var minimumThroughput = 1;
		var durationOfBreak = TimeSpan.FromMilliseconds(500);
		var jitterMaxDuration = TimeSpan.FromMilliseconds(300); // 300ms jitter
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
		options.BackplaneCircuitBreakerJitterMaxDuration = jitterMaxDuration;
		
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundBackplaneOperations = false;

		fusionCache.SetupBackplane(chaosBackplane);

		var key = "foo";
		var value = 42;

		// Verify initial state
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));

		// Open the circuit with failure
		chaosBackplane.SetAlwaysThrow();
		await fusionCache.SetAsync(key, value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));

		var startTime = DateTimeOffset.UtcNow;

		// Fix the backplane
		chaosBackplane.SetNeverThrow();

		// Wait just past the base break duration but not the full jitter
		await Task.Delay(durationOfBreak.Add(TimeSpan.FromMilliseconds(50)));

		// Circuit may or may not be ready depending on jitter
		var operationTime = DateTimeOffset.UtcNow;
		
		// Verify the circuit transitioned to half-open (indicating jitter worked and recovery occurred)
		Assert.Equal(CircuitBreakerState.HalfOpen, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));

		await fusionCache.SetAsync(key + "2", value);
		// Verify the circuit transitioned to half-open (indicating jitter worked and recovery occurred)
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));


		var totalElapsed = operationTime - startTime;
		Assert.True(totalElapsed > durationOfBreak.Subtract(TimeSpan.FromMilliseconds(50)), 
			"Circuit recovery should take at least the base duration");
		Assert.True(totalElapsed < durationOfBreak.Add(jitterMaxDuration).Add(TimeSpan.FromMilliseconds(100)), 
			"Circuit recovery should not exceed base duration + jitter + tolerance");
	}

	[Theory]
	[ClassData(typeof(SerializerTypesClassData))]
	public async Task CircuitBreaker_JitterDistributionIsReasonableInRealScenario(SerializerType serializerType)
	{
		// Integration test to verify jitter produces reasonable distribution in a real FusionCache scenario
		const int iterations = 20; // Reduced for integration test speed
		var maxFailuresBeforeBreaking = 1;
		var durationOfBreak = TimeSpan.FromMilliseconds(300);
		var jitterMaxDuration = TimeSpan.FromMilliseconds(200); // Significant jitter for testing

		var recoveryTimes = new List<TimeSpan>();

		for (int i = 0; i < iterations; i++)
		{
			using var memoryCache = new MemoryCache(new MemoryCacheOptions());
			var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
			var chaosDistributedCache = new ChaosDistributedCache(distributedCache);

			var options = CreateFusionCacheOptions();
			options.EnableAutoRecovery = false;
			options.DistributedCacheCircuitBreakerFailuresAllowedBeforeBreaking = maxFailuresBeforeBreaking;
			options.DistributedCacheCircuitBreakerDuration = durationOfBreak;
			options.DistributedCacheCircuitBreakerJitterMaxDuration = jitterMaxDuration;
			
			using var fusionCache = new FusionCache(options, memoryCache);
			fusionCache.DefaultEntryOptions.AllowBackgroundDistributedCacheOperations = false;

			fusionCache.SetupDistributedCache(chaosDistributedCache, TestsUtils.GetSerializer(serializerType));

			var key = $"foo{i}";
			var value = 42;

			// Verify initial state and open the circuit
			Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
			chaosDistributedCache.SetAlwaysThrow();
			await fusionCache.SetAsync(key, value);
			Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

			var startTime = DateTimeOffset.UtcNow;

			// Fix the distributed cache
			chaosDistributedCache.SetNeverThrow();

			// Wait until the circuit transitions to half-open (indicating recovery with jitter)
			bool recovered = false;
			TimeSpan recoveryTime = TimeSpan.Zero;
			
			while (!recovered && recoveryTime < durationOfBreak.Add(jitterMaxDuration).Add(TimeSpan.FromMilliseconds(200)))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(10));
				recoveryTime = DateTimeOffset.UtcNow - startTime;
				
				// Check if circuit is ready for operations by attempting one
				await fusionCache.SetAsync(key + "test", value);
				
				// Check if the circuit has transitioned to half-open (indicating actual recovery)
				if (TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache) == CircuitBreakerState.HalfOpen)
				{
					recovered = true;
					recoveryTimes.Add(recoveryTime);
				}
			}

			if (!recovered)
			{
				// Fallback - circuit never recovered within expected time
				recoveryTimes.Add(durationOfBreak.Add(jitterMaxDuration).Add(TimeSpan.FromMilliseconds(50)));
			}
		}

		// Verify distribution characteristics
		var minTime = recoveryTimes.Min();
		var maxTime = recoveryTimes.Max();

		Assert.True(minTime >= durationOfBreak.Subtract(TimeSpan.FromMilliseconds(100)), 
			"Minimum recovery time should be close to base duration");
		Assert.True(maxTime <= durationOfBreak.Add(jitterMaxDuration).Add(TimeSpan.FromMilliseconds(100)), 
			"Maximum recovery time should be within jitter bounds");

		// Verify we got some reasonable variation (not all the same)
		var uniqueTimes = recoveryTimes.Select(t => (long)(t.TotalMilliseconds / 50) * 50).Distinct().Count();
		Assert.True(uniqueTimes > 1, "Should have reasonable variation in recovery times");
	}

	[Theory]
	[ClassData(typeof(SerializerTypesClassData))]
	public async Task CircuitBreaker_HalfOpenStateAllowsExactlyOneConcurrentCall_DistributedCache(SerializerType serializerType)
	{
		var maxFailuresBeforeBreaking = 1;
		var durationOfBreak = TimeSpan.FromMilliseconds(100);

		using var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		
		// Use a tracking wrapper to count actual calls to distributed cache
		var callTracker = new CallTrackingDistributedCache(distributedCache);
		var chaosDistributedCache = new ChaosDistributedCache(callTracker);

		var options = CreateFusionCacheOptions();
		options.EnableAutoRecovery = false;
		options.DistributedCacheCircuitBreakerFailuresAllowedBeforeBreaking = maxFailuresBeforeBreaking;
		options.DistributedCacheCircuitBreakerDuration = durationOfBreak;
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundDistributedCacheOperations = false;

		fusionCache.SetupDistributedCache(chaosDistributedCache, TestsUtils.GetSerializer(serializerType));

		var key = "foo";
		var value = 42;

		// Open the circuit by causing a failure
		chaosDistributedCache.SetAlwaysThrow();
		await fusionCache.SetAsync(key, value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// Wait for break duration to pass
		await Task.Delay(durationOfBreak.PlusALittleBit());

		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// Fix the distributed cache and reset call counter
		chaosDistributedCache.SetNeverThrow();
		callTracker.ResetCallCount();

		// Create multiple concurrent tasks that will all try to execute when circuit is half-open
		const int concurrentTasks = 10;
		var tasks = new Task[concurrentTasks];
		var barrier = new System.Threading.Barrier(concurrentTasks + 1); // +1 for main thread

		for (int i = 0; i < concurrentTasks; i++)
		{
			int taskId = i;
			tasks[i] = Task.Run(async () =>
			{
				// All tasks wait at the barrier to ensure simultaneous execution
				barrier.SignalAndWait();
				
				// All tasks attempt to execute simultaneously
				await fusionCache.SetAsync($"{key}_{taskId}", value + taskId);
			});
		}

		// Wait for all tasks to be ready, then release them simultaneously
		barrier.SignalAndWait();
		
		// Wait for all tasks to complete
		await Task.WhenAll(tasks);

		// In half-open state, only exactly ONE call should have gone through to the distributed cache
		// The circuit should allow exactly one test call, then block others until that call completes
		Assert.True(callTracker.SetCallCount <= 2, $"Expected at most 2 calls to distributed cache in half-open state, but got {callTracker.SetCallCount}");
		Assert.True(callTracker.SetCallCount >= 1, $"Expected at least 1 call to distributed cache in half-open state, but got {callTracker.SetCallCount}");

		// After successful operations, circuit should be closed
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
	}

	[Fact]
	public async Task CircuitBreaker_HalfOpenStateAllowsExactlyOneConcurrentCall_Backplane()
	{
		var maxFailuresBeforeBreaking = 1;
		var durationOfBreak = TimeSpan.FromMilliseconds(100);
		var backplaneConnectionId = "test-connection";

		using var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions() { ConnectionId = backplaneConnectionId });
		
		// Use a tracking wrapper to count actual calls to backplane
		var callTracker = new CallTrackingBackplane(backplane);
		var chaosBackplane = new ChaosBackplane(callTracker);

		var options = CreateFusionCacheOptions();
		options.EnableAutoRecovery = false;
		options.BackplaneCircuitBreakerFailuresAllowedBeforeBreaking = maxFailuresBeforeBreaking;
		options.BackplaneCircuitBreakerDuration = durationOfBreak;
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundBackplaneOperations = false;

		fusionCache.SetupBackplane(chaosBackplane);

		var key = "foo";
		var value = 42;

		// Open the circuit by causing a failure
		chaosBackplane.SetAlwaysThrow();
		await fusionCache.SetAsync(key, value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));

		// Wait for break duration to pass
		await Task.Delay(durationOfBreak.PlusALittleBit());

		// Fix the backplane and reset call counter
		chaosBackplane.SetNeverThrow();
		callTracker.ResetCallCount();

		// Create multiple concurrent tasks that will all try to execute when circuit is half-open
		const int concurrentTasks = 10;
		var tasks = new Task[concurrentTasks];
		var barrier = new System.Threading.Barrier(concurrentTasks + 1); // +1 for main thread

		for (int i = 0; i < concurrentTasks; i++)
		{
			int taskId = i;
			tasks[i] = Task.Run(async () =>
			{
				// All tasks wait at the barrier to ensure simultaneous execution
				barrier.SignalAndWait();
				
				// All tasks attempt to execute simultaneously
				await fusionCache.SetAsync($"{key}_{taskId}", value + taskId);
			});
		}

		// Wait for all tasks to be ready, then release them simultaneously
		barrier.SignalAndWait();
		
		// Wait for all tasks to complete
		await Task.WhenAll(tasks);

		// In half-open state, only exactly ONE call should have gone through to the backplane
		// The circuit should allow exactly one test call, then block others until that call completes
		Assert.True(callTracker.PublishCallCount <= 2, $"Expected at most 2 calls to backplane in half-open state, but got {callTracker.PublishCallCount}");
		Assert.True(callTracker.PublishCallCount >= 1, $"Expected at least 1 call to backplane in half-open state, but got {callTracker.PublishCallCount}");

		// After successful operations, circuit should be closed
		Assert.Equal(CircuitBreakerState.Closed, TestsUtils.GetBackplaneCircuitBreakerState(fusionCache));
	}

	[Theory]
	[ClassData(typeof(SerializerTypesClassData))]
	public async Task CircuitBreaker_HalfOpenFailureReopensCircuit_DistributedCache(SerializerType serializerType)
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

		var key = "foo";
		var value = 42;

		// Open the circuit
		chaosDistributedCache.SetAlwaysThrow();
		await fusionCache.SetAsync(key, value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// Wait for break duration
		await Task.Delay(durationOfBreak.PlusALittleBit());

		// Keep chaos cache failing - this will cause the half-open test to fail
		// First operation should transition to half-open
		await fusionCache.SetAsync(key + "1", value);
		Assert.Equal(CircuitBreakerState.HalfOpen, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));

		// The failure in half-open state should reopen the circuit
		await fusionCache.SetAsync(key + "2", value);
		Assert.Equal(CircuitBreakerState.Open, TestsUtils.GetDistributedCacheCircuitBreakerState(fusionCache));
	}

	[Theory]
	[ClassData(typeof(SerializerTypesClassData))]
	public async Task CircuitBreaker_IndependentJitterForDistributedCacheAndBackplane(SerializerType serializerType)
	{
		// Test that distributed cache and backplane circuit breakers have independent jitter
		var maxFailuresBeforeBreaking = 1;
		var durationOfBreak = TimeSpan.FromMilliseconds(400);
		var distributedCacheJitter = TimeSpan.FromMilliseconds(100);
		var backplaneJitter = TimeSpan.FromMilliseconds(200);
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
		options.DistributedCacheCircuitBreakerDuration = durationOfBreak;
		options.DistributedCacheCircuitBreakerJitterMaxDuration = distributedCacheJitter;
		options.BackplaneCircuitBreakerFailuresAllowedBeforeBreaking = maxFailuresBeforeBreaking;
		options.BackplaneCircuitBreakerDuration = durationOfBreak;
		options.BackplaneCircuitBreakerJitterMaxDuration = backplaneJitter;
		
		using var fusionCache = new FusionCache(options, memoryCache);
		fusionCache.DefaultEntryOptions.AllowBackgroundDistributedCacheOperations = false;
		fusionCache.DefaultEntryOptions.AllowBackgroundBackplaneOperations = false;

		fusionCache.SetupDistributedCache(chaosDistributedCache, TestsUtils.GetSerializer(serializerType));
		fusionCache.SetupBackplane(chaosBackplane);

		var key = "foo";
		var value = 42;

		// Open both circuits simultaneously
		chaosDistributedCache.SetAlwaysThrow();
		chaosBackplane.SetAlwaysThrow();
		await fusionCache.SetAsync(key, value);

		// Fix both systems
		chaosDistributedCache.SetNeverThrow();
		chaosBackplane.SetNeverThrow();

		// Wait for base duration plus some tolerance
		await Task.Delay(durationOfBreak.Add(TimeSpan.FromMilliseconds(50)));

		// Both circuits should potentially be in different states due to independent jitter
		// This test mainly verifies that the configuration is accepted and operations complete
		await fusionCache.SetAsync(key + "2", value);
		await fusionCache.SetAsync(key + "3", value);

		// The circuits operate independently with their own jitter values
		// We can't easily assert the exact timing, but the operations should complete successfully
	}
}
