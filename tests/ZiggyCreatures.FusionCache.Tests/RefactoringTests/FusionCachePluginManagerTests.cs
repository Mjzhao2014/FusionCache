using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Internals;
using ZiggyCreatures.Caching.Fusion.Plugins;
using FusionCacheTests.Stuff;

namespace FusionCacheTests.RefactoringTests;

public class FusionCachePluginManagerTests : AbstractTests
{
	public FusionCachePluginManagerTests(ITestOutputHelper output)
		: base(output, null)
	{
	}

	private class TestPlugin : IFusionCachePlugin
	{
		public bool StartCalled { get; private set; }
		public bool StopCalled { get; private set; }
		public bool ThrowOnStart { get; set; }
		public bool ThrowOnStop { get; set; }
		public FusionCache? LastCacheOnStart { get; private set; }
		public FusionCache? LastCacheOnStop { get; private set; }

		public void Start(IFusionCache cache)
		{
			if (ThrowOnStart)
				throw new InvalidOperationException("Start failed");
			
			StartCalled = true;
			LastCacheOnStart = cache as FusionCache;
		}

		public void Stop(IFusionCache cache)
		{
			if (ThrowOnStop)
				throw new InvalidOperationException("Stop failed");
			
			StopCalled = true;
			LastCacheOnStop = cache as FusionCache;
		}
	}

	private FusionCachePluginManager CreatePluginManager(
		FusionCache? cache = null,
		FusionCacheOptions? options = null,
		ILogger<FusionCache>? logger = null)
	{
		options ??= new FusionCacheOptions();
		cache ??= new FusionCache(Options.Create(options));
		
		return new FusionCachePluginManager(cache, options, logger);
	}

	[Fact]
	public void Constructor_WithValidParameters_InitializesCorrectly()
	{
		// Arrange
		var options = new FusionCacheOptions();
		var cache = new FusionCache(Options.Create(options));

		// Act
		var manager = CreatePluginManager(cache, options);

		// Assert
		Assert.NotNull(manager);
		var pluginsList = manager.GetPluginsList();
		Assert.NotNull(pluginsList);
		Assert.Empty(pluginsList);
	}

	[Fact]
	public void Constructor_WithNullCache_ThrowsArgumentNullException()
	{
		// Arrange
		var options = new FusionCacheOptions();

		// Act & Assert
		Assert.Throws<ArgumentNullException>(() =>
			new FusionCachePluginManager(null!, options, null));
	}

	[Fact]
	public void Constructor_WithNullOptions_ThrowsArgumentNullException()
	{
		// Arrange
		var options = new FusionCacheOptions();
		var cache = new FusionCache(Options.Create(options));

		// Act & Assert
		Assert.Throws<ArgumentNullException>(() =>
			new FusionCachePluginManager(cache, null!, null));
	}

	[Fact]
	public void AddPlugin_WithValidPlugin_AddsAndStartsPlugin()
	{
		// Arrange
		var manager = CreatePluginManager();
		var plugin = new TestPlugin();

		// Act
		manager.AddPlugin(plugin);

		// Assert
		Assert.True(plugin.StartCalled);
		Assert.NotNull(plugin.LastCacheOnStart);
		
		var pluginsList = manager.GetPluginsList();
		Assert.NotNull(pluginsList);
		Assert.Single(pluginsList);
		Assert.Contains(plugin, pluginsList);
	}

	[Fact]
	public void AddPlugin_WithNullPlugin_ThrowsArgumentNullException()
	{
		// Arrange
		var manager = CreatePluginManager();

		// Act & Assert
		Assert.Throws<ArgumentNullException>(() => manager.AddPlugin(null!));
	}

	[Fact]
	public void AddPlugin_WithDuplicatePlugin_ThrowsInvalidOperationException()
	{
		// Arrange
		var manager = CreatePluginManager();
		var plugin = new TestPlugin();
		manager.AddPlugin(plugin);

		// Act & Assert
		var ex = Assert.Throws<InvalidOperationException>(() => manager.AddPlugin(plugin));
		Assert.Contains("already exists", ex.Message);
	}

	[Fact]
	public void AddPlugin_WhenStartThrows_RemovesPluginAndThrows()
	{
		// Arrange
		var manager = CreatePluginManager();
		var plugin = new TestPlugin { ThrowOnStart = true };

		// Act & Assert
		var ex = Assert.Throws<InvalidOperationException>(() => manager.AddPlugin(plugin));
		Assert.Contains("error occurred while starting", ex.Message);
		
		// Plugin should not be in the list
		var pluginsList = manager.GetPluginsList();
		Assert.NotNull(pluginsList);
		Assert.Empty(pluginsList);
	}

	[Fact]
	public void RemovePlugin_WithExistingPlugin_RemovesAndStopsPlugin()
	{
		// Arrange
		var manager = CreatePluginManager();
		var plugin = new TestPlugin();
		manager.AddPlugin(plugin);

		// Act
		var result = manager.RemovePlugin(plugin);

		// Assert
		Assert.True(result);
		Assert.True(plugin.StopCalled);
		Assert.NotNull(plugin.LastCacheOnStop);
		
		var pluginsList = manager.GetPluginsList();
		Assert.NotNull(pluginsList);
		Assert.Empty(pluginsList);
	}

	[Fact]
	public void RemovePlugin_WithNonExistentPlugin_ReturnsFalse()
	{
		// Arrange
		var manager = CreatePluginManager();
		var plugin = new TestPlugin();

		// Act
		var result = manager.RemovePlugin(plugin);

		// Assert
		Assert.False(result);
		Assert.False(plugin.StopCalled);
	}

	[Fact]
	public void RemovePlugin_WithNullPlugin_ThrowsArgumentNullException()
	{
		// Arrange
		var manager = CreatePluginManager();

		// Act & Assert
		Assert.Throws<ArgumentNullException>(() => manager.RemovePlugin(null!));
	}

	[Fact]
	public void RemovePlugin_WhenStopThrows_RemovesPluginButThrows()
	{
		// Arrange
		var manager = CreatePluginManager();
		var plugin = new TestPlugin { ThrowOnStop = true };
		manager.AddPlugin(plugin);

		// Act & Assert
		var ex = Assert.Throws<InvalidOperationException>(() => manager.RemovePlugin(plugin));
		Assert.Contains("error occurred while stopping", ex.Message);
		
		// Plugin should still be removed from the list despite the exception
		var pluginsList = manager.GetPluginsList();
		Assert.NotNull(pluginsList);
		Assert.Empty(pluginsList);
	}

	[Fact]
	public void RemoveAllPlugins_RemovesAllPlugins()
	{
		// Arrange
		var manager = CreatePluginManager();
		var plugin1 = new TestPlugin();
		var plugin2 = new TestPlugin();
		var plugin3 = new TestPlugin();
		
		manager.AddPlugin(plugin1);
		manager.AddPlugin(plugin2);
		manager.AddPlugin(plugin3);

		// Act
		manager.RemoveAllPlugins();

		// Assert
		Assert.True(plugin1.StopCalled);
		Assert.True(plugin2.StopCalled);
		Assert.True(plugin3.StopCalled);
		
		var pluginsList = manager.GetPluginsList();
		Assert.NotNull(pluginsList);
		Assert.Empty(pluginsList);
	}

	[Fact]
	public void RemoveAllPlugins_WithNoPlugins_DoesNotThrow()
	{
		// Arrange
		var manager = CreatePluginManager();

		// Act & Assert (should not throw)
		manager.RemoveAllPlugins();
	}

	[Fact]
	public void GetPluginsList_ReturnsCurrentPlugins()
	{
		// Arrange
		var manager = CreatePluginManager();
		var plugin1 = new TestPlugin();
		var plugin2 = new TestPlugin();
		
		// Act & Assert - Initially empty
		var initialList = manager.GetPluginsList();
		Assert.NotNull(initialList);
		Assert.Empty(initialList);

		// Add plugins
		manager.AddPlugin(plugin1);
		manager.AddPlugin(plugin2);
		
		// Act & Assert - Contains plugins
		var withPlugins = manager.GetPluginsList();
		Assert.NotNull(withPlugins);
		Assert.Equal(2, withPlugins.Count);
		Assert.Contains(plugin1, withPlugins);
		Assert.Contains(plugin2, withPlugins);

		// Remove one plugin
		manager.RemovePlugin(plugin1);
		
		// Act & Assert - Contains remaining plugin
		var afterRemoval = manager.GetPluginsList();
		Assert.NotNull(afterRemoval);
		Assert.Single(afterRemoval);
		Assert.Contains(plugin2, afterRemoval);
		Assert.DoesNotContain(plugin1, afterRemoval);
	}

	[Fact]
	public void Dispose_CallsRemoveAllPlugins()
	{
		// Arrange
		var manager = CreatePluginManager();
		var plugin1 = new TestPlugin();
		var plugin2 = new TestPlugin();
		
		manager.AddPlugin(plugin1);
		manager.AddPlugin(plugin2);

		// Act
		manager.Dispose();

		// Assert
		Assert.True(plugin1.StopCalled);
		Assert.True(plugin2.StopCalled);
		
		var pluginsList = manager.GetPluginsList();
		Assert.NotNull(pluginsList);
		Assert.Empty(pluginsList);
	}

	[Fact]
	public void PluginOperations_AreThreadSafe()
	{
		// Arrange
		var manager = CreatePluginManager();
		var plugins = Enumerable.Range(0, 10).Select(_ => new TestPlugin()).ToList();
		var exceptions = new List<Exception>();

		// Act - Add plugins concurrently
		var addTasks = plugins.Select(plugin => Task.Run(() =>
		{
			try
			{
				manager.AddPlugin(plugin);
			}
			catch (Exception ex)
			{
				lock (exceptions)
				{
					exceptions.Add(ex);
				}
			}
		})).ToArray();

		Task.WaitAll(addTasks);

		// Assert
		Assert.Empty(exceptions);
		var pluginsList = manager.GetPluginsList();
		Assert.NotNull(pluginsList);
		Assert.Equal(10, pluginsList.Count);
		
		// Act - Remove plugins concurrently
		var removeTasks = plugins.Select(plugin => Task.Run(() =>
		{
			try
			{
				manager.RemovePlugin(plugin);
			}
			catch (Exception ex)
			{
				lock (exceptions)
				{
					exceptions.Add(ex);
				}
			}
		})).ToArray();

		Task.WaitAll(removeTasks);

		// Assert
		Assert.Empty(exceptions);
		pluginsList = manager.GetPluginsList();
		Assert.NotNull(pluginsList);
		Assert.Empty(pluginsList);
	}

	[Fact]
	public void AddPlugin_WithLogging_LogsCorrectly()
	{
		// Arrange
		var logger = CreateListLogger<FusionCache>(LogLevel.Trace);
		var options = new FusionCacheOptions();
		var cache = new FusionCache(Options.Create(options), logger: logger);
		var manager = CreatePluginManager(cache, options, logger);
		var plugin = new TestPlugin();

		// Act
		manager.AddPlugin(plugin);

		// Assert
		var logs = logger.Items;
		Assert.NotEmpty(logs);
		// Should have logs about plugin being added
		Assert.Contains(logs, log => log.Message.Contains("plugin has been added"));
	}

	[Fact]
	public void RemovePlugin_WithLogging_LogsCorrectly()
	{
		// Arrange
		var logger = CreateListLogger<FusionCache>(LogLevel.Trace);
		var options = new FusionCacheOptions();
		var cache = new FusionCache(Options.Create(options), logger: logger);
		var manager = CreatePluginManager(cache, options, logger);
		var plugin = new TestPlugin();
		
		manager.AddPlugin(plugin);
		logger.Items.Clear(); // Clear add logs

		// Act
		manager.RemovePlugin(plugin);

		// Assert
		var logs = logger.Items;
		Assert.NotEmpty(logs);
		// Should have logs about plugin being removed
		Assert.Contains(logs, log => log.Message.Contains("plugin has been stopped and removed"));
	}
}