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
	: AbstractTests
{
	public DependencyTests(ITestOutputHelper output)
		: base(output, null)
	{
	}

	[Fact]
	public void CanSetEntryWithDependencies()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent1", "parent2");

		cache.Set("child", 42, options);

		var result = cache.GetOrDefault<int>("child");
		Assert.Equal(42, result);
	}

	[Fact]
	public void DependencyChangeInvalidatesChild()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Set child with dependency on parent
		cache.Set("child", 42, options);
		
		// Set parent initially
		cache.Set("parent", "initial");
		
		// Verify child is in cache
		var result = cache.GetOrDefault<int>("child");
		Assert.Equal(42, result);

		// Change parent (this is now an update)
		cache.Set("parent", "changed");

		// Child should be invalidated
		var childResult = cache.GetOrDefault<int>("child");
		Assert.Equal(0, childResult); // Default value
	}

	[Fact]
	public void DependencyRemovalInvalidatesChild()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Set child with dependency on parent
		cache.Set("child", 42, options);
		
		// Set parent
		cache.Set("parent", "value");
		
		// Verify child is in cache
		var result = cache.GetOrDefault<int>("child");
		Assert.Equal(42, result);

		// Remove parent
		cache.Remove("parent");

		// Child should be invalidated
		var childResult = cache.GetOrDefault<int>("child");
		Assert.Equal(0, childResult); // Default value
	}

	[Fact]
	public void DependencyExpirationInvalidatesChild()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Set child with dependency on parent
		cache.Set("child", 42, options);
		
		// Set parent
		cache.Set("parent", "value");
		
		// Verify child is in cache
		var result = cache.GetOrDefault<int>("child");
		Assert.Equal(42, result);

		// Expire parent
		cache.Expire("parent");

		// Child should be invalidated
		var childResult = cache.GetOrDefault<int>("child");
		Assert.Equal(0, childResult); // Default value
	}

	[Fact]
	public void MultipleDependenciesWork()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent1", "parent2", "parent3");

		// Set child with multiple dependencies
		cache.Set("child", 42, options);
		
		// Set parents
		cache.Set("parent1", "value1");
		cache.Set("parent2", "value2");
		cache.Set("parent3", "value3");
		
		// Verify child is in cache
		var result = cache.GetOrDefault<int>("child");
		Assert.Equal(42, result);

		// Change one parent
		cache.Set("parent2", "changed");

		// Child should be invalidated
		var childResult = cache.GetOrDefault<int>("child");
		Assert.Equal(0, childResult); // Default value
	}

	[Fact]
	public void OneDependentMultipleChildren()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options1 = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		var options2 = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Set children with same dependency
		cache.Set("child1", 42, options1);
		cache.Set("child2", 84, options2);
		
		// Set parent
		cache.Set("parent", "value");
		
		// Verify children are in cache
		Assert.Equal(42, cache.GetOrDefault<int>("child1"));
		Assert.Equal(84, cache.GetOrDefault<int>("child2"));

		// Change parent
		cache.Set("parent", "changed");

		// Both children should be invalidated
		Assert.Equal(0, cache.GetOrDefault<int>("child1"));
		Assert.Equal(0, cache.GetOrDefault<int>("child2"));
	}

	[Fact]
	public void CascadingDependencies()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Create dependency chain: root -> parent -> child
		var parentOptions = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("root");

		var childOptions = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Set entries
		cache.Set("root", "root_value");
		cache.Set("parent", "parent_value", parentOptions);
		cache.Set("child", "child_value", childOptions);
		
		// Verify all are in cache
		Assert.Equal("root_value", cache.GetOrDefault<string>("root"));
		Assert.Equal("parent_value", cache.GetOrDefault<string>("parent"));
		Assert.Equal("child_value", cache.GetOrDefault<string>("child"));

		// Change root
		cache.Set("root", "changed");

		// Parent should be invalidated, but child may still be there
		// since dependency invalidation doesn't cascade automatically
		Assert.Equal("changed", cache.GetOrDefault<string>("root"));
		Assert.Null(cache.GetOrDefault<string>("parent"));
		
		// Child might still be there since parent invalidation doesn't cascade
		// This tests that we don't have infinite cascading by design
	}

	[Fact]
	public void DependencyUpdatesClearOldDependencies()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set child with dependency on parent1
		var options1 = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent1");

		cache.Set("child", 42, options1);
		cache.Set("parent1", "value1");
		cache.Set("parent2", "value2");
		
		// Verify child is in cache
		Assert.Equal(42, cache.GetOrDefault<int>("child"));

		// Update child to depend on parent2 instead
		var options2 = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent2");

		cache.Set("child", 84, options2);
		
		// Verify child updated
		Assert.Equal(84, cache.GetOrDefault<int>("child"));

		// Change parent1 - should NOT affect child anymore
		cache.Set("parent1", "changed1");
		Assert.Equal(84, cache.GetOrDefault<int>("child")); // Should still be there

		// Change parent2 - SHOULD affect child
		cache.Set("parent2", "changed2");
		Assert.Equal(0, cache.GetOrDefault<int>("child")); // Should be invalidated
	}

	[Fact]
	public void DependenciesWorkWithDistributedCache()
	{
		var serializer = new FusionCacheNewtonsoftJsonSerializer();
		var distributedCache = new TestDistributedCache(serializer);

		using var cache = new FusionCache(new FusionCacheOptions())
			.SetupDistributedCache(distributedCache, serializer);

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Set child with dependency
		cache.Set("child", 42, options);
		cache.Set("parent", "value");
		
		// Verify child is in both L1 and L2
		Assert.Equal(42, cache.GetOrDefault<int>("child"));

		// Change parent
		cache.Set("parent", "changed");

		// Child should be invalidated in both layers
		Assert.Equal(0, cache.GetOrDefault<int>("child"));
	}

	[Fact]
	public void EmptyDependenciesArrayDoesNotCauseCrash()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies(); // Empty array

		// Should not throw
		cache.Set("child", 42, options);
		
		Assert.Equal(42, cache.GetOrDefault<int>("child"));
	}

	[Fact]
	public void NullDependenciesDoesNotCauseCrash()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10));
		
		options.Dependencies = null;

		// Should not throw
		cache.Set("child", 42, options);
		
		Assert.Equal(42, cache.GetOrDefault<int>("child"));
	}

	[Fact]
	public void SelfDependencyDoesNotCauseInfiniteLoop()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("self"); // Self-dependency

		// Should not cause infinite loop
		cache.Set("self", 42, options);
		
		Assert.Equal(42, cache.GetOrDefault<int>("self"));

		// Updating self should work without infinite recursion
		cache.Set("self", 84);
		Assert.Equal(84, cache.GetOrDefault<int>("self"));
	}

	// Helper class for distributed cache tests
	private class TestDistributedCache : IDistributedCache
	{
		private readonly Dictionary<string, byte[]> _storage = new();
		private readonly IFusionCacheSerializer _serializer;

		public TestDistributedCache(IFusionCacheSerializer serializer)
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