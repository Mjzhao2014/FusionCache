using FusionCacheTests.Stuff;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Backplane.Memory;
using ZiggyCreatures.Caching.Fusion.Serialization.NewtonsoftJson;

namespace FusionCacheTests;

public class DependencyBackplaneTests
	: AbstractTests
{
	public DependencyBackplaneTests(ITestOutputHelper output)
		: base(output, null)
	{
	}

	[Fact]
	public async Task BackplanePropagatesDependencyChanges()
	{
		var serializer = new FusionCacheNewtonsoftJsonSerializer();
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions());

		// Create two cache instances sharing the same backplane
		using var cache1 = new FusionCache(new FusionCacheOptions { CacheName = "cache1" })
			.SetupDistributedCache(new TestDistributedCache(), serializer)
			.SetupBackplane(backplane);

		using var cache2 = new FusionCache(new FusionCacheOptions { CacheName = "cache2" })
			.SetupDistributedCache(new TestDistributedCache(), serializer)
			.SetupBackplane(backplane);

		var options = cache1.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Set child on cache1 with dependency
		await cache1.SetAsync("child", 42, options);
		await cache1.SetAsync("parent", "value");

		// Set the same entries on cache2 (simulating distributed scenario)
		await cache2.SetAsync("child", 42, options);
		await cache2.SetAsync("parent", "value");

		// Verify both caches have the child
		Assert.Equal(42, await cache1.GetOrDefaultAsync<int>("child"));
		Assert.Equal(42, await cache2.GetOrDefaultAsync<int>("child"));

		// Change parent on cache1
		await cache1.SetAsync("parent", "changed");

		// Give backplane time to propagate
		await Task.Delay(100);

		// Child should be invalidated on both caches due to backplane notification
		Assert.Equal(0, await cache1.GetOrDefaultAsync<int>("child"));
		Assert.Equal(0, await cache2.GetOrDefaultAsync<int>("child"));
	}

	[Fact]
	public async Task BackplaneHandlesDependencyChangedMessage()
	{
		var serializer = new FusionCacheNewtonsoftJsonSerializer();
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions());

		using var cache = new FusionCache(new FusionCacheOptions())
			.SetupDistributedCache(new TestDistributedCache(), serializer)
			.SetupBackplane(backplane);

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Set child with dependency
		await cache.SetAsync("child", 42, options);

		// Verify child is in cache
		Assert.Equal(42, await cache.GetOrDefaultAsync<int>("child"));

		// Create and send a dependency changed message directly to backplane
		var message = BackplaneMessage.CreateForDependencyChanged("remote-cache-id", "parent", DateTimeOffset.UtcNow.Ticks);
		
		// Simulate receiving the message from backplane
		await backplane.PublishAsync(message, new FusionCacheEntryOptions(), CancellationToken.None);

		// Give backplane time to propagate
		await Task.Delay(100);

		// Child should be invalidated due to dependency change message
		Assert.Equal(0, await cache.GetOrDefaultAsync<int>("child"));
	}

	[Fact]
	public async Task MultipleNodesWithComplexDependencies()
	{
		var serializer = new FusionCacheNewtonsoftJsonSerializer();
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions());

		// Create three cache instances (simulating three nodes)
		using var node1 = new FusionCache(new FusionCacheOptions { CacheName = "node1" })
			.SetupDistributedCache(new TestDistributedCache(), serializer)
			.SetupBackplane(backplane);

		using var node2 = new FusionCache(new FusionCacheOptions { CacheName = "node2" })
			.SetupDistributedCache(new TestDistributedCache(), serializer)
			.SetupBackplane(backplane);

		using var node3 = new FusionCache(new FusionCacheOptions { CacheName = "node3" })
			.SetupDistributedCache(new TestDistributedCache(), serializer)
			.SetupBackplane(backplane);

		// Create complex dependency structure
		var productOptions = node1.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("category", "brand");

		var aggregateOptions = node1.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("product1", "product2");

		// Set up data on different nodes
		await node1.SetAsync("category", "Electronics");
		await node2.SetAsync("brand", "Apple");
		await node3.SetAsync("product1", "iPhone", productOptions);
		
		// Set products and aggregates
		await node1.SetAsync("product2", "iPad", productOptions);
		await node2.SetAsync("sales_report", "Q1: $1M", aggregateOptions);

		// Verify all nodes have the data
		Assert.Equal("iPhone", await node1.GetOrDefaultAsync<string>("product1"));
		Assert.Equal("iPhone", await node2.GetOrDefaultAsync<string>("product1"));
		Assert.Equal("iPhone", await node3.GetOrDefaultAsync<string>("product1"));
		Assert.Equal("Q1: $1M", await node1.GetOrDefaultAsync<string>("sales_report"));
		Assert.Equal("Q1: $1M", await node3.GetOrDefaultAsync<string>("sales_report"));

		// Change category on node2
		await node2.SetAsync("category", "Smartphones");

		// Give backplane time to propagate
		await Task.Delay(200);

		// Both products should be invalidated across all nodes
		Assert.Null(await node1.GetOrDefaultAsync<string>("product1"));
		Assert.Null(await node2.GetOrDefaultAsync<string>("product1"));
		Assert.Null(await node3.GetOrDefaultAsync<string>("product1"));
		Assert.Null(await node1.GetOrDefaultAsync<string>("product2"));
		Assert.Null(await node2.GetOrDefaultAsync<string>("product2"));

		// Sales report should still be there (depends on products, not category)
		Assert.Equal("Q1: $1M", await node1.GetOrDefaultAsync<string>("sales_report"));
		Assert.Equal("Q1: $1M", await node2.GetOrDefaultAsync<string>("sales_report"));
	}

	// Helper class for distributed cache tests
	private class TestDistributedCache : IDistributedCache
	{
		private readonly Dictionary<string, byte[]> _storage = new();

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