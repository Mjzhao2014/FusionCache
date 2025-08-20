using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using ZiggyCreatures.Caching.Fusion.Internals.Builder;

namespace ZiggyCreatures.Caching.Fusion.Tests;

public class EvictionFluentApiTests
{
	[Fact]
	public void FusionCacheBuilder_WithLruEviction_ConfiguresCorrectly()
	{
		// Arrange & Act & Assert
		var services = new ServiceCollection();
		var builder = new FusionCacheBuilder("test-cache", services);
		
		// This should not throw - it means the extension method works
		var result = builder.WithLruEviction(maxEntryCount: 100, evictionPercentage: 0.2);
		
		Assert.NotNull(result);
		Assert.Same(builder, result); // Should return the same builder for chaining
	}

	[Fact]
	public void FusionCacheBuilder_WithLruEviction_WithConfig_ConfiguresCorrectly()
	{
		// Arrange
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = 500,
			EvictionThreshold = 0.8,
			EvictionPercentage = 0.15,
			MinEvictionBatchSize = 10,
			MaxEvictionBatchSize = 50
		};

		// Act & Assert
		var services = new ServiceCollection();
		var builder = new FusionCacheBuilder("test-cache", services);
		
		var result = builder.WithLruEviction(config);
		
		Assert.NotNull(result);
		Assert.Same(builder, result); // Should return the same builder for chaining
	}

	[Fact]
	public void FusionCacheBuilder_WithLfuEviction_ConfiguresCorrectly()
	{
		// Arrange & Act & Assert
		var services = new ServiceCollection();
		var builder = new FusionCacheBuilder("test-cache", services);
		
		var result = builder.WithLfuEviction(maxEntryCount: 250, evictionPercentage: 0.25);
		
		Assert.NotNull(result);
		Assert.Same(builder, result); // Should return the same builder for chaining
	}

	[Fact]
	public void FusionCacheBuilder_WithLfuEviction_WithConfig_ConfiguresCorrectly()
	{
		// Arrange
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = 1000,
			EvictionPercentage = 0.1
		};

		// Act & Assert
		var services = new ServiceCollection();
		var builder = new FusionCacheBuilder("test-cache", services);
		
		var result = builder.WithLfuEviction(config);
		
		Assert.NotNull(result);
		Assert.Same(builder, result); // Should return the same builder for chaining
	}
	

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(-100)]
	public void FusionCacheBuilder_WithInvalidMaxEntryCount_ThrowsArgumentException(int invalidCount)
	{
		// Arrange
		var options = new FusionCacheOptions();
		var services = new ServiceCollection();
		var builder = new FusionCacheBuilder("test-cache", services)
			.WithOptions(options);

		// Act & Assert
		Assert.Throws<ArgumentException>(() => 
			builder.WithLruEviction(maxEntryCount: invalidCount));
		Assert.Throws<ArgumentException>(() => 
			builder.WithLfuEviction(maxEntryCount: invalidCount));
	}


	
}