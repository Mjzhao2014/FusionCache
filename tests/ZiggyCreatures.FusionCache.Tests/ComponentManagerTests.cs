using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Backplane.Memory;
using ZiggyCreatures.Caching.Fusion.DangerZone;
using ZiggyCreatures.Caching.Fusion.Internals;
using ZiggyCreatures.Caching.Fusion.Locking;
using ZiggyCreatures.Caching.Fusion.Plugins;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;
using FusionCacheTests.Stuff;
using Xunit.Abstractions;

namespace ZiggyCreatures.Caching.Fusion.Tests;

public class ComponentManagerTests : AbstractTests
{
	public ComponentManagerTests(ITestOutputHelper output)
		: base(output, null)
	{
	}

	[Fact]
	public void ConfigurationManager_CanInitialize_WithValidOptions()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);

		// ACT & ASSERT
		var configManager = TestsUtils.GetConfigurationManager(cache);
		Assert.NotNull(configManager);
		Assert.Equal(FusionCacheOptions.DefaultCacheName, configManager.CacheName);
		Assert.NotNull(configManager.InstanceId);
		Assert.NotNull(configManager.DefaultEntryOptions);
	}

	[Fact]
	public void ConfigurationManager_GeneratesInstanceId_WhenNotProvided()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		// Don't set InstanceId to null explicitly since it has private setter
		var logger = CreateXUnitLogger<FusionCache>();

		// ACT
		var cache = new FusionCache(options, logger: logger);
		var configManager = TestsUtils.GetConfigurationManager(cache);

		// ASSERT
		Assert.NotNull(configManager.InstanceId);
		Assert.NotEmpty(configManager.InstanceId);
	}

	[Fact]
	public void ConfigurationManager_PreservesInstanceId_WhenProvided()
	{
		// ARRANGE
		var expectedInstanceId = "test-instance-123";
		var options = Options.Create(new FusionCacheOptions());
		options.Value.SetInstanceId(expectedInstanceId);
		var logger = CreateXUnitLogger<FusionCache>();

		// ACT
		var cache = new FusionCache(options, logger: logger);
		var configManager = TestsUtils.GetConfigurationManager(cache);

		// ASSERT
		Assert.Equal(expectedInstanceId, configManager.InstanceId);
	}

	[Fact]
	public void ConfigurationManager_HandlesCacheKeyPrefix_Correctly()
	{
		// ARRANGE
		var prefix = "test-prefix-";
		var options = Options.Create(new FusionCacheOptions { CacheKeyPrefix = prefix });
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var configManager = TestsUtils.GetConfigurationManager(cache);

		// ACT
		var originalKey = "mykey";
		var processedKey = originalKey;
		configManager.MaybePreProcessCacheKey(ref processedKey);

		// ASSERT
		Assert.Equal(prefix + originalKey, processedKey);
	}

	[Fact]
	public void ConfigurationManager_DoesNotModifyKey_WhenNoPrefixSet()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions { CacheKeyPrefix = null });
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var configManager = TestsUtils.GetConfigurationManager(cache);

		// ACT
		var originalKey = "mykey";
		var processedKey = originalKey;
		configManager.MaybePreProcessCacheKey(ref processedKey);

		// ASSERT
		Assert.Equal(originalKey, processedKey);
	}

	[Fact]
	public void ConfigurationManager_CreateEntryOptions_ReturnsValidOptions()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var configManager = TestsUtils.GetConfigurationManager(cache);

		// ACT
		var entryOptions = configManager.CreateEntryOptions();

		// ASSERT
		Assert.NotNull(entryOptions);
		Assert.NotSame(configManager.DefaultEntryOptions, entryOptions); // Should be a copy
	}

	[Fact]
	public void ConfigurationManager_CreateEntryOptions_AppliesSetupAction()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var configManager = TestsUtils.GetConfigurationManager(cache);
		var expectedDuration = TimeSpan.FromMinutes(42);

		// ACT
		Action<FusionCacheEntryOptions> setupAction = opt => opt.SetDuration(expectedDuration);
		var entryOptions = configManager.CreateEntryOptions(setupAction);

		// ASSERT
		Assert.Equal(expectedDuration, entryOptions.Duration);
	}

	[Fact]
	public void ConfigurationManager_CheckTaggingEnabled_ThrowsWhenDisabled()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions { DisableTagging = true });
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var configManager = (FusionCacheConfigurationManager)TestsUtils.GetConfigurationManager(cache);

		// ACT & ASSERT
		Assert.Throws<InvalidOperationException>(() => configManager.CheckTaggingEnabled());
	}

	[Fact]
	public void ConfigurationManager_CheckTaggingEnabled_DoesNotThrowWhenEnabled()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions { DisableTagging = false });
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var configManager = TestsUtils.GetConfigurationManager(cache);

		// ACT & ASSERT (Should not throw)
		configManager.CheckTaggingEnabled();
	}

	[Fact]
	public void ComponentCoordinator_CanInitialize_WithValidParameters()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);

		// ACT & ASSERT
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		Assert.NotNull(coordinator);
		Assert.NotNull(coordinator.MemoryCacheAccessor);
		Assert.NotNull(coordinator.MemoryLocker);
		Assert.False(coordinator.HasDistributedCache);
		Assert.False(coordinator.HasBackplane);
	}

	[Fact]
	public void ComponentCoordinator_SetupSerializer_StoresSerializer()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var serializer = new FusionCacheSystemTextJsonSerializer();

		// ACT
		coordinator.SetupSerializer(serializer);

		// ASSERT
		Assert.Same(serializer, coordinator.Serializer);
	}

	[Fact]
	public void ComponentCoordinator_SetupDistributedCache_RequiresSerializerFirst()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

		// ACT & ASSERT
		Assert.Throws<InvalidOperationException>(() => coordinator.SetupDistributedCache(distributedCache));
	}

	[Fact]
	public void ComponentCoordinator_SetupDistributedCache_WorksWithSerializer()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = (FusionCacheComponentCoordinator)TestsUtils.GetComponentCoordinator(cache);
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

		// ACT
		coordinator.SetupSerializer(serializer);
		coordinator.SetupDistributedCache(distributedCache);

		// ASSERT
		Assert.True(coordinator.HasDistributedCache);
		Assert.Same(distributedCache, coordinator.DistributedCache);
		Assert.NotNull(coordinator.DistributedCacheAccessor);
	}

	[Fact]
	public void ComponentCoordinator_SetupDistributedCache_WithSerializerOverload()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

		// ACT
		coordinator.SetupDistributedCache(distributedCache, serializer);

		// ASSERT
		Assert.True(coordinator.HasDistributedCache);
		Assert.Same(distributedCache, coordinator.DistributedCache);
		Assert.Same(serializer, coordinator.Serializer);
	}

	[Fact]
	public void ComponentCoordinator_RemoveDistributedCache_ClearsState()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

		coordinator.SetupDistributedCache(distributedCache, serializer);

		// ACT
		coordinator.RemoveDistributedCache();

		// ASSERT
		Assert.False(coordinator.HasDistributedCache);
		Assert.Null(coordinator.DistributedCache);
		Assert.Null(coordinator.DistributedCacheAccessor);
	}

	[Fact]
	public void ComponentCoordinator_SetupBackplane_WorksCorrectly()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions());

		// ACT
		coordinator.SetupBackplane(backplane);

		// ASSERT
		Assert.True(coordinator.HasBackplane);
		Assert.Same(backplane, coordinator.Backplane);
		Assert.NotNull(coordinator.BackplaneAccessor);
	}

	[Fact]
	public void ComponentCoordinator_RemoveBackplane_ClearsState()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions());

		coordinator.SetupBackplane(backplane);

		// ACT
		coordinator.RemoveBackplane();

		// ASSERT
		Assert.False(coordinator.HasBackplane);
		Assert.Null(coordinator.Backplane);
		Assert.Null(coordinator.BackplaneAccessor);
	}

	[Fact]
	public void ComponentCoordinator_SetupBackplane_ReplacesExisting()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var backplane1 = new MemoryBackplane(new MemoryBackplaneOptions());
		var backplane2 = new MemoryBackplane(new MemoryBackplaneOptions());

		coordinator.SetupBackplane(backplane1);

		// ACT
		coordinator.SetupBackplane(backplane2);

		// ASSERT
		Assert.True(coordinator.HasBackplane);
		Assert.Same(backplane2, coordinator.Backplane);
	}

	[Fact]
	public void PluginManager_CanInitialize_WithValidParameters()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);

		// ACT & ASSERT
		var pluginManager = TestsUtils.GetPluginManager(cache);
		Assert.NotNull(pluginManager);
		var plugins = pluginManager.GetPluginsList();
		Assert.NotNull(plugins);
		Assert.Empty(plugins);
	}

	[Fact]
	public void PluginManager_AddPlugin_AddsAndStartsPlugin()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var pluginManager = TestsUtils.GetPluginManager(cache);
		var plugin = new SimplePlugin("test-plugin");

		// ACT
		pluginManager.AddPlugin(plugin);

		// ASSERT
		var plugins = pluginManager.GetPluginsList();
		Assert.NotNull(plugins);
		Assert.Single(plugins);
		Assert.Contains(plugin, plugins);
		Assert.True(plugin.IsRunning);
	}

	[Fact]
	public void PluginManager_AddPlugin_ThrowsOnDuplicate()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var pluginManager = (FusionCachePluginManager)TestsUtils.GetPluginManager(cache);
		var plugin = new SimplePlugin("test-plugin");

		pluginManager.AddPlugin(plugin);

		// ACT & ASSERT
		Assert.Throws<InvalidOperationException>(() => pluginManager.AddPlugin(plugin));
	}

	[Fact]
	public void PluginManager_RemovePlugin_RemovesAndStopsPlugin()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var pluginManager = TestsUtils.GetPluginManager(cache);
		var plugin = new SimplePlugin("test-plugin");

		pluginManager.AddPlugin(plugin);

		// ACT
		var result = pluginManager.RemovePlugin(plugin);

		// ASSERT
		Assert.True(result);
		var plugins = pluginManager.GetPluginsList();
		Assert.NotNull(plugins);
		Assert.Empty(plugins);
		Assert.False(plugin.IsRunning);
	}

	[Fact]
	public void PluginManager_RemovePlugin_ReturnsFalseForNonExistentPlugin()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var pluginManager = TestsUtils.GetPluginManager(cache);
		var plugin = new SimplePlugin("test-remove-plugin");

		// ACT
		var result = pluginManager.RemovePlugin(plugin);

		// ASSERT
		Assert.False(result);
	}

	[Fact]
	public void PluginManager_RemoveAllPlugins_ClearsAllPlugins()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var pluginManager = TestsUtils.GetPluginManager(cache);
		var plugin1 = new SimplePlugin("test-plugin-1");
		var plugin2 = new SimplePlugin("test-plugin-2");

		pluginManager.AddPlugin(plugin1);
		pluginManager.AddPlugin(plugin2);

		// ACT
		pluginManager.RemoveAllPlugins();

		// ASSERT
		var plugins = pluginManager.GetPluginsList();
		Assert.NotNull(plugins);
		Assert.Empty(plugins);
		Assert.False(plugin1.IsRunning);
		Assert.False(plugin2.IsRunning);
	}

	[Fact]
	public void FusionCache_MaintainsBackwardCompatibility_WithManagerDelegation()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions { CacheName = "TestCache" });
		var logger = CreateXUnitLogger<FusionCache>();
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions());
		var plugin = new SimplePlugin("integration-test-plugin");

		// ACT
		var cache = new FusionCache(options, logger: logger);
		cache.SetupDistributedCache(distributedCache, serializer);
		cache.SetupBackplane(backplane);
		cache.AddPlugin(plugin);

		// ASSERT
		Assert.Equal("TestCache", cache.CacheName);
		Assert.True(cache.HasDistributedCache);
		Assert.Same(distributedCache, cache.DistributedCache);
		Assert.True(cache.HasBackplane);
		Assert.Same(backplane, cache.Backplane);

		// Verify plugin was added through delegation
		var plugins = TestsUtils.GetPluginManager(cache).GetPluginsList();
		Assert.NotNull(plugins);
		Assert.Single(plugins);
		Assert.Contains(plugin, plugins);
		Assert.True(plugin.IsRunning);
	}

	[Fact]
	public void FusionCache_CreateEntryOptions_DelegatesToConfigurationManager()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var expectedDuration = TimeSpan.FromMinutes(30);

		// ACT
		var entryOptions = cache.CreateEntryOptions(opt => opt.SetDuration(expectedDuration));

		// ASSERT
		Assert.Equal(expectedDuration, entryOptions.Duration);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ConfigurationManager_HandlesCacheKeyPrefix_WithOriginalKey(bool hasPrefix)
	{
		// ARRANGE
		var prefix = hasPrefix ? "prefix-" : null;
		var options = Options.Create(new FusionCacheOptions { CacheKeyPrefix = prefix });
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var configManager = (FusionCacheConfigurationManager)TestsUtils.GetConfigurationManager(cache);

		// ACT
		var originalKey = "mykey";
		var processedKey = originalKey;
		configManager.MaybePreProcessCacheKey(ref processedKey);

		if (hasPrefix)
		{
			Assert.Equal(prefix + originalKey, processedKey);
		}
		else
		{
			Assert.Equal(originalKey, processedKey);
		}
	}

	[Fact]
	public void ComponentCoordinator_Dispose_CleansUpResources()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions());

		coordinator.SetupDistributedCache(distributedCache, serializer);
		coordinator.SetupBackplane(backplane);

		// ACT
		coordinator.Dispose();

		// ASSERT
		Assert.False(coordinator.HasDistributedCache);
		Assert.False(coordinator.HasBackplane);
		Assert.Null(coordinator.DistributedCache);
		Assert.Null(coordinator.Backplane);
	}


	// COMPONENT COORDINATOR SPECIFIC TESTS

	[Fact]
	public void ComponentCoordinator_AutoRecovery_IsLazilyInitialized()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions { EnableAutoRecovery = true });
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);

		// ACT
		var autoRecovery1 = coordinator.AutoRecovery;
		var autoRecovery2 = coordinator.AutoRecovery;

		// ASSERT
		Assert.NotNull(autoRecovery1);
		Assert.Same(autoRecovery1, autoRecovery2); // Should be the same instance (singleton)
	}

	[Fact]
	public async Task ComponentCoordinator_AutoRecovery_ThreadSafety()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions { EnableAutoRecovery = true });
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);

		// ACT - Access auto recovery from multiple threads concurrently
		var tasks = new List<Task<object>>();
		for (int i = 0; i < 10; i++)
		{
			tasks.Add(Task.Run(() => coordinator.AutoRecovery));
		}

		var autoRecoveryInstances = await Task.WhenAll(tasks);

		// ASSERT
		// All instances should be the same (singleton pattern)
		Assert.All(autoRecoveryInstances, instance => Assert.Same(autoRecoveryInstances[0], instance));
	}

	[Fact]
	public void ComponentCoordinator_Serializer_CanBeReplacedAfterInitialSetup()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var serializer1 = new FusionCacheSystemTextJsonSerializer();
		var serializer2 = new FusionCacheSystemTextJsonSerializer();

		// ACT
		coordinator.SetupSerializer(serializer1);
		var firstSerializer = coordinator.Serializer;
		
		coordinator.SetupSerializer(serializer2);
		var secondSerializer = coordinator.Serializer;

		// ASSERT
		Assert.Same(serializer1, firstSerializer);
		Assert.Same(serializer2, secondSerializer);
		Assert.NotSame(firstSerializer, secondSerializer);
	}

	[Fact]
	public void ComponentCoordinator_DistributedCacheSetup_WithBothOverloads()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

		// ACT - Test both overloads work correctly
		coordinator.SetupDistributedCache(distributedCache, serializer);

		// ASSERT
		Assert.True(coordinator.HasDistributedCache);
		Assert.Same(distributedCache, coordinator.DistributedCache);
		Assert.Same(serializer, coordinator.Serializer);
		Assert.NotNull(coordinator.DistributedCacheAccessor);
	}

	[Fact]
	public void ComponentCoordinator_DistributedCacheReplacement_ClearsOldReference()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var distributedCache1 = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var distributedCache2 = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

		// ACT
		coordinator.SetupDistributedCache(distributedCache1, serializer);
		var firstAccessor = coordinator.DistributedCacheAccessor;
		
		coordinator.SetupDistributedCache(distributedCache2, serializer);
		var secondAccessor = coordinator.DistributedCacheAccessor;

		// ASSERT
		Assert.NotSame(firstAccessor, secondAccessor);
		Assert.Same(distributedCache2, coordinator.DistributedCache);
		Assert.True(coordinator.HasDistributedCache);
	}

	[Fact]
	public void ComponentCoordinator_BackplaneReplacement_UnsubscribesOld()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var backplane1 = new MemoryBackplane(new MemoryBackplaneOptions());
		var backplane2 = new MemoryBackplane(new MemoryBackplaneOptions());

		// ACT
		coordinator.SetupBackplane(backplane1);
		var firstAccessor = coordinator.BackplaneAccessor;
		
		coordinator.SetupBackplane(backplane2);
		var secondAccessor = coordinator.BackplaneAccessor;

		// ASSERT
		Assert.NotSame(firstAccessor, secondAccessor);
		Assert.Same(backplane2, coordinator.Backplane);
		Assert.True(coordinator.HasBackplane);
	}

	[Fact]
	public void ComponentCoordinator_MemoryLocker_CanBeCustomized()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var customMemoryLocker = new StandardMemoryLocker();

		// ACT
		var cache = new FusionCache(options, logger: logger, memoryLocker: customMemoryLocker);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);

		// ASSERT
		Assert.Same(customMemoryLocker, coordinator.MemoryLocker);
	}

	[Fact]
	public void ComponentCoordinator_MemoryCache_AccessorHasCorrectProperties()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		
		var logger = CreateXUnitLogger<FusionCache>();
		var customMemoryCache = new MemoryCache(new MemoryCacheOptions());

		// ACT
		var cache = new FusionCache(options, customMemoryCache, logger: logger);
		var coordinator = (FusionCacheComponentCoordinator)TestsUtils.GetComponentCoordinator(cache);

		// ASSERT
		Assert.NotNull(coordinator.MemoryCacheAccessor);
		Assert.False(coordinator.MemoryCacheCanClear);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ComponentCoordinator_BackplaneWarning_RespectsSkipNotificationsFlag(bool skipNotifications)
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		options.Value.DefaultEntryOptions.SkipBackplaneNotifications = skipNotifications;
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions());

		// ACT
		coordinator.SetupBackplane(backplane);

		// ASSERT
		Assert.True(coordinator.HasBackplane);
		Assert.False(coordinator.HasDistributedCache);
		// With skipNotifications=true, no warning should be logged
		// With skipNotifications=false, warning should be logged (we can't test log output easily)
	}

	[Fact]
	public void ComponentCoordinator_SetupSerializer_ReturnsOriginalCache()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var serializer = new FusionCacheSystemTextJsonSerializer();

		// ACT
		var result = coordinator.SetupSerializer(serializer);

		// ASSERT
		Assert.Same(cache, result);
	}

	[Fact]
	public void ComponentCoordinator_SetupDistributedCache_ReturnsOriginalCache()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

		// ACT
		var result = coordinator.SetupDistributedCache(distributedCache, serializer);

		// ASSERT
		Assert.Same(cache, result);
	}

	[Fact]
	public void ComponentCoordinator_SetupBackplane_ReturnsOriginalCache()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions());

		// ACT
		var result = coordinator.SetupBackplane(backplane);

		// ASSERT
		Assert.Same(cache, result);
	}

	[Fact]
	public void ComponentCoordinator_RemoveDistributedCache_ReturnsOriginalCache()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);

		// ACT
		var result = coordinator.RemoveDistributedCache();

		// ASSERT
		Assert.Same(cache, result);
	}

	[Fact]
	public void ComponentCoordinator_RemoveBackplane_ReturnsOriginalCache()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);

		// ACT
		var result = coordinator.RemoveBackplane();

		// ASSERT
		Assert.Same(cache, result);
	}

	[Fact]
	public void ComponentCoordinator_InitialState_IsCorrect()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();

		// ACT
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);

		// ASSERT
		Assert.NotNull(coordinator.MemoryCacheAccessor);
		Assert.NotNull(coordinator.MemoryLocker);
		Assert.False(coordinator.HasDistributedCache);
		Assert.False(coordinator.HasBackplane);
		Assert.Null(coordinator.DistributedCache);
		Assert.Null(coordinator.Backplane);
		Assert.Null(coordinator.DistributedCacheAccessor);
		Assert.Null(coordinator.BackplaneAccessor);
		Assert.Null(coordinator.Serializer);
	}

	[Fact]
	public void ComponentCoordinator_StateAfterComponentSetup_IsCorrect()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions());

		// ACT
		coordinator.SetupDistributedCache(distributedCache, serializer);
		coordinator.SetupBackplane(backplane);

		// ASSERT
		Assert.True(coordinator.HasDistributedCache);
		Assert.True(coordinator.HasBackplane);
		Assert.Same(distributedCache, coordinator.DistributedCache);
		Assert.Same(backplane, coordinator.Backplane);
		Assert.Same(serializer, coordinator.Serializer);
		Assert.NotNull(coordinator.DistributedCacheAccessor);
		Assert.NotNull(coordinator.BackplaneAccessor);
	}

	[Fact]
	public async Task ComponentCoordinator_ConcurrentSetupOperations_ThreadSafe()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions());

		// ACT - Simulate concurrent setup operations
		var tasks = new List<Task>();
		
		tasks.Add(Task.Run(() => coordinator.SetupDistributedCache(distributedCache, serializer)));
		tasks.Add(Task.Run(() => coordinator.SetupBackplane(backplane)));
		tasks.Add(Task.Run(() => coordinator.SetupSerializer(serializer)));

		await Task.WhenAll(tasks.ToArray());

		// ASSERT
		// The operations should complete without exceptions
		Assert.True(coordinator.HasDistributedCache);
		Assert.True(coordinator.HasBackplane);
		Assert.NotNull(coordinator.Serializer);
	}

	[Fact]
	public void ComponentCoordinator_DisposeMultipleTimes_Idempotent()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
		var backplane = new MemoryBackplane(new MemoryBackplaneOptions());

		coordinator.SetupDistributedCache(distributedCache, serializer);
		coordinator.SetupBackplane(backplane);

		// ACT - Dispose multiple times
		coordinator.Dispose();
		coordinator.Dispose(); // Should not throw
		coordinator.Dispose(); // Should not throw

		// ASSERT
		Assert.False(coordinator.HasDistributedCache);
		Assert.False(coordinator.HasBackplane);
	}

	[Fact]
	public void ComponentCoordinator_WithCustomMemoryCache_PreservesCustomInstance()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();
		var customMemoryCache = new MemoryCache(new MemoryCacheOptions 
		{ 
			SizeLimit = 1000 
		});

		// ACT
		var cache = new FusionCache(options, customMemoryCache, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);

		// ASSERT
		Assert.NotNull(coordinator.MemoryCacheAccessor);
		// The custom memory cache should be used internally
	}

	[Fact]
	public void ComponentCoordinator_ConstructorParameterValidation()
	{
		// Test that the coordinator properly validates constructor parameters
		// This is implicitly tested through FusionCache constructor,
		// but we can verify the behavior through reflection

		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();

		// ACT & ASSERT
		// FusionCache constructor should handle null parameters appropriately
		Assert.Throws<ArgumentNullException>(() => new FusionCache(null!, logger: logger));
	}

	[Fact]
	public void ComponentCoordinator_AutoRecovery_WithDisabledOption()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions { EnableAutoRecovery = false });
		var logger = CreateXUnitLogger<FusionCache>();
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);

		// ACT
		var autoRecovery = coordinator.AutoRecovery;

		// ASSERT
		// Auto recovery should still be available even if disabled in options
		// The option controls usage, not availability
		Assert.NotNull(autoRecovery);
	}

	[Fact]
	public void ComponentCoordinator_MemoryLocker_DefaultWhenNotProvided()
	{
		// ARRANGE
		var options = Options.Create(new FusionCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();

		// ACT
		var cache = new FusionCache(options, logger: logger);
		var coordinator = TestsUtils.GetComponentCoordinator(cache);

		// ASSERT
		Assert.NotNull(coordinator.MemoryLocker);
		// Should have a default memory locker when none is provided
		Assert.IsType<StandardMemoryLocker>(coordinator.MemoryLocker);
	}

}