using FusionCacheTests.Stuff;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization;
using ZiggyCreatures.Caching.Fusion.Serialization.NewtonsoftJson;

namespace FusionCacheTests;

public partial class DependencyTests
{
	[Fact]
	public async Task CanSetEntryWithDependenciesAsync()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent1", "parent2");

		await cache.SetAsync("child", 42, options);

		var result = await cache.GetOrDefaultAsync<int>("child");
		Assert.Equal(42, result);
	}

	[Fact]
	public async Task DependencyChangeInvalidatesChildAsync()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Set child with dependency on parent
		await cache.SetAsync("child", 42, options);
		
		// Set parent initially
		await cache.SetAsync("parent", "initial");
		
		// Verify child is in cache
		var result = await cache.GetOrDefaultAsync<int>("child");
		Assert.Equal(42, result);

		// Change parent (this is now an update)
		await cache.SetAsync("parent", "changed");

		// Child should be invalidated
		var childResult = await cache.GetOrDefaultAsync<int>("child");
		Assert.Equal(0, childResult); // Default value
	}

	[Fact]
	public async Task DependencyRemovalInvalidatesChildAsync()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Set child with dependency on parent
		await cache.SetAsync("child", 42, options);
		
		// Set parent
		await cache.SetAsync("parent", "value");
		
		// Verify child is in cache
		var result = await cache.GetOrDefaultAsync<int>("child");
		Assert.Equal(42, result);

		// Remove parent
		await cache.RemoveAsync("parent");

		// Child should be invalidated
		var childResult = await cache.GetOrDefaultAsync<int>("child");
		Assert.Equal(0, childResult); // Default value
	}

	[Fact]
	public async Task DependencyExpirationInvalidatesChildAsync()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Set child with dependency on parent
		await cache.SetAsync("child", 42, options);
		
		// Set parent
		await cache.SetAsync("parent", "value");
		
		// Verify child is in cache
		var result = await cache.GetOrDefaultAsync<int>("child");
		Assert.Equal(42, result);

		// Expire parent
		await cache.ExpireAsync("parent");

		// Child should be invalidated
		var childResult = await cache.GetOrDefaultAsync<int>("child");
		Assert.Equal(0, childResult); // Default value
	}

	[Fact]
	public async Task MultipleDependenciesWorkAsync()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent1", "parent2", "parent3");

		// Set child with multiple dependencies
		await cache.SetAsync("child", 42, options);
		
		// Set parents
		await cache.SetAsync("parent1", "value1");
		await cache.SetAsync("parent2", "value2");
		await cache.SetAsync("parent3", "value3");
		
		// Verify child is in cache
		var result = await cache.GetOrDefaultAsync<int>("child");
		Assert.Equal(42, result);

		// Change one parent
		await cache.SetAsync("parent2", "changed");

		// Child should be invalidated
		var childResult = await cache.GetOrDefaultAsync<int>("child");
		Assert.Equal(0, childResult); // Default value
	}

	[Fact]
	public async Task OneDependentMultipleChildrenAsync()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options1 = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		var options2 = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Set children with same dependency
		await cache.SetAsync("child1", 42, options1);
		await cache.SetAsync("child2", 84, options2);
		
		// Set parent
		await cache.SetAsync("parent", "value");
		
		// Verify children are in cache
		Assert.Equal(42, await cache.GetOrDefaultAsync<int>("child1"));
		Assert.Equal(84, await cache.GetOrDefaultAsync<int>("child2"));

		// Change parent
		await cache.SetAsync("parent", "changed");

		// Both children should be invalidated
		Assert.Equal(0, await cache.GetOrDefaultAsync<int>("child1"));
		Assert.Equal(0, await cache.GetOrDefaultAsync<int>("child2"));
	}

	[Fact]
	public async Task DependencyUpdatesClearOldDependenciesAsync()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set child with dependency on parent1
		var options1 = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent1");

		await cache.SetAsync("child", 42, options1);
		await cache.SetAsync("parent1", "value1");
		await cache.SetAsync("parent2", "value2");
		
		// Verify child is in cache
		Assert.Equal(42, await cache.GetOrDefaultAsync<int>("child"));

		// Update child to depend on parent2 instead
		var options2 = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent2");

		await cache.SetAsync("child", 84, options2);
		
		// Verify child updated
		Assert.Equal(84, await cache.GetOrDefaultAsync<int>("child"));

		// Change parent1 - should NOT affect child anymore
		await cache.SetAsync("parent1", "changed1");
		Assert.Equal(84, await cache.GetOrDefaultAsync<int>("child")); // Should still be there

		// Change parent2 - SHOULD affect child
		await cache.SetAsync("parent2", "changed2");
		Assert.Equal(0, await cache.GetOrDefaultAsync<int>("child")); // Should be invalidated
	}

	[Fact]
	public async Task DependenciesWorkWithFactoryAsync()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var callCount = 0;

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Get or set with factory
		var result1 = await cache.GetOrSetAsync<int>("child", async (ctx, ct) => {
			callCount++;
			return 42;
		}, options: options);
		
		Assert.Equal(42, result1);
		Assert.Equal(1, callCount);

		// Should get from cache without calling factory
		var result2 = await cache.GetOrSetAsync<int>("child", async (ctx, ct) => {
			callCount++;
			return 84;
		}, options: options);
		
		Assert.Equal(42, result2);
		Assert.Equal(1, callCount); // Factory not called again

		// Set parent initially
		await cache.SetAsync("parent", "initial");

		// Change parent to invalidate child (this is now an update)
		await cache.SetAsync("parent", "changed");

		// Should call factory again since child was invalidated
		var result3 = await cache.GetOrSetAsync<int>("child", async (ctx, ct) => {
			callCount++;
			return 99;
		}, options: options);
		
		Assert.Equal(99, result3);
		Assert.Equal(2, callCount); // Factory called again
	}

	[Fact]
	public async Task DependenciesWorkWithDistributedCacheAsync()
	{
		var serializer = new FusionCacheNewtonsoftJsonSerializer();
		var distributedCache = new TestDistributedCacheAsync(serializer);

		using var cache = new FusionCache(new FusionCacheOptions())
			.SetupDistributedCache(distributedCache, serializer);

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Set child with dependency
		await cache.SetAsync("child", 42, options);
		await cache.SetAsync("parent", "value");
		
		// Verify child is in both L1 and L2
		Assert.Equal(42, await cache.GetOrDefaultAsync<int>("child"));

		// Change parent
		await cache.SetAsync("parent", "changed");

		// Child should be invalidated in both layers
		Assert.Equal(0, await cache.GetOrDefaultAsync<int>("child"));
	}

	// Helper class for distributed cache tests
	private class TestDistributedCacheAsync : IDistributedCache
	{
		private readonly Dictionary<string, byte[]> _storage = new();
		private readonly IFusionCacheSerializer _serializer;

		public TestDistributedCacheAsync(IFusionCacheSerializer serializer)
		{
			_serializer = serializer;
		}

		public byte[]? Get(string key) => _storage.GetValueOrDefault(key);

		public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
			=> Task.FromResult(Get(key));

		public void Refresh(string key) { }

		public Task RefreshAsync(string key, CancellationToken token = default)
			=> Task.CompletedTask;

		public void Remove(string key) => _storage.Remove(key);

		public Task RemoveAsync(string key, CancellationToken token = default)
		{
			Remove(key);
			return Task.CompletedTask;
		}

		public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
			=> _storage[key] = value;

		public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
		{
			Set(key, value, options);
			return Task.CompletedTask;
		}
	}
}