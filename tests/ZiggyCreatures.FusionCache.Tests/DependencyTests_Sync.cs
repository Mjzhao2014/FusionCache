using FusionCacheTests.Stuff;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.NewtonsoftJson;

namespace FusionCacheTests;

public partial class DependencyTests
{
	[Fact]
	public void DependenciesWorkWithFactorySync()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var callCount = 0;

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Get or set with factory
		var result1 = cache.GetOrSet<int>("child", (ctx, ct) => {
			callCount++;
			return 42;
		}, options: options);
		
		Assert.Equal(42, result1);
		Assert.Equal(1, callCount);

		// Should get from cache without calling factory
		var result2 = cache.GetOrSet<int>("child", (ctx, ct) => {
			callCount++;
			return 84;
		}, options: options);
		
		Assert.Equal(42, result2);
		Assert.Equal(1, callCount); // Factory not called again

		// Set parent initially
		cache.Set("parent", "initial");

		// Change parent to invalidate child (this is now an update)
		cache.Set("parent", "changed");

		// Should call factory again since child was invalidated
		var result3 = cache.GetOrSet<int>("child", (ctx, ct) => {
			callCount++;
			return 99;
		}, options: options);
		
		Assert.Equal(99, result3);
		Assert.Equal(2, callCount); // Factory called again
	}

	[Fact]
	public void ComplexDependencyChainWorks()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Create a complex dependency scenario
		// product -> category, brand
		// category_summary -> category
		// brand_summary -> brand
		// combined_summary -> category_summary, brand_summary

		var productOptions = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("category", "brand");

		var categorySummaryOptions = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("category");

		var brandSummaryOptions = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("brand");

		var combinedSummaryOptions = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("category_summary", "brand_summary");

		// Set all entries
		cache.Set("category", "Electronics");
		cache.Set("brand", "Apple");
		cache.Set("product", "iPhone", productOptions);
		cache.Set("category_summary", "Electronics: 100 items", categorySummaryOptions);
		cache.Set("brand_summary", "Apple: 50 items", brandSummaryOptions);
		cache.Set("combined_summary", "Total: 150 items", combinedSummaryOptions);

		// Verify all are in cache
		Assert.Equal("Electronics", cache.GetOrDefault<string>("category"));
		Assert.Equal("Apple", cache.GetOrDefault<string>("brand"));
		Assert.Equal("iPhone", cache.GetOrDefault<string>("product"));
		Assert.Equal("Electronics: 100 items", cache.GetOrDefault<string>("category_summary"));
		Assert.Equal("Apple: 50 items", cache.GetOrDefault<string>("brand_summary"));
		Assert.Equal("Total: 150 items", cache.GetOrDefault<string>("combined_summary"));

		// Update category
		cache.Set("category", "Smartphones");

		// Product and category_summary should be invalidated
		Assert.Equal("Smartphones", cache.GetOrDefault<string>("category"));
		Assert.Null(cache.GetOrDefault<string>("product"));
		Assert.Null(cache.GetOrDefault<string>("category_summary"));
		
		// Brand, brand_summary, and combined_summary should still be there
		Assert.Equal("Apple", cache.GetOrDefault<string>("brand"));
		Assert.Equal("Apple: 50 items", cache.GetOrDefault<string>("brand_summary"));
		// combined_summary is still there because only category_summary was invalidated,
		// not brand_summary (dependency invalidation doesn't cascade automatically)
		Assert.Equal("Total: 150 items", cache.GetOrDefault<string>("combined_summary"));
	}

	[Fact]
	public void DependencyCleanupOnRemove()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		var options = cache.CreateEntryOptions()
			.SetDuration(TimeSpan.FromMinutes(10))
			.SetDependencies("parent");

		// Set child with dependency
		cache.Set("child", 42, options);
		cache.Set("parent", "value");
		
		// Verify child is in cache
		Assert.Equal(42, cache.GetOrDefault<int>("child"));

		// Remove child (should clean up dependency tracking)
		cache.Remove("child");

		// Set parent again - should NOT affect the non-existent child
		cache.Set("parent", "changed");

		// Child should still be gone
		Assert.Equal(0, cache.GetOrDefault<int>("child"));

		// Set child again with same dependency
		cache.Set("child", 84, options);
		
		// Verify it works again
		Assert.Equal(84, cache.GetOrDefault<int>("child"));
		
		// Change parent - should affect child again
		cache.Set("parent", "changed_again");
		Assert.Equal(0, cache.GetOrDefault<int>("child"));
	}
}