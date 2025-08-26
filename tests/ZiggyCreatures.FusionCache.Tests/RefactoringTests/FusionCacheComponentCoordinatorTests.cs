using System;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Internals;
using ZiggyCreatures.Caching.Fusion.Internals.Memory;
using ZiggyCreatures.Caching.Fusion.Locking;
using ZiggyCreatures.Caching.Fusion.Serialization;
using FusionCacheTests.Stuff;

namespace FusionCacheTests.RefactoringTests;

public class FusionCacheComponentCoordinatorTests : AbstractTests
{
	public FusionCacheComponentCoordinatorTests(ITestOutputHelper output)
		: base(output, null)
	{
	}

	private class MockDistributedCache : IDistributedCache
	{
		public byte[]? Get(string key) => null;
		public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
		public void Refresh(string key) { }
		public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
		public void Remove(string key) { }
		public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
		public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
		public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
	}

	private class MockSerializer : IFusionCacheSerializer
	{
		public byte[] Serialize<T>(T? obj) => Array.Empty<byte>();
		public T? Deserialize<T>(byte[] data) => default;
		public ValueTask<byte[]> SerializeAsync<T>(T? obj, CancellationToken token = default) => new(Array.Empty<byte>());
		public ValueTask<T?> DeserializeAsync<T>(byte[] data, CancellationToken token = default) => new(default(T));
	}

	private class MockBackplane : IFusionCacheBackplane
	{
		public void Subscribe(BackplaneSubscriptionOptions options) { }
		public ValueTask SubscribeAsync(BackplaneSubscriptionOptions options) => default;
		public void Unsubscribe() { }
		public ValueTask UnsubscribeAsync() => default;
		public void Publish(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default) { }
		public ValueTask PublishAsync(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default) => default;
	}

	private FusionCacheComponentCoordinator CreateCoordinator(
		FusionCache? cache = null,
		FusionCacheOptions? options = null,
		ILogger<FusionCache>? logger = null,
		IMemoryCache? memoryCache = null,
		IFusionCacheMemoryLocker? memoryLocker = null)
	{
		options ??= new FusionCacheOptions();
		cache ??= new FusionCache(Options.Create(options));
		var events = new FusionCacheEventsHub(cache, options, logger);
		
		return new FusionCacheComponentCoordinator(cache, options, logger, events, memoryCache, memoryLocker);
	}

	[Fact]
	public void Constructor_WithValidParameters_InitializesCorrectly()
	{
		// Arrange
		var options = new FusionCacheOptions();
		var cache = new FusionCache(Options.Create(options));

		// Act
		var coordinator = CreateCoordinator(cache, options);

		// Assert
		Assert.NotNull(coordinator.MemoryCacheAccessor);
		Assert.NotNull(coordinator.MemoryLocker);
		Assert.False(coordinator.HasDistributedCache);
		Assert.False(coordinator.HasBackplane);
		Assert.Null(coordinator.DistributedCacheAccessor);
		Assert.Null(coordinator.BackplaneAccessor);
	}

	[Fact]
	public void Constructor_WithNullCache_ThrowsArgumentNullException()
	{
		// Arrange
		var options = new FusionCacheOptions();
		var cache = new FusionCache(Options.Create(options));
		var events = new FusionCacheEventsHub(cache, options, null);

		// Act & Assert
		Assert.Throws<ArgumentNullException>(() =>
			new FusionCacheComponentCoordinator(null!, options, null, events, null, null));
	}

	[Fact]
	public void SetupSerializer_WithValidSerializer_SetsSerializer()
	{
		// Arrange
		var coordinator = CreateCoordinator();
		var serializer = new MockSerializer();

		// Act
		coordinator.SetupSerializer(serializer);

		// Assert
		Assert.Same(serializer, coordinator.Serializer);
	}

	[Fact]
	public void SetupSerializer_WithNullSerializer_ThrowsArgumentNullException()
	{
		// Arrange
		var coordinator = CreateCoordinator();

		// Act & Assert
		Assert.Throws<ArgumentNullException>(() => coordinator.SetupSerializer(null!));
	}

	[Fact]
	public void SetupDistributedCache_WithoutSerializer_ThrowsInvalidOperationException()
	{
		// Arrange
		var coordinator = CreateCoordinator();
		var distributedCache = new MockDistributedCache();

		// Act & Assert
		var ex = Assert.Throws<InvalidOperationException>(() => coordinator.SetupDistributedCache(distributedCache));
		Assert.Contains("serializer must be set", ex.Message);
	}

	[Fact]
	public void SetupDistributedCache_WithSerializer_SetsUpDistributedCache()
	{
		// Arrange
		var coordinator = CreateCoordinator();
		var serializer = new MockSerializer();
		var distributedCache = new MockDistributedCache();

		// Act
		coordinator.SetupSerializer(serializer);
		coordinator.SetupDistributedCache(distributedCache);

		// Assert
		Assert.True(coordinator.HasDistributedCache);
		Assert.NotNull(coordinator.DistributedCacheAccessor);
		Assert.Same(distributedCache, coordinator.DistributedCache);
	}

	[Fact]
	public void SetupDistributedCache_WithBothParameters_SetsUpBoth()
	{
		// Arrange
		var coordinator = CreateCoordinator();
		var serializer = new MockSerializer();
		var distributedCache = new MockDistributedCache();

		// Act
		coordinator.SetupDistributedCache(distributedCache, serializer);

		// Assert
		Assert.True(coordinator.HasDistributedCache);
		Assert.Same(serializer, coordinator.Serializer);
		Assert.Same(distributedCache, coordinator.DistributedCache);
	}

	[Fact]
	public void RemoveDistributedCache_RemovesDistributedCache()
	{
		// Arrange
		var coordinator = CreateCoordinator();
		var serializer = new MockSerializer();
		var distributedCache = new MockDistributedCache();
		coordinator.SetupDistributedCache(distributedCache, serializer);

		// Act
		coordinator.RemoveDistributedCache();

		// Assert
		Assert.False(coordinator.HasDistributedCache);
		Assert.Null(coordinator.DistributedCacheAccessor);
		Assert.Null(coordinator.DistributedCache);
		// Serializer should remain
		Assert.Same(serializer, coordinator.Serializer);
	}

	[Fact]
	public void SetupBackplane_WithValidBackplane_SetsUpBackplane()
	{
		// Arrange
		var options = new FusionCacheOptions { WaitForInitialBackplaneSubscribe = true };
		var coordinator = CreateCoordinator(options: options);
		var backplane = new MockBackplane();
		var defaultEntryOptions = new FusionCacheEntryOptions();

		// Act
		coordinator.SetupBackplane(backplane, defaultEntryOptions);

		// Assert
		Assert.True(coordinator.HasBackplane);
		Assert.NotNull(coordinator.BackplaneAccessor);
		Assert.Same(backplane, coordinator.Backplane);
	}

	[Fact]
	public void SetupBackplane_WithNullBackplane_ThrowsArgumentNullException()
	{
		// Arrange
		var coordinator = CreateCoordinator();
		var defaultEntryOptions = new FusionCacheEntryOptions();

		// Act & Assert
		Assert.Throws<ArgumentNullException>(() => coordinator.SetupBackplane(null!, defaultEntryOptions));
	}

	[Fact]
	public void SetupBackplane_RemovesExistingBackplane_BeforeSettingNew()
	{
		// Arrange
		var options = new FusionCacheOptions { WaitForInitialBackplaneSubscribe = true };
		var coordinator = CreateCoordinator(options: options);
		var backplane1 = new MockBackplane();
		var backplane2 = new MockBackplane();
		var defaultEntryOptions = new FusionCacheEntryOptions();

		// Act
		coordinator.SetupBackplane(backplane1, defaultEntryOptions);
		coordinator.SetupBackplane(backplane2, defaultEntryOptions);

		// Assert
		Assert.True(coordinator.HasBackplane);
		Assert.Same(backplane2, coordinator.Backplane);
	}

	[Fact]
	public void RemoveBackplane_RemovesBackplane()
	{
		// Arrange
		var options = new FusionCacheOptions { WaitForInitialBackplaneSubscribe = true };
		var coordinator = CreateCoordinator(options: options);
		var backplane = new MockBackplane();
		var defaultEntryOptions = new FusionCacheEntryOptions();
		coordinator.SetupBackplane(backplane, defaultEntryOptions);

		// Act
		coordinator.RemoveBackplane();

		// Assert
		Assert.False(coordinator.HasBackplane);
		Assert.Null(coordinator.BackplaneAccessor);
		Assert.Null(coordinator.Backplane);
	}

	[Fact]
	public void CanExecuteRawClear_WithNoComponents_ReturnsTrue()
	{
		// Arrange
		var coordinator = CreateCoordinator();

		// Act
		var result = coordinator.CanExecuteRawClear();

		// Assert
		Assert.True(result);
	}

	[Fact]
	public void CanExecuteRawClear_WithDistributedCache_ReturnsFalse()
	{
		// Arrange
		var coordinator = CreateCoordinator();
		var serializer = new MockSerializer();
		var distributedCache = new MockDistributedCache();
		coordinator.SetupDistributedCache(distributedCache, serializer);

		// Act
		var result = coordinator.CanExecuteRawClear();

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void CanExecuteRawClear_WithBackplane_ReturnsFalse()
	{
		// Arrange
		var options = new FusionCacheOptions { WaitForInitialBackplaneSubscribe = true };
		var coordinator = CreateCoordinator(options: options);
		var backplane = new MockBackplane();
		var defaultEntryOptions = new FusionCacheEntryOptions();
		coordinator.SetupBackplane(backplane, defaultEntryOptions);

		// Act
		var result = coordinator.CanExecuteRawClear();

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void AutoRecovery_LazilyInitializes()
	{
		// Arrange
		var coordinator = CreateCoordinator();

		// Act
		var autoRecovery1 = coordinator.AutoRecovery;
		var autoRecovery2 = coordinator.AutoRecovery;

		// Assert
		Assert.NotNull(autoRecovery1);
		Assert.Same(autoRecovery1, autoRecovery2); // Should be the same instance
	}

	[Fact]
	public void Dispose_CleansUpResources()
	{
		// Arrange
		var options = new FusionCacheOptions { WaitForInitialBackplaneSubscribe = true };
		var coordinator = CreateCoordinator(options: options);
		var serializer = new MockSerializer();
		var distributedCache = new MockDistributedCache();
		var backplane = new MockBackplane();
		var defaultEntryOptions = new FusionCacheEntryOptions();

		coordinator.SetupDistributedCache(distributedCache, serializer);
		coordinator.SetupBackplane(backplane, defaultEntryOptions);

		// Trigger auto-recovery initialization
		var autoRecovery = coordinator.AutoRecovery;

		// Act
		coordinator.Dispose();

		// Assert
		Assert.False(coordinator.HasDistributedCache);
		Assert.False(coordinator.HasBackplane);
		Assert.Null(coordinator.DistributedCacheAccessor);
		Assert.Null(coordinator.BackplaneAccessor);
	}

	[Fact]
	public void MemoryCacheCanClear_ReflectsMemoryCacheCapability()
	{
		// Arrange
		var coordinator = CreateCoordinator();

		// Act
		var canClear = coordinator.MemoryCacheCanClear;

		// Assert
		// This should be true for the default memory cache
		Assert.True(canClear);
	}
}