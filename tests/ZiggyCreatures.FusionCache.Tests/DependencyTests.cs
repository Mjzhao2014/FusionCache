using FusionCacheTests.Stuff;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Xunit;
using Xunit.Abstractions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.Memory;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace FusionCacheTests;

public class DependencyTests : AbstractTests
{
	public DependencyTests(ITestOutputHelper output)
		: base(output, null)
	{
	}
	[Fact]
	public void BasicKeyDependency_InvalidatesChildWhenParentChanges()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set parent value
		cache.Set("parent:1", "parent-value");

		// Set child with dependency on parent
		cache.Set("child:1", "child-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("parent:1")));

		// Verify both exist
		Assert.Equal("parent-value", cache.GetOrDefault<string>("parent:1"));
		Assert.Equal("child-value", cache.GetOrDefault<string>("child:1"));

		// Update parent (this should invalidate child)
		cache.Set("parent:1", "new-parent-value");

		// Parent should have new value, child should be gone
		Assert.Equal("new-parent-value", cache.GetOrDefault<string>("parent:1"));
		Assert.Null(cache.GetOrDefault<string>("child:1"));
	}

	[Fact]
	public void MultipleDependencies_InvalidatesOnAnyParentChange()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set multiple parents
		cache.Set("parent:1", "value1");
		cache.Set("parent:2", "value2");

		// Set child depending on multiple parents
		cache.Set("child:complex", "complex-child", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("parent:1", "parent:2")));

		// Verify child exists
		Assert.Equal("complex-child", cache.GetOrDefault<string>("child:complex"));

		// Update first parent - should invalidate child
		cache.Set("parent:1", "new-value1");
		Assert.Null(cache.GetOrDefault<string>("child:complex"));

		// Re-add child
		cache.Set("child:complex", "complex-child", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("parent:1", "parent:2")));

		// Update second parent - should invalidate child again
		cache.Set("parent:2", "new-value2");
		Assert.Null(cache.GetOrDefault<string>("child:complex"));

		// Re-add child one more time
		cache.Set("child:complex", "complex-child", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("parent:1", "parent:2")));

	}

	[Fact]
	public void ChainedDependencies_CascadesThroughMultipleLevels()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set up chain: grandparent -> parent -> child
		cache.Set("grandparent", "gp-value");

		cache.Set("parent", "parent-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("grandparent")));

		cache.Set("child", "child-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("parent")));

		// Verify all exist
		Assert.Equal("gp-value", cache.GetOrDefault<string>("grandparent"));
		Assert.Equal("parent-value", cache.GetOrDefault<string>("parent"));
		Assert.Equal("child-value", cache.GetOrDefault<string>("child"));

		// Update grandparent - should cascade to both parent and child
		cache.Set("grandparent", "new-gp-value");

		// Grandparent should have new value, others should be gone
		Assert.Equal("new-gp-value", cache.GetOrDefault<string>("grandparent"));
		Assert.Null(cache.GetOrDefault<string>("parent"));
		Assert.Null(cache.GetOrDefault<string>("child"));
	}

	[Fact]
	public async Task AsyncDependency_WorksCorrectly()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set parent value
		await cache.SetAsync("async-parent", "parent-value");

		// Set child with dependency
		await cache.SetAsync("async-child", "child-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("async-parent")));

		// Verify both exist
		Assert.Equal("parent-value", await cache.GetOrDefaultAsync<string>("async-parent"));
		Assert.Equal("child-value", await cache.GetOrDefaultAsync<string>("async-child"));

		// Update parent async - should invalidate child
		await cache.SetAsync("async-parent", "new-parent-value");

		// Parent should have new value, child should be gone
		Assert.Equal("new-parent-value", await cache.GetOrDefaultAsync<string>("async-parent"));
		Assert.Null(await cache.GetOrDefaultAsync<string>("async-child"));
	}

	[Fact]
	public void GetOrSetWithDependencies_RegistersDependenciesCorrectly()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set parent
		cache.Set("parent:config", "config-value");

		// SCENARIO 1: Fresh insert - GetOrSet creates new entry with dependencies
		var result1 = cache.GetOrSet("derived:config", 
			_ => "derived-config-value",
			options => options
				.SetDuration(TimeSpan.FromMinutes(5))
				.WithDependencies(DependsOn.Keys("parent:config")));

		Assert.Equal("derived-config-value", result1);

		// Verify dependency works for fresh insert
		cache.Set("parent:config", "new-config-value");
		Assert.Null(cache.GetOrDefault<string>("derived:config"));

		// SCENARIO 2: Cache hit - GetOrSet on existing entry should still register dependencies
		// First, re-add the entries without dependencies
		cache.Set("parent:config2", "config2-value");
		cache.Set("derived:config2", "existing-value"); // Pre-existing entry without dependencies

		// Verify the entry exists and has no dependencies initially
		Assert.Equal("existing-value", cache.GetOrDefault<string>("derived:config2"));

		// Now call GetOrSet on the existing entry with dependencies
		var result2 = cache.GetOrSet("derived:config2",
			_ => "should-not-be-called", // Factory should not be called since entry exists
			options => options
				.SetDuration(TimeSpan.FromMinutes(5))
				.WithDependencies(DependsOn.Keys("parent:config2")));

		// Should return existing value (factory not called)
		Assert.Equal("existing-value", result2);

		// CRITICAL TEST: Verify dependency was registered even on cache hit
		cache.Set("parent:config2", "updated-config2-value");
		Assert.Null(cache.GetOrDefault<string>("derived:config2")); // Should be invalidated if dependency was registered

		// SCENARIO 3: Test dependency update on existing entry
		cache.Set("parent:config3a", "config3a-value");
		cache.Set("parent:config3b", "config3b-value");
		
		// Create entry with initial dependency
		var result3a = cache.GetOrSet("derived:config3",
			_ => "derived-config3-value",
			options => options
				.SetDuration(TimeSpan.FromMinutes(5))
				.WithDependencies(DependsOn.Keys("parent:config3a")));

		Assert.Equal("derived-config3-value", result3a);

		// Update dependencies on existing entry (should replace old dependencies)
		var result3b = cache.GetOrSet("derived:config3",
			_ => "should-not-be-called",
			options => options
				.SetDuration(TimeSpan.FromMinutes(5))
				.WithDependencies(DependsOn.Keys("parent:config3b"))); // Different parent

		Assert.Equal("derived-config3-value", result3b); // Same value returned

		// Old dependency should no longer work
		cache.Set("parent:config3a", "updated-config3a-value");
		Assert.NotNull(cache.GetOrDefault<string>("derived:config3")); // Should NOT be invalidated

		// New dependency should work
		cache.Set("parent:config3b", "updated-config3b-value");
		Assert.Null(cache.GetOrDefault<string>("derived:config3")); // Should be invalidated
	}

	[Fact]
	public async Task GetOrSetAsyncWithDependencies_RegistersDependenciesCorrectly()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set parent
		await cache.SetAsync("parent:settings", "settings-value");

		// SCENARIO 1: Fresh insert - GetOrSetAsync creates new entry with dependencies
		var result1 = await cache.GetOrSetAsync("derived:settings",
			async _ => {
				await Task.Delay(10); // Simulate async work
				return "derived-settings-value";
			},
			options => options
				.SetDuration(TimeSpan.FromMinutes(5))
				.WithDependencies(DependsOn.Keys("parent:settings")));

		Assert.Equal("derived-settings-value", result1);

		// Verify dependency works for fresh insert
		await cache.SetAsync("parent:settings", "new-settings-value");
		Assert.Null(await cache.GetOrDefaultAsync<string>("derived:settings"));

		// SCENARIO 2: Cache hit - GetOrSetAsync on existing entry should still register dependencies
		// First, re-add the entries without dependencies
		await cache.SetAsync("parent:settings2", "settings2-value");
		await cache.SetAsync("derived:settings2", "existing-settings-value"); // Pre-existing entry without dependencies

		// Verify the entry exists and has no dependencies initially
		Assert.Equal("existing-settings-value", await cache.GetOrDefaultAsync<string>("derived:settings2"));

		// Now call GetOrSetAsync on the existing entry with dependencies
		var result2 = await cache.GetOrSetAsync("derived:settings2",
			async _ => {
				await Task.Delay(10);
				return "should-not-be-called"; // Factory should not be called since entry exists
			},
			options => options
				.SetDuration(TimeSpan.FromMinutes(5))
				.WithDependencies(DependsOn.Keys("parent:settings2")));

		// Should return existing value (factory not called)
		Assert.Equal("existing-settings-value", result2);

		// CRITICAL TEST: Verify dependency was registered even on cache hit
		await cache.SetAsync("parent:settings2", "updated-settings2-value");
		Assert.Null(await cache.GetOrDefaultAsync<string>("derived:settings2")); // Should be invalidated if dependency was registered

		// SCENARIO 3: Test dependency update on existing entry
		await cache.SetAsync("parent:settings3a", "settings3a-value");
		await cache.SetAsync("parent:settings3b", "settings3b-value");
		
		// Create entry with initial dependency
		var result3a = await cache.GetOrSetAsync("derived:settings3",
			async _ => {
				await Task.Delay(10);
				return "derived-settings3-value";
			},
			options => options
				.SetDuration(TimeSpan.FromMinutes(5))
				.WithDependencies(DependsOn.Keys("parent:settings3a")));

		Assert.Equal("derived-settings3-value", result3a);

		// Update dependencies on existing entry (should replace old dependencies)
		var result3b = await cache.GetOrSetAsync("derived:settings3",
			async _ => {
				await Task.Delay(10);
				return "should-not-be-called";
			},
			options => options
				.SetDuration(TimeSpan.FromMinutes(5))
				.WithDependencies(DependsOn.Keys("parent:settings3b"))); // Different parent

		Assert.Equal("derived-settings3-value", result3b); // Same value returned

		// Old dependency should no longer work
		await cache.SetAsync("parent:settings3a", "updated-settings3a-value");
		Assert.NotNull(await cache.GetOrDefaultAsync<string>("derived:settings3")); // Should NOT be invalidated

		// New dependency should work
		await cache.SetAsync("parent:settings3b", "updated-settings3b-value");
		Assert.Null(await cache.GetOrDefaultAsync<string>("derived:settings3")); // Should be invalidated
	}

	[Fact]
	public void RemoveEntry_RemovesDependenciesAndCascades_CleansDependencyGraphAndPreventsStaleReferences()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set up initial dependency: parent -> child
		cache.Set("parent", "parent-value");
		cache.Set("child", "child-value", options => options
			.WithDependencies(DependsOn.Keys("parent")));

		// Verify initial state
		Assert.Equal("parent-value", cache.GetOrDefault<string>("parent"));
		Assert.Equal("child-value", cache.GetOrDefault<string>("child"));

		// Remove parent - this should cascade to child AND clean up dependency graph
		cache.Remove("parent");

		// Both should be gone
		Assert.Null(cache.GetOrDefault<string>("parent"));
		Assert.Null(cache.GetOrDefault<string>("child"));

		// Now re-add the same keys WITHOUT dependencies
		cache.Set("parent", "new-parent-value");
		cache.Set("child", "new-child-value");

		// Verify they exist
		Assert.Equal("new-parent-value", cache.GetOrDefault<string>("parent"));
		Assert.Equal("new-child-value", cache.GetOrDefault<string>("child"));

		// Update parent again - this should NOT affect child since dependency was cleaned up
		cache.Set("parent", "updated-parent-value");

		// Child should still exist (proving no stale dependency)
		Assert.Equal("updated-parent-value", cache.GetOrDefault<string>("parent"));
		Assert.Equal("new-child-value", cache.GetOrDefault<string>("child"));
	}

	[Fact]
	public void CascadeDepthLimit_PreventsInfiniteRecursion()
	{
		var options = new FusionCacheOptions();
		options.Cascade.MaxCascadeDepth = 2;
		
		using var cache = new FusionCache(options);

		// Set up deep chain: level0 -> level1 -> level2 -> level3 -> level4
		cache.Set("level0", "value0");
		
		for (int i = 1; i <= 4; i++)
		{
			cache.Set($"level{i}", $"value{i}", options => options
				.WithDependencies(DependsOn.Keys($"level{i-1}")));
		}

		// Verify all exist
		for (int i = 0; i <= 4; i++)
		{
			Assert.Equal($"value{i}", cache.GetOrDefault<string>($"level{i}"));
		}

		// Update root - should only cascade to depth limit
		cache.Set("level0", "new-value0");

		// Level0 should be updated, level1 and level2 should be removed due to cascade
		// level3 and level4 should still exist due to depth limit
		Assert.Equal("new-value0", cache.GetOrDefault<string>("level0"));
		Assert.Null(cache.GetOrDefault<string>("level1"));
		Assert.Null(cache.GetOrDefault<string>("level2"));
		// These should still exist due to depth limit
		Assert.Equal("value3", cache.GetOrDefault<string>("level3"));
		Assert.Equal("value4", cache.GetOrDefault<string>("level4"));
	}

	[Fact]
	public void BackplanePropagation_PropagatesCascadeInvalidation()
	{
		var memoryBackplane = new MemoryBackplane(Options.Create(new MemoryBackplaneOptions()), null);
		
		// Create two cache instances sharing the same backplane
		var cache1 = new FusionCache(new FusionCacheOptions { CacheName = "test-cache-1" });
		cache1.SetupBackplane(memoryBackplane);

		var cache2 = new FusionCache(new FusionCacheOptions { CacheName = "test-cache-2" });
		cache2.SetupBackplane(memoryBackplane);

		// Set parent in cache1
		cache1.Set("shared-parent", "parent-value");

		// Set child with dependency in cache2
		cache2.Set("shared-child", "child-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("shared-parent")));

		// Verify both caches have their entries
		Assert.Equal("parent-value", cache1.GetOrDefault<string>("shared-parent"));
		Assert.Equal("child-value", cache2.GetOrDefault<string>("shared-child"));

		// Update parent in cache1 - should propagate invalidation to cache2
		cache1.Set("shared-parent", "new-parent-value");

		// Give some time for backplane propagation
		Thread.Sleep(200);

		// Child in cache2 should be invalidated
		Assert.Null(cache2.GetOrDefault<string>("shared-child"));

	}

	[Fact]
	public async Task BackplanePropagationAsync_PropagatesCascadeInvalidation()
	{
		var memoryBackplane = new MemoryBackplane(Options.Create(new MemoryBackplaneOptions()), null);
		
		// Create two cache instances sharing the same backplane
		var cache1 = new FusionCache(new FusionCacheOptions { CacheName = "test-cache-1" });
		cache1.SetupBackplane(memoryBackplane);

		var cache2 = new FusionCache(new FusionCacheOptions { CacheName = "test-cache-2" });
		cache2.SetupBackplane(memoryBackplane);

		// Set parent in cache1
		await cache1.SetAsync("async-shared-parent", "parent-value");

		// Set child with dependency in cache2
		await cache2.SetAsync("async-shared-child", "child-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("async-shared-parent")));

		// Verify both caches have their entries
		Assert.Equal("parent-value", await cache1.GetOrDefaultAsync<string>("async-shared-parent"));
		Assert.Equal("child-value", await cache2.GetOrDefaultAsync<string>("async-shared-child"));

		// Update parent in cache1 - should propagate invalidation to cache2
		await cache1.SetAsync("async-shared-parent", "new-parent-value");

		// Give some time for backplane propagation
		await Task.Delay(200);

		// Child in cache2 should be invalidated
		Assert.Null(await cache2.GetOrDefaultAsync<string>("async-shared-child"));
	}

	[Fact]
	public void NoDependencies_DoesNotAffectNormalOperation()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Normal cache operations without dependencies should work as before
		cache.Set("normal:1", "value1");
		cache.Set("normal:2", "value2");

		Assert.Equal("value1", cache.GetOrDefault<string>("normal:1"));
		Assert.Equal("value2", cache.GetOrDefault<string>("normal:2"));

		// Updating one should not affect the other
		cache.Set("normal:1", "new-value1");

		Assert.Equal("new-value1", cache.GetOrDefault<string>("normal:1"));
		Assert.Equal("value2", cache.GetOrDefault<string>("normal:2"));
	}

	[Fact]
	public void DependencyTracking_WorksWithExpiration_Child()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set parent
		cache.Set("expire-parent", "parent-value");

		// Set child with dependency and short expiration
		cache.Set("expire-child", "child-value", options => options
			.SetDuration(TimeSpan.FromMilliseconds(100))
			.WithDependencies(DependsOn.Keys("expire-parent")));

		// Wait for natural expiration
		Thread.Sleep(200);

		// Child should be naturally expired
		Assert.Null(cache.GetOrDefault<string>("expire-child"));

		// Parent still exists
		Assert.Equal("parent-value", cache.GetOrDefault<string>("expire-parent"));

		// Updating parent should not cause issues even though child is already expired
		cache.Set("expire-parent", "new-parent-value");
		Assert.Equal("new-parent-value", cache.GetOrDefault<string>("expire-parent"));
	}

	[Fact]
	public void DependencyTracking_WorksWithExpiration_Parent()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set parent with very short expiration
		cache.Set("expire-parent", "parent-value", options => options
			.SetDuration(TimeSpan.FromMilliseconds(100)));

		// Set child with dependency and longer duration
		cache.Set("expire-child", "child-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("expire-parent")));

		// Wait for parent's natural expiration
		Thread.Sleep(500);

		// Parent should be naturally expired
		Assert.Null(cache.GetOrDefault<string>("expire-parent"));

		// Child will be invalidated
		Assert.Null(cache.GetOrDefault<string>("expire-child"));

		// Updating parent should not cause issues even though child is already expired
		cache.Set("expire-parent", "new-parent-value");
		Assert.Equal("new-parent-value", cache.GetOrDefault<string>("expire-parent"));
	}

	[Fact]
	public void L2CacheIntegration_CascadesToDistributedCache()
	{
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var serializer = new FusionCacheSystemTextJsonSerializer();

		using var cache = new FusionCache(new FusionCacheOptions());
		cache.SetupDistributedCache(distributedCache, serializer);

		// Set parent in both L1 and L2
		cache.Set("l2-parent", "parent-value", options => options.SetDuration(TimeSpan.FromMinutes(10)));

		// Set child with dependency 
		cache.Set("l2-child", "child-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("l2-parent")));

		// Verify both exist in cache (which should also populate L2)
		Assert.Equal("parent-value", cache.GetOrDefault<string>("l2-parent"));
		Assert.Equal("child-value", cache.GetOrDefault<string>("l2-child"));

		// Update parent - should cascade to L2
		cache.Set("l2-parent", "new-parent-value");

		// Child should be removed from both L1 and L2
		Assert.Null(cache.GetOrDefault<string>("l2-child"));
		// Parent should still exist with new value
		Assert.Equal("new-parent-value", cache.GetOrDefault<string>("l2-parent"));
	}

	[Fact]
	public void L2CacheIntegration_DisableCascadeToL2()
	{
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var serializer = new FusionCacheSystemTextJsonSerializer();

		var options = new FusionCacheOptions();
		options.Cascade.CascadeToL2 = false;

		using var cache = new FusionCache(options);
		cache.SetupDistributedCache(distributedCache, serializer);

		// Set parent and child
		cache.Set("no-l2-parent", "parent-value", options => options.SetDuration(TimeSpan.FromMinutes(10)));
		cache.Set("no-l2-child", "child-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("no-l2-parent")));

		// Verify both exist in cache
		Assert.Equal("parent-value", cache.GetOrDefault<string>("no-l2-parent"));
		Assert.Equal("child-value", cache.GetOrDefault<string>("no-l2-child"));

		// Update parent - should NOT cascade to L2 when CascadeToL2 is false
		cache.Set("no-l2-parent", "new-parent-value");

		// Child should NOT be removed from L2, but should be removed from L1
		Assert.NotNull(distributedCache.GetString("no-l2-child"));
		Assert.Null(cache.GetOrDefault<string>("no-l2-child"));
		// Parent should have new value
		Assert.Equal("new-parent-value", cache.GetOrDefault<string>("no-l2-parent"));
	}

	[Fact]
	public void CircularDependencies_PreventsInfiniteLoop()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Create circular dependency: A depends on B, B depends on A
		cache.Set("circular-a", "value-a");
		cache.Set("circular-b", "value-b", options => options
			.WithDependencies(DependsOn.Keys("circular-a")));

		// Now try to make A depend on B (creating circular dependency) - should throw exception
		Assert.Throws<FusionCacheDependencyCycleException>(() =>
			cache.Set("circular-a", "new-value-a", options => options
				.WithDependencies(DependsOn.Keys("circular-b"))));

		// Verify original values are still intact (no partial update occurred)
		Assert.Equal("value-a", cache.GetOrDefault<string>("circular-a"));
		Assert.Equal("value-b", cache.GetOrDefault<string>("circular-b"));
	}

	[Fact]
	public void SelfReferencingDependency_HandledGracefully()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Entry depends on itself
		cache.Set("self-ref", "initial-value");

		Assert.Throws<FusionCacheDependencyCycleException>(() =>
			cache.Set("self-ref", "self-dependent-value", options => options
			.WithDependencies(DependsOn.Keys("self-ref"))));

		// Verify original values are still intact
		Assert.Equal("initial-value", cache.GetOrDefault<string>("self-ref"));
	}

	[Fact]
	public void ParentOfAPI_EstablishesReverseDependency()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set child first
		cache.Set("reverse-child", "child-value");

		// Set parent that declares the existing child as dependent
		cache.Set("reverse-parent", "parent-value", options => options
			.WithDependencies(DependsOn.ParentOf("reverse-child")));

		// Verify both exist
		Assert.Equal("parent-value", cache.GetOrDefault<string>("reverse-parent"));
		Assert.Equal("child-value", cache.GetOrDefault<string>("reverse-child"));

		// Update parent - should invalidate child
		cache.Set("reverse-parent", "new-parent-value");

		Assert.Equal("new-parent-value", cache.GetOrDefault<string>("reverse-parent"));
		Assert.Null(cache.GetOrDefault<string>("reverse-child"));
	}

	[Fact]
	public void MixedDependencyTypes_KeysAndParentOf()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set up complex scenario
		cache.Set("mixed-parent", "parent-value");
		cache.Set("mixed-child1", "child1-value");
		cache.Set("mixed-child2", "child2-value");

		// Entry that both depends on a parent AND is parent of children
		cache.Set("mixed-middle", "middle-value", options => options
			.WithDependencies(DependsOn
				.Keys("mixed-parent")
				.ParentOf("mixed-child1")
				.ParentOf("mixed-child2")));

		// Verify all exist
		Assert.Equal("parent-value", cache.GetOrDefault<string>("mixed-parent"));
		Assert.Equal("middle-value", cache.GetOrDefault<string>("mixed-middle"));
		Assert.Equal("child1-value", cache.GetOrDefault<string>("mixed-child1"));
		Assert.Equal("child2-value", cache.GetOrDefault<string>("mixed-child2"));

		// Update top parent - should cascade through middle to children
		cache.Set("mixed-parent", "new-parent-value");

		Assert.Equal("new-parent-value", cache.GetOrDefault<string>("mixed-parent"));
		Assert.Null(cache.GetOrDefault<string>("mixed-middle"));
		Assert.Null(cache.GetOrDefault<string>("mixed-child1"));
		Assert.Null(cache.GetOrDefault<string>("mixed-child2"));
	}

	[Fact]
	public void DependencyWithTags_InteractionBehavior()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set parent with tags
		cache.Set("tagged-parent", "parent-value", tags: ["parent-tag"]);

		// Set child with dependency and different tags
		cache.Set("tagged-child", "child-value", options => options
			.WithDependencies(DependsOn.Keys("tagged-parent")), tags: ["child-tag"]);

		// Verify both exist
		Assert.Equal("parent-value", cache.GetOrDefault<string>("tagged-parent"));
		Assert.Equal("child-value", cache.GetOrDefault<string>("tagged-child"));

		// Clear by child tag - should only remove child
		cache.RemoveByTag("child-tag");
		Assert.Equal("parent-value", cache.GetOrDefault<string>("tagged-parent"));
		Assert.Null(cache.GetOrDefault<string>("tagged-child"));

		// Re-add child
		cache.Set("tagged-child", "child-value", options => options
			.WithDependencies(DependsOn.Keys("tagged-parent")), tags: ["child-tag"]);

		// Clear by parent tag - should remove both parent and child
		cache.RemoveByTag("parent-tag");
		Assert.Null(cache.GetOrDefault<string>("tagged-parent"));
		Assert.Null(cache.GetOrDefault<string>("tagged-child"));
	}

	[Fact]
	public void NonExistentParentDependency_HandledGracefully()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set child with dependency on non-existent parent
		cache.Set("orphan-from-start", "orphan-value", options => options
			.WithDependencies(DependsOn.Keys("non-existent-parent")));

		Assert.Equal("orphan-value", cache.GetOrDefault<string>("orphan-from-start"));

		// Later add the parent
		cache.Set("non-existent-parent", "parent-value");

		// Child should still exist (adding parent doesn't invalidate existing children)
		Assert.Equal("orphan-value", cache.GetOrDefault<string>("orphan-from-start"));

		// Update parent - should now invalidate child since dependency relationship exists
		cache.Set("non-existent-parent", "new-parent-value");
		Assert.Null(cache.GetOrDefault<string>("orphan-from-start"));
	}
}