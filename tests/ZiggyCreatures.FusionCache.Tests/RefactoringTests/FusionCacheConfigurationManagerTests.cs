using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.DangerZone;
using ZiggyCreatures.Caching.Fusion.Internals;
using FusionCacheTests.Stuff;

namespace FusionCacheTests.RefactoringTests;

public class FusionCacheConfigurationManagerTests : AbstractTests
{
	public FusionCacheConfigurationManagerTests(ITestOutputHelper output)
		: base(output, null)
	{
	}

	private FusionCacheConfigurationManager CreateConfigurationManager(
		FusionCacheOptions? options = null,
		ILogger<FusionCache>? logger = null,
		string? cacheName = null,
		IMemoryCache? memoryCache = null)
	{
		options ??= new FusionCacheOptions();
		if (cacheName is not null)
			options.CacheName = cacheName;
		
		var optionsAccessor = Options.Create(options);
		return new FusionCacheConfigurationManager(
			optionsAccessor, 
			logger, 
			cacheName ?? FusionCacheOptions.DefaultCacheName, 
			memoryCache);
	}

	[Fact]
	public void Constructor_WithValidOptions_InitializesCorrectly()
	{
		// Arrange
		var options = new FusionCacheOptions
		{
			CacheName = "TestCache",
			CacheKeyPrefix = "test:"
		};
		options.SetInstanceId("test-instance");

		// Act
		var manager = CreateConfigurationManager(options);

		// Assert
		Assert.NotNull(manager.Options);
		Assert.Equal("TestCache", manager.CacheName);
		Assert.Equal("test-instance", manager.InstanceId);
		Assert.NotNull(manager.DefaultEntryOptions);
		Assert.NotNull(manager.TryUpdateEntryOptions);
		Assert.NotNull(manager.TagsDefaultEntryOptions);
		Assert.NotNull(manager.CascadeRemoveByTagEntryOptions);
	}

	[Fact]
	public void Constructor_WithNullOptionsAccessor_ThrowsArgumentNullException()
	{
		// Arrange & Act & Assert
		Assert.Throws<ArgumentNullException>(() => 
			new FusionCacheConfigurationManager(null!, null, "TestCache", null));
	}

	[Fact]
	public void Constructor_WithNullOptionsValue_ThrowsNullReferenceException()
	{
		// Arrange
		var optionsAccessor = Options.Create<FusionCacheOptions>(null!);

		// Act & Assert
		Assert.Throws<NullReferenceException>(() => 
			new FusionCacheConfigurationManager(optionsAccessor, null, "TestCache", null));
	}

	[Fact]
	public void Constructor_GeneratesInstanceId_WhenNotProvided()
	{
		// Arrange
		var options = new FusionCacheOptions();

		// Act
		var manager = CreateConfigurationManager(options);

		// Assert
		Assert.NotNull(manager.InstanceId);
		Assert.NotEmpty(manager.InstanceId);
	}

	[Fact]
	public void Constructor_PreservesInstanceId_WhenProvided()
	{
		// Arrange
		var expectedInstanceId = "custom-instance-id";
		var options = new FusionCacheOptions();
		options.SetInstanceId(expectedInstanceId);

		// Act
		var manager = CreateConfigurationManager(options);

		// Assert
		Assert.Equal(expectedInstanceId, manager.InstanceId);
	}

	[Fact]
	public void CreateEntryOptions_ReturnsNewInstance()
	{
		// Arrange
		var manager = CreateConfigurationManager();

		// Act
		var options1 = manager.CreateEntryOptions();
		var options2 = manager.CreateEntryOptions();

		// Assert
		Assert.NotSame(options1, options2);
		Assert.NotSame(manager.DefaultEntryOptions, options1);
	}

	[Fact]
	public void CreateEntryOptions_WithSetupAction_AppliesCustomization()
	{
		// Arrange
		var manager = CreateConfigurationManager();
		var expectedDuration = TimeSpan.FromMinutes(10);

		// Act
		var options = manager.CreateEntryOptions(opt => opt.Duration = expectedDuration);

		// Assert
		Assert.Equal(expectedDuration, options.Duration);
	}

	[Fact]
	public void CreateEntryOptions_WithDuration_SetsDuration()
	{
		// Arrange
		var manager = CreateConfigurationManager();
		var expectedDuration = TimeSpan.FromHours(2);

		// Act
		var options = manager.CreateEntryOptions(duration: expectedDuration);

		// Assert
		Assert.Equal(expectedDuration, options.Duration);
	}

	[Fact]
	public void MaybePreProcessCacheKey_WithoutPrefix_DoesNotModifyKey()
	{
		// Arrange
		var options = new FusionCacheOptions(); // No prefix
		var manager = CreateConfigurationManager(options);
		var key = "testkey";

		// Act
		manager.MaybePreProcessCacheKey(ref key);

		// Assert
		Assert.Equal("testkey", key);
	}

	[Fact]
	public void MaybePreProcessCacheKey_WithPrefix_PrependsPrefix()
	{
		// Arrange
		var options = new FusionCacheOptions { CacheKeyPrefix = "myprefix:" };
		var manager = CreateConfigurationManager(options);
		var key = "testkey";

		// Act
		manager.MaybePreProcessCacheKey(ref key);

		// Assert
		Assert.Equal("myprefix:testkey", key);
	}

	[Fact]
	public void CheckTaggingEnabled_WithTaggingDisabled_ThrowsInvalidOperationException()
	{
		// Arrange
		var options = new FusionCacheOptions { DisableTagging = true };
		var manager = CreateConfigurationManager(options);

		// Act & Assert
		var ex = Assert.Throws<InvalidOperationException>(() => manager.CheckTaggingEnabled());
		Assert.Contains("Tagging", ex.Message);
		Assert.Contains("disabled", ex.Message);
	}

	[Fact]
	public void CheckTaggingEnabled_WithTaggingEnabled_DoesNotThrow()
	{
		// Arrange
		var options = new FusionCacheOptions { DisableTagging = false };
		var manager = CreateConfigurationManager(options);

		// Act & Assert (should not throw)
		manager.CheckTaggingEnabled();
	}

	[Fact]
	public void ValidateCacheKey_WithNullKey_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.Throws<ArgumentNullException>(() => 
			FusionCacheConfigurationManager.ValidateCacheKey(null!));
	}

	[Fact]
	public void ValidateCacheKey_WithValidKey_DoesNotThrow()
	{
		// Act & Assert (should not throw)
		FusionCacheConfigurationManager.ValidateCacheKey("valid-key");
	}

	[Fact]
	public void ValidateTag_WithNullTag_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.Throws<ArgumentNullException>(() => 
			FusionCacheConfigurationManager.ValidateTag(null!));
	}

	[Fact]
	public void ValidateTag_WithValidTag_DoesNotThrow()
	{
		// Act & Assert (should not throw)
		FusionCacheConfigurationManager.ValidateTag("valid-tag");
	}

	[Fact]
	public void ValidateTags_WithNullTags_DoesNotThrow()
	{
		// Arrange
		var manager = CreateConfigurationManager();

		// Act & Assert (should not throw)
		manager.ValidateTags(null);
	}

	[Fact]
	public void ValidateTags_WithEmptyTags_DoesNotThrow()
	{
		// Arrange
		var manager = CreateConfigurationManager();

		// Act & Assert (should not throw)
		manager.ValidateTags(Array.Empty<string>());
	}

	[Fact]
	public void ValidateTags_WithTaggingDisabled_ThrowsInvalidOperationException()
	{
		// Arrange
		var options = new FusionCacheOptions { DisableTagging = true };
		var manager = CreateConfigurationManager(options);
		var tags = new[] { "tag1", "tag2" };

		// Act & Assert
		Assert.Throws<InvalidOperationException>(() => manager.ValidateTags(tags));
	}

	[Fact]
	public void ValidateTags_WithValidTags_DoesNotThrow()
	{
		// Arrange
		var options = new FusionCacheOptions { DisableTagging = false };
		var manager = CreateConfigurationManager(options);
		var tags = new[] { "tag1", "tag2", "tag3" };

		// Act & Assert (should not throw)
		manager.ValidateTags(tags);
	}

	[Fact]
	public void GetTagCacheKey_ReturnsFormattedKey()
	{
		// Arrange
		var tag = "mytag";

		// Act
		var result = FusionCacheConfigurationManager.GetTagCacheKey(tag);

		// Assert
		Assert.Equal("__fc:t:mytag", result);
	}

	[Fact]
	public void GetTagInternalCacheKey_AppliesKeyPrefix()
	{
		// Arrange
		var options = new FusionCacheOptions { CacheKeyPrefix = "prefix:" };
		var manager = CreateConfigurationManager(options);
		var tag = "mytag";

		// Act
		var result = manager.GetTagInternalCacheKey(tag);

		// Assert
		Assert.Equal("prefix:__fc:t:mytag", result);
	}

	[Fact]
	public void GetTagInternalCacheKey_WithoutPrefix_ReturnsTagCacheKey()
	{
		// Arrange
		var manager = CreateConfigurationManager();
		var tag = "mytag";

		// Act
		var result = manager.GetTagInternalCacheKey(tag);

		// Assert
		Assert.Equal("__fc:t:mytag", result);
	}

	[Fact]
	public void TagCacheKeys_AreInitializedCorrectly()
	{
		// Arrange & Act
		var manager = CreateConfigurationManager();

		// Assert
		Assert.NotNull(manager.TagInternalCacheKeyPrefix);
		Assert.NotNull(manager.ClearRemoveTagCacheKey);
		Assert.NotNull(manager.ClearRemoveTagInternalCacheKey);
		Assert.NotNull(manager.ClearExpireTagCacheKey);
		Assert.NotNull(manager.ClearExpireTagInternalCacheKey);
		
		Assert.Contains("__fc:t:", manager.TagInternalCacheKeyPrefix);
		Assert.Contains("!", manager.ClearRemoveTagCacheKey);
		Assert.Contains("*", manager.ClearExpireTagCacheKey);
	}

	[Fact]
	public void MaybeGenerateOperationId_ReturnsValidId()
	{
		// Arrange
		var logger = CreateXUnitLogger<FusionCache>();
		var manager = CreateConfigurationManager(logger: logger);

		// Act
		var operationId = manager.MaybeGenerateOperationId();

		// Assert
		Assert.NotNull(operationId);
		Assert.NotEmpty(operationId);
		// Should be 13 characters (based on FusionCacheInternalUtils implementation)
		Assert.Equal(13, operationId.Length);
	}

	[Fact]
	public void OptionsDuplication_PreventsExternalModification()
	{
		// Arrange
		var originalOptions = new FusionCacheOptions 
		{ 
			CacheName = "Original",
			CacheKeyPrefix = "orig:"
		};
		var manager = CreateConfigurationManager(originalOptions);

		// Act - Modify original options after manager creation
		originalOptions.CacheName = "Modified";
		originalOptions.CacheKeyPrefix = "mod:";

		// Assert - Manager should have the original values
		Assert.Equal("Original", manager.CacheName);
		// The manager should have preserved the original prefix behavior
		var key = "test";
		manager.MaybePreProcessCacheKey(ref key);
		Assert.Equal("orig:test", key);
	}
}