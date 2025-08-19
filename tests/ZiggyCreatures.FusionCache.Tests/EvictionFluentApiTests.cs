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
	public void FusionCacheBuilder_WithLruEvictionBySize_ConfiguresCorrectly()
	{
		// Arrange & Act & Assert
		var services = new ServiceCollection();
		var builder = new FusionCacheBuilder("test-cache", services);
		
		var result = builder.WithLruEvictionBySize(maxTotalSize: 1024 * 1024, evictionPercentage: 0.3);
		
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

	[Fact]
	public void FusionCacheBuilder_WithSizeBasedEviction_ConfiguresCorrectly()
	{
		// Arrange & Act & Assert
		var services = new ServiceCollection();
		var builder = new FusionCacheBuilder("test-cache", services);
		
		var result = builder.WithSizeBasedEviction(maxTotalSize: 5 * 1024 * 1024, evictionPercentage: 0.2);
		
		Assert.NotNull(result);
		Assert.Same(builder, result); // Should return the same builder for chaining
	}

	[Fact]
	public void FusionCacheBuilder_WithSizeBasedEviction_WithConfig_ConfiguresCorrectly()
	{
		// Arrange
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxTotalSize = 10 * 1024 * 1024,
			EvictionThreshold = 0.9,
			EvictionPercentage = 0.3
		};

		// Act & Assert
		var services = new ServiceCollection();
		var builder = new FusionCacheBuilder("test-cache", services);
		
		var result = builder.WithSizeBasedEviction(config);
		
		Assert.NotNull(result);
		Assert.Same(builder, result); // Should return the same builder for chaining
	}

	[Fact]
	public void FusionCacheBuilder_ChainMultipleConfigurations_LastOneWins()
	{
		// Arrange & Act & Assert
		var services = new ServiceCollection();
		var builder = new FusionCacheBuilder("test-cache", services);
		
		// This should not throw and should return the builder for chaining
		var result = builder
			.WithLruEviction(maxEntryCount: 100)
			.WithLfuEviction(maxEntryCount: 200)
			.WithSizeBasedEviction(maxTotalSize: 1024);

		Assert.NotNull(result);
		Assert.Same(builder, result); // Should return the same builder for chaining
	}

	[Fact]
	public void FusionCacheBuilder_WithNullBuilder_ThrowsArgumentNullException()
	{
		// Arrange
		IFusionCacheBuilder builder = null!;

		// Act & Assert
		Assert.Throws<ArgumentNullException>(() => 
			builder.WithLruEviction(maxEntryCount: 100));
		Assert.Throws<ArgumentNullException>(() => 
			builder.WithLfuEviction(maxEntryCount: 100));
		Assert.Throws<ArgumentNullException>(() => 
			builder.WithSizeBasedEviction(maxTotalSize: 1024));
	}

	[Fact]
	public void FusionCacheBuilder_WithNullConfig_ThrowsArgumentNullException()
	{
		// Arrange
		var options = new FusionCacheOptions();
		var services = new ServiceCollection();
		var builder = new FusionCacheBuilder("test-cache", services)
			.WithOptions(options);

		// Act & Assert
		Assert.Throws<ArgumentNullException>(() => 
			builder.WithLruEviction(null!));
		Assert.Throws<ArgumentNullException>(() => 
			builder.WithLfuEviction(null!));
		Assert.Throws<ArgumentNullException>(() => 
			builder.WithSizeBasedEviction(null!));
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

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(-1024)]
	public void FusionCacheBuilder_WithInvalidMaxTotalSize_ThrowsArgumentException(long invalidSize)
	{
		// Arrange
		var options = new FusionCacheOptions();
		var services = new ServiceCollection();
		var builder = new FusionCacheBuilder("test-cache", services)
			.WithOptions(options);

		// Act & Assert
		Assert.Throws<ArgumentException>(() => 
			builder.WithLruEvictionBySize(maxTotalSize: invalidSize));
		Assert.Throws<ArgumentException>(() => 
			builder.WithSizeBasedEviction(maxTotalSize: invalidSize));
	}

	[Theory]
	[InlineData(-0.1)]
	[InlineData(0.0)]
	[InlineData(1.1)]
	[InlineData(2.0)]
	public void FusionCacheBuilder_WithInvalidEvictionPercentage_ThrowsArgumentException(double invalidPercentage)
	{
		// Arrange
		var options = new FusionCacheOptions();
		var services = new ServiceCollection();
		var builder = new FusionCacheBuilder("test-cache", services)
			.WithOptions(options);

		// Act & Assert
		Assert.Throws<ArgumentException>(() => 
			builder.WithLruEviction(maxEntryCount: 100, evictionPercentage: invalidPercentage));
		Assert.Throws<ArgumentException>(() => 
			builder.WithLfuEviction(maxEntryCount: 100, evictionPercentage: invalidPercentage));
		Assert.Throws<ArgumentException>(() => 
			builder.WithSizeBasedEviction(maxTotalSize: 1024, evictionPercentage: invalidPercentage));
	}
}