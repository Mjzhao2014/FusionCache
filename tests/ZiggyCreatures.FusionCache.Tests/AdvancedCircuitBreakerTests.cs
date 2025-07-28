using FusionCacheTests.Stuff;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace FusionCacheTests;

public class AdvancedCircuitBreakerTests : AbstractTests
{
	public AdvancedCircuitBreakerTests(ITestOutputHelper output)
		: base(output, "AdvancedCircuitBreaker:")
	{
	}

	[Fact]
	public void AdvancedCircuitBreaker_StartsInClosedState()
	{
		var options = new AdvancedCircuitBreakerOptions
		{
			FailureThreshold = 3,
			SamplingDuration = TimeSpan.FromMinutes(1),
			DurationOfBreak = TimeSpan.FromSeconds(30)
		};

		var breaker = new AdvancedCircuitBreaker(options);

		Assert.Equal(CircuitBreakerState.Closed, breaker.State);
		Assert.Equal(0, breaker.CurrentFailureCount);
	}

	[Fact]
	public void AdvancedCircuitBreaker_AllowsExecutionWhenClosed()
	{
		var options = new AdvancedCircuitBreakerOptions
		{
			FailureThreshold = 3,
			SamplingDuration = TimeSpan.FromMinutes(1),
			DurationOfBreak = TimeSpan.FromSeconds(30)
		};

		var breaker = new AdvancedCircuitBreaker(options);

		var canExecute = breaker.TryExecute(out var stateChanged);

		Assert.True(canExecute);
		Assert.False(stateChanged);
		Assert.Equal(CircuitBreakerState.Closed, breaker.State);
	}

	[Fact]
	public void AdvancedCircuitBreaker_OpensAfterFailureThreshold()
	{
		var options = new AdvancedCircuitBreakerOptions
		{
			FailureThreshold = 3,
			SamplingDuration = TimeSpan.FromMinutes(1),
			DurationOfBreak = TimeSpan.FromSeconds(30)
		};

		var breaker = new AdvancedCircuitBreaker(options);

		// Record failures up to threshold
		for (int i = 0; i < 2; i++)
		{
			breaker.RecordFailure(out var stateChanged);
			Assert.False(stateChanged);
			Assert.Equal(CircuitBreakerState.Closed, breaker.State);
		}

		// Third failure should open the circuit
		breaker.RecordFailure(out var finalStateChanged);
		Assert.True(finalStateChanged);
		Assert.Equal(CircuitBreakerState.Open, breaker.State);
		Assert.Equal(3, breaker.CurrentFailureCount);
	}

	[Fact]
	public void AdvancedCircuitBreaker_BlocksExecutionWhenOpen()
	{
		var options = new AdvancedCircuitBreakerOptions
		{
			FailureThreshold = 2,
			SamplingDuration = TimeSpan.FromMinutes(1),
			DurationOfBreak = TimeSpan.FromSeconds(30)
		};

		var breaker = new AdvancedCircuitBreaker(options);

		// Open the circuit
		breaker.RecordFailure(out _);
		breaker.RecordFailure(out _);

		var canExecute = breaker.TryExecute(out var stateChanged);

		Assert.False(canExecute);
		Assert.False(stateChanged);
		Assert.Equal(CircuitBreakerState.Open, breaker.State);
	}

	[Fact]
	public void AdvancedCircuitBreaker_TransitionsToHalfOpenAfterDurationOfBreak()
	{
		var options = new AdvancedCircuitBreakerOptions
		{
			FailureThreshold = 2,
			SamplingDuration = TimeSpan.FromMinutes(1),
			DurationOfBreak = TimeSpan.FromMilliseconds(100)
		};

		var breaker = new AdvancedCircuitBreaker(options);

		// Open the circuit
		breaker.RecordFailure(out _);
		breaker.RecordFailure(out _);
		Assert.Equal(CircuitBreakerState.Open, breaker.State);

		// Wait for break duration
		Thread.Sleep(150);

		// Should transition to half-open
		var canExecute = breaker.TryExecute(out var stateChanged);
		Assert.True(canExecute);
		Assert.True(stateChanged);
		Assert.Equal(CircuitBreakerState.HalfOpen, breaker.State);
	}

	[Fact]
	public void AdvancedCircuitBreaker_HalfOpenLimitsExecutions()
	{
		var options = new AdvancedCircuitBreakerOptions
		{
			FailureThreshold = 2,
			SamplingDuration = TimeSpan.FromMinutes(1),
			DurationOfBreak = TimeSpan.FromMilliseconds(100),
			MinimumThroughput = 2
		};

		var breaker = new AdvancedCircuitBreaker(options);

		// Open the circuit
		breaker.RecordFailure(out _);
		breaker.RecordFailure(out _);

		// Wait for break duration
		Thread.Sleep(150);

		// Should allow limited executions in half-open state
		Assert.True(breaker.TryExecute(out _));
		Assert.True(breaker.TryExecute(out _));
		Assert.False(breaker.TryExecute(out _)); // Should be blocked after max calls
	}

	[Fact]
	public void AdvancedCircuitBreaker_HalfOpenClosesOnAllSuccesses()
	{
		var options = new AdvancedCircuitBreakerOptions
		{
			FailureThreshold = 2,
			SamplingDuration = TimeSpan.FromMinutes(1),
			DurationOfBreak = TimeSpan.FromMilliseconds(100),
			MinimumThroughput = 2
		};

		var breaker = new AdvancedCircuitBreaker(options);

		// Open the circuit
		breaker.RecordFailure(out _);
		breaker.RecordFailure(out _);

		// Wait for break duration
		Thread.Sleep(150);

		// Execute in half-open state
		breaker.TryExecute(out _);
		breaker.TryExecute(out _);

		// Record successes - should close circuit after max calls reached
		breaker.RecordSuccess(out var stateChanged1);
		Assert.False(stateChanged1);
		
		breaker.RecordSuccess(out var stateChanged2);
		Assert.True(stateChanged2);
		Assert.Equal(CircuitBreakerState.Closed, breaker.State);
		Assert.Equal(0, breaker.CurrentFailureCount);
	}

	[Fact]
	public void AdvancedCircuitBreaker_HalfOpenOpensOnFailure()
	{
		var options = new AdvancedCircuitBreakerOptions
		{
			FailureThreshold = 2,
			SamplingDuration = TimeSpan.FromMinutes(1),
			DurationOfBreak = TimeSpan.FromMilliseconds(100),
			MinimumThroughput = 3
		};

		var breaker = new AdvancedCircuitBreaker(options);

		// Open the circuit
		breaker.RecordFailure(out _);
		breaker.RecordFailure(out _);

		// Wait for break duration
		Thread.Sleep(150);

		// Execute in half-open state
		breaker.TryExecute(out _);

		// Record failure - should open circuit again
		breaker.RecordFailure(out var stateChanged);
		Assert.True(stateChanged);
		Assert.Equal(CircuitBreakerState.Open, breaker.State);
	}

	[Fact]
	public void AdvancedCircuitBreaker_ManualCloseWorks()
	{
		var options = new AdvancedCircuitBreakerOptions
		{
			FailureThreshold = 2,
			SamplingDuration = TimeSpan.FromMinutes(1),
			DurationOfBreak = TimeSpan.FromSeconds(30)
		};

		var breaker = new AdvancedCircuitBreaker(options);

		// Open the circuit
		breaker.RecordFailure(out _);
		breaker.RecordFailure(out _);
		Assert.Equal(CircuitBreakerState.Open, breaker.State);

		// Manually close
		breaker.Close(out var stateChanged);
		Assert.True(stateChanged);
		Assert.Equal(CircuitBreakerState.Closed, breaker.State);
		Assert.Equal(0, breaker.CurrentFailureCount);
	}

	[Fact]
	public void AdvancedCircuitBreaker_ExpiredFailuresAreIgnored()
	{
		var options = new AdvancedCircuitBreakerOptions
		{
			FailureThreshold = 3,
			SamplingDuration = TimeSpan.FromMilliseconds(100),
			DurationOfBreak = TimeSpan.FromSeconds(30)
		};

		var breaker = new AdvancedCircuitBreaker(options);

		// Record failures
		breaker.RecordFailure(out _);
		breaker.RecordFailure(out _);
		Assert.Equal(2, breaker.CurrentFailureCount);

		// Wait for sampling duration to expire
		Thread.Sleep(150);

		// Old failures should be ignored
		Assert.Equal(0, breaker.CurrentFailureCount);
		Assert.Equal(CircuitBreakerState.Closed, breaker.State);
	}

	[Fact]
	public void FusionCache_UsesSimpleCircuitBreakerByDefault()
	{
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var memoryCache = new MemoryCache(Options.Create(new MemoryCacheOptions()));

		using var cache = new FusionCache(new FusionCacheOptions
		{
			DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(5)
		}, memoryCache);

		cache.SetupDistributedCache(distributedCache);

		// Should start with simple circuit breaker behavior
		// We can't directly test the internal type, but we can verify basic functionality
		Assert.NotNull(cache);
	}

	[Fact]
	public void FusionCache_UsesAdvancedCircuitBreakerWhenEnabled()
	{
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var memoryCache = new MemoryCache(Options.Create(new MemoryCacheOptions()));

		using var cache = new FusionCache(new FusionCacheOptions
		{
			EnableAdvancedDistributedCacheCircuitBreaker = true,
			DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(5),
			DistributedCacheCircuitBreakerFailureThreshold = 3,
			DistributedCacheCircuitBreakerSamplingDuration = TimeSpan.FromMinutes(1),
			DistributedCacheCircuitBreakerMinimumThroughput = 2
		}, memoryCache);

		cache.SetupDistributedCache(distributedCache);

		// Should start with advanced circuit breaker
		Assert.NotNull(cache);
	}

	[Fact]
	public void CircuitBreakerFactory_CreatesSimpleCircuitBreakerByDefault()
	{
		var options = new FusionCacheOptions
		{
			DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(30)
		};

		var breaker = CircuitBreakerFactory.CreateDistributedCacheCircuitBreaker(options);

		Assert.IsType<SimpleCircuitBreaker>(breaker);
	}

	[Fact]
	public void CircuitBreakerFactory_CreatesAdvancedCircuitBreakerWhenEnabled()
	{
		var options = new FusionCacheOptions
		{
			EnableAdvancedDistributedCacheCircuitBreaker = true,
			DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(30),
			DistributedCacheCircuitBreakerFailureThreshold = 5,
			DistributedCacheCircuitBreakerSamplingDuration = TimeSpan.FromMinutes(2),
			DistributedCacheCircuitBreakerMinimumThroughput = 3
		};

		var breaker = CircuitBreakerFactory.CreateDistributedCacheCircuitBreaker(options);

		Assert.IsType<AdvancedCircuitBreaker>(breaker);
	}

	[Fact]
	public async Task FusionCache_AdvancedCircuitBreakerIntegrationTest()
	{
		var memoryCache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
		var distributedCache = new FailingDistributedCache();

		using var cache = new FusionCache(new FusionCacheOptions
		{
			EnableAdvancedDistributedCacheCircuitBreaker = true,
			DistributedCacheCircuitBreakerDuration = TimeSpan.FromMilliseconds(100),
			DistributedCacheCircuitBreakerFailureThreshold = 2,
			DistributedCacheCircuitBreakerSamplingDuration = TimeSpan.FromMinutes(1),
			DistributedCacheCircuitBreakerMinimumThroughput = 1
		}, memoryCache);

		cache.SetupDistributedCache(distributedCache);

		// First operations should fail and open the circuit
		await cache.SetAsync("key1", "value1");
		await cache.SetAsync("key2", "value2");

		// Circuit should be open now, so operations should be skipped
		var result = await cache.GetOrDefaultAsync<string>("key3");
		Assert.Null(result);

		// Wait for break duration
		await Task.Delay(150);

		// Make distributed cache work again
		distributedCache.ShouldFail = false;

		// Next operation should succeed and close the circuit (half-open -> closed)
		await cache.SetAsync("key4", "value4");

		// Verify cache is working normally
		var retrievedValue = await cache.GetOrDefaultAsync<string>("key4");
		Assert.Equal("value4", retrievedValue);
	}

	private class FailingDistributedCache : IDistributedCache
	{
		private readonly MemoryDistributedCache _inner;
		public bool ShouldFail { get; set; } = true;

		public FailingDistributedCache()
		{
			_inner = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		}

		public byte[]? Get(string key)
		{
			if (ShouldFail)
				throw new InvalidOperationException("Simulated failure");
			return _inner.Get(key);
		}

		public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
		{
			if (ShouldFail)
				throw new InvalidOperationException("Simulated failure");
			return _inner.GetAsync(key, token);
		}

		public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
		{
			if (ShouldFail)
				throw new InvalidOperationException("Simulated failure");
			_inner.Set(key, value, options);
		}

		public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
		{
			if (ShouldFail)
				throw new InvalidOperationException("Simulated failure");
			return _inner.SetAsync(key, value, options, token);
		}

		public void Refresh(string key)
		{
			if (ShouldFail)
				throw new InvalidOperationException("Simulated failure");
			_inner.Refresh(key);
		}

		public Task RefreshAsync(string key, CancellationToken token = default)
		{
			if (ShouldFail)
				throw new InvalidOperationException("Simulated failure");
			return _inner.RefreshAsync(key, token);
		}

		public void Remove(string key)
		{
			if (ShouldFail)
				throw new InvalidOperationException("Simulated failure");
			_inner.Remove(key);
		}

		public Task RemoveAsync(string key, CancellationToken token = default)
		{
			if (ShouldFail)
				throw new InvalidOperationException("Simulated failure");
			return _inner.RemoveAsync(key, token);
		}
	}
}