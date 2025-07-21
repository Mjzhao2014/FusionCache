using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.NullObjects;

namespace FusionCacheTests;

public partial class GeneralTests
{
	[Fact]
	public void CanUseNullFusionCache()
	{
		using var cache = new NullFusionCache(new FusionCacheOptions()
		{
			CacheName = "SlothsAreCool42",
			DefaultEntryOptions = new FusionCacheEntryOptions()
			{
				IsFailSafeEnabled = true,
				Duration = TimeSpan.FromMinutes(123)
			}
		});

		cache.Set<int>("foo", 42, token: TestContext.Current.CancellationToken);

		var maybeFoo1 = cache.TryGet<int>("foo", token: TestContext.Current.CancellationToken);

		cache.Remove("foo", token: TestContext.Current.CancellationToken);

		var maybeBar1 = cache.TryGet<int>("bar", token: TestContext.Current.CancellationToken);

		cache.Expire("qux", token: TestContext.Current.CancellationToken);

		var qux1 = cache.GetOrSet("qux", _ => 1, token: TestContext.Current.CancellationToken);
		var qux2 = cache.GetOrSet("qux", _ => 2, token: TestContext.Current.CancellationToken);
		var qux3 = cache.GetOrSet("qux", _ => 3, token: TestContext.Current.CancellationToken);
		var qux4 = cache.GetOrDefault("qux", 4, token: TestContext.Current.CancellationToken);

		Assert.Equal("SlothsAreCool42", cache.CacheName);
		Assert.False(string.IsNullOrWhiteSpace(cache.InstanceId));

		Assert.False(cache.HasDistributedCache);
		Assert.False(cache.HasBackplane);

		Assert.True(cache.DefaultEntryOptions.IsFailSafeEnabled);
		Assert.Equal(TimeSpan.FromMinutes(123), cache.DefaultEntryOptions.Duration);

		Assert.False(maybeFoo1.HasValue);
		Assert.False(maybeBar1.HasValue);

		Assert.Equal(1, qux1);
		Assert.Equal(2, qux2);
		Assert.Equal(3, qux3);
		Assert.Equal(4, qux4);

		Assert.Throws<UnreachableException>(() =>
		{
			_ = cache.GetOrSet<int>("qux", _ => throw new UnreachableException("Sloths"), token: TestContext.Current.CancellationToken);
		});
	}

	[Fact]
	public void CanUseDefaultEntryOptionsProvider()
	{
		using var cache = new FusionCache(new FusionCacheOptions()
		{
			DefaultEntryOptions = new FusionCacheEntryOptions()
			{
				IsFailSafeEnabled = true,
				Duration = TimeSpan.FromMinutes(123)
			},
			DefaultEntryOptionsProvider = new MyEntryOptionsProvider()
		});

		bool isFactoryRun = false;
		bool isFailSafeEnabled = false;
		TimeSpan duration = TimeSpan.Zero;
		CacheItemPriority priority = CacheItemPriority.NeverRemove;

		// GET OR SET: KEY MATCHING FOO
		isFactoryRun = false;
		isFailSafeEnabled = false;
		duration = TimeSpan.Zero;
		priority = CacheItemPriority.NeverRemove;
		cache.GetOrSet<int>(
			"my-foo-01",
			(ctx, ct) =>
			{
				isFactoryRun = true;
				isFailSafeEnabled = ctx.Options.IsFailSafeEnabled;
				duration = ctx.Options.Duration;
				priority = ctx.Options.Priority;

				return 42;
			},
			token: TestContext.Current.CancellationToken
		);

		Assert.True(isFactoryRun);
		Assert.False(isFailSafeEnabled);
		Assert.Equal(TimeSpan.FromMinutes(456), duration);
		Assert.Equal(CacheItemPriority.Low, priority);

		// GET OR SET: KEY MATCHING FOO, BUT OPTIONS DIRECTLY SPECIFIED
		isFactoryRun = false;
		isFailSafeEnabled = false;
		duration = TimeSpan.Zero;
		priority = CacheItemPriority.NeverRemove;
		cache.GetOrSet<int>(
			"my-foo-02",
			(ctx, ct) =>
			{
				isFactoryRun = true;
				isFailSafeEnabled = ctx.Options.IsFailSafeEnabled;
				duration = ctx.Options.Duration;
				priority = ctx.Options.Priority;

				return 42;
			},
			new FusionCacheEntryOptions()
			{
				IsFailSafeEnabled = false,
				Duration = TimeSpan.FromMinutes(21),
				Priority = CacheItemPriority.NeverRemove
			},
			token: TestContext.Current.CancellationToken
		);

		Assert.True(isFactoryRun);
		Assert.False(isFailSafeEnabled);
		Assert.Equal(TimeSpan.FromMinutes(21), duration);
		Assert.Equal(CacheItemPriority.NeverRemove, priority);

		// GET OR SET: KEY MATCHING FOO, BUT OPTIONS DIRECTLY SPECIFIED
		isFactoryRun = false;
		isFailSafeEnabled = false;
		duration = TimeSpan.Zero;
		priority = CacheItemPriority.NeverRemove;
		cache.GetOrSet<int>(
			"my-foo-03",
			(ctx, ct) =>
			{
				isFactoryRun = true;
				isFailSafeEnabled = ctx.Options.IsFailSafeEnabled;
				duration = ctx.Options.Duration;
				priority = ctx.Options.Priority;

				return 42;
			},
			options =>
			{
				// EMPTY
			},
			token: TestContext.Current.CancellationToken
		);

		Assert.True(isFactoryRun);
		Assert.False(isFailSafeEnabled);
		Assert.Equal(TimeSpan.FromMinutes(456), duration);
		Assert.Equal(CacheItemPriority.Low, priority);

		// GET OR SET: KEY MATCHING BAR
		isFactoryRun = false;
		isFailSafeEnabled = false;
		duration = TimeSpan.Zero;
		priority = CacheItemPriority.NeverRemove;
		cache.GetOrSet<int>(
			"my-bar-01",
			(ctx, ct) =>
			{
				isFactoryRun = true;
				isFailSafeEnabled = ctx.Options.IsFailSafeEnabled;
				duration = ctx.Options.Duration;
				priority = ctx.Options.Priority;

				return 42;
			},
			token: TestContext.Current.CancellationToken
		);

		Assert.True(isFactoryRun);
		Assert.False(isFailSafeEnabled);
		Assert.Equal(TimeSpan.FromMinutes(789), duration);
		Assert.Equal(CacheItemPriority.High, priority);

		// GET OR SET: KEY MATCHING NOTHING -> DEFAULT OPTIONS
		isFactoryRun = false;
		isFailSafeEnabled = false;
		duration = TimeSpan.Zero;
		priority = CacheItemPriority.NeverRemove;
		cache.GetOrSet<int>(
			"whatever-01",
			(ctx, ct) =>
			{
				isFactoryRun = true;
				isFailSafeEnabled = ctx.Options.IsFailSafeEnabled;
				duration = ctx.Options.Duration;
				priority = ctx.Options.Priority;

				return 42;
			},
			token: TestContext.Current.CancellationToken
		);

		Assert.True(isFactoryRun);
		Assert.True(isFailSafeEnabled);
		Assert.Equal(TimeSpan.FromMinutes(123), duration);
		Assert.Equal(CacheItemPriority.Normal, priority);

		//// SET: KEY MATCHING FOO
		//await cache.SetAsync<int>(
		//	"my-foo-01",
		//	async (ctx, ct) =>
		//	{
		//		isFailSafeEnabled = ctx.Options.IsFailSafeEnabled;
		//		duration = ctx.Options.Duration;
		//		priority = ctx.Options.Priority;

		//		return 42;
		//	},
		//	token: TestContext.Current.CancellationToken
		//);

		//Assert.False(isFailSafeEnabled);
		//Assert.Equal(TimeSpan.FromMinutes(456), duration);
		//Assert.Equal(CacheItemPriority.Low, priority);
	}

	[Fact]
	public void SlidingExpirationWorksCorrectly()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// SET WITH SLIDING EXPIRATION
		cache.Set<int>("foo", 42, opt => opt.SetSliding(TimeSpan.FromMilliseconds(500)), token: TestContext.Current.CancellationToken);

		// IMMEDIATELY AVAILABLE
		var value1 = cache.GetOrDefault<int>("foo", -1, token: TestContext.Current.CancellationToken);
		Assert.Equal(42, value1);

		// WAIT LESS THAN SLIDING DURATION BUT ACCESS TO RESET TIMER
		Thread.Sleep(300);
		var value2 = cache.GetOrDefault<int>("foo", -1, token: TestContext.Current.CancellationToken);
		Assert.Equal(42, value2);

		// WAIT LESS THAN SLIDING DURATION AGAIN
		Thread.Sleep(300);
		var value3 = cache.GetOrDefault<int>("foo", -1, token: TestContext.Current.CancellationToken);
		Assert.Equal(42, value3);

		// NOW WAIT LONGER THAN SLIDING DURATION WITHOUT ACCESS
		Thread.Sleep(600);
		var value4 = cache.GetOrDefault<int>("foo", -1, token: TestContext.Current.CancellationToken);
		Assert.Equal(-1, value4); // Should be expired
	}

	[Fact]
	public void SlidingExpirationWithFactoryWorksCorrectly()
	{
		using var cache = new FusionCache(new FusionCacheOptions());
		int factoryCalls = 0;

		// GET OR SET WITH SLIDING EXPIRATION
		var value1 = cache.GetOrSet<int>("foo", _ => ++factoryCalls, opt => opt.SetSliding(TimeSpan.FromMilliseconds(500)), token: TestContext.Current.CancellationToken);
		Assert.Equal(1, value1);
		Assert.Equal(1, factoryCalls);

		// ACCESS WITHIN SLIDING WINDOW - SHOULD USE CACHED VALUE
		Thread.Sleep(300);
		var value2 = cache.GetOrSet<int>("foo", _ => ++factoryCalls, opt => opt.SetSliding(TimeSpan.FromMilliseconds(500)), token: TestContext.Current.CancellationToken);
		Assert.Equal(1, value2);
		Assert.Equal(1, factoryCalls); // Factory should not be called again

		// ACCESS AGAIN WITHIN SLIDING WINDOW
		Thread.Sleep(300);
		var value3 = cache.GetOrSet<int>("foo", _ => ++factoryCalls, opt => opt.SetSliding(TimeSpan.FromMilliseconds(500)), token: TestContext.Current.CancellationToken);
		Assert.Equal(1, value3);
		Assert.Equal(1, factoryCalls); // Factory should not be called again

		// WAIT LONGER THAN SLIDING DURATION WITHOUT ACCESS
		Thread.Sleep(600);
		var value4 = cache.GetOrSet<int>("foo", _ => ++factoryCalls, opt => opt.SetSliding(TimeSpan.FromMilliseconds(500)), token: TestContext.Current.CancellationToken);
		Assert.Equal(2, value4); // New value from factory
		Assert.Equal(2, factoryCalls); // Factory should be called again
	}

	[Fact]
	public void SlidingExpirationWithCustomDurationWorksCorrectly()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// SET WITH DURATION AND DIFFERENT SLIDING EXPIRATION
		cache.Set<int>("foo", 42, opt => opt.SetDuration(TimeSpan.FromMinutes(5)).SetSliding(TimeSpan.FromMilliseconds(300)), token: TestContext.Current.CancellationToken);

		// IMMEDIATELY AVAILABLE
		var value1 = cache.GetOrDefault<int>("foo", -1, token: TestContext.Current.CancellationToken);
		Assert.Equal(42, value1);

		// WAIT LESS THAN SLIDING DURATION
		Thread.Sleep(200);
		var value2 = cache.GetOrDefault<int>("foo", -1, token: TestContext.Current.CancellationToken);
		Assert.Equal(42, value2);

		// WAIT MORE THAN SLIDING DURATION
		Thread.Sleep(400);
		var value3 = cache.GetOrDefault<int>("foo", -1, token: TestContext.Current.CancellationToken);
		Assert.Equal(-1, value3); // Should be expired due to sliding expiration
	}

	[Fact]
	public void SlidingExpirationWorksWithFailSafe()
	{
		using var cache = new FusionCache(new FusionCacheOptions());
		int factoryCalls = 0;

		// SET WITH SLIDING EXPIRATION AND FAIL-SAFE
		var value1 = cache.GetOrSet<int>("foo", _ => ++factoryCalls, 
			opt => opt.SetSliding(TimeSpan.FromMilliseconds(300))
			          .SetFailSafe(true, TimeSpan.FromMinutes(10)), 
			token: TestContext.Current.CancellationToken);
		Assert.Equal(1, value1);
		Assert.Equal(1, factoryCalls);

		// ACCESS WITHIN SLIDING WINDOW
		Thread.Sleep(200);
		var value2 = cache.GetOrSet<int>("foo", _ => ++factoryCalls, 
			opt => opt.SetSliding(TimeSpan.FromMilliseconds(300))
			          .SetFailSafe(true, TimeSpan.FromMinutes(10)), 
			token: TestContext.Current.CancellationToken);
		Assert.Equal(1, value2);
		Assert.Equal(1, factoryCalls);

		// WAIT LONGER THAN SLIDING DURATION BUT FACTORY THROWS
		Thread.Sleep(400);
		var value3 = cache.GetOrSet<int>("foo", _ => throw new Exception("Error"), 
			opt => opt.SetSliding(TimeSpan.FromMilliseconds(300))
			          .SetFailSafe(true, TimeSpan.FromMinutes(10)), 
			token: TestContext.Current.CancellationToken);
		Assert.Equal(1, value3); // Should return stale value due to fail-safe
	}

	[Fact]
	public void SlidingExpirationEnabledFailSafeRenewalTest()
	{
		using var cache = new FusionCache(new FusionCacheOptions());
		int factoryCalls = 0;

		// SET ITEM WITH SHORT DURATION + FAIL-SAFE + SLIDING
		var value1 = cache.GetOrSet<int>("test", _ => ++factoryCalls, 
			opt => opt.SetDuration(TimeSpan.FromMilliseconds(400))  // Short base duration
			          .SetSliding(TimeSpan.FromMilliseconds(600))    // Longer sliding window
			          .SetFailSafe(true, TimeSpan.FromSeconds(5)), 
			token: TestContext.Current.CancellationToken);
		Assert.Equal(1, value1);
		Assert.Equal(1, factoryCalls);

		// ACCESS EVERY 500MS - WITHIN SLIDING WINDOW BUT AFTER BASE DURATION
		Thread.Sleep(500); // Should exceed base duration but within sliding window
		var value2 = cache.GetOrSet<int>("test", _ => ++factoryCalls, 
			opt => opt.SetDuration(TimeSpan.FromMilliseconds(400))
			          .SetSliding(TimeSpan.FromMilliseconds(600))
			          .SetFailSafe(true, TimeSpan.FromSeconds(5)), 
			token: TestContext.Current.CancellationToken);
		Assert.Equal(1, value2); // Should still get cached value due to sliding renewal
		Assert.Equal(1, factoryCalls); // Factory should not be called again

		Thread.Sleep(500);
		var value3 = cache.GetOrSet<int>("test", _ => ++factoryCalls, 
			opt => opt.SetDuration(TimeSpan.FromMilliseconds(400))
			          .SetSliding(TimeSpan.FromMilliseconds(600))
			          .SetFailSafe(true, TimeSpan.FromSeconds(5)), 
			token: TestContext.Current.CancellationToken);
		Assert.Equal(1, value3);
		Assert.Equal(1, factoryCalls);

		// NOW WAIT LONGER THAN SLIDING WINDOW
		Thread.Sleep(700);
		var value4 = cache.GetOrSet<int>("test", _ => ++factoryCalls, 
			opt => opt.SetDuration(TimeSpan.FromMilliseconds(400))
			          .SetSliding(TimeSpan.FromMilliseconds(600))
			          .SetFailSafe(true, TimeSpan.FromSeconds(5)), 
			token: TestContext.Current.CancellationToken);
		Assert.Equal(2, value4); // New value from factory
		Assert.Equal(2, factoryCalls);
	}

	[Fact]
	public void DefaultExpirationExpiresRegardlessOfAccess()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// SET WITH NO SLIDING EXPIRATION (DEFAULT BEHAVIOR)
		cache.Set<int>("test", 42, opt => opt.SetDuration(TimeSpan.FromMilliseconds(500)).SetSliding(false), token: TestContext.Current.CancellationToken);

		// IMMEDIATELY AVAILABLE
		var value1 = cache.GetOrDefault<int>("test", -1, token: TestContext.Current.CancellationToken);
		Assert.Equal(42, value1);

		// ACCESS EVERY 200MS (WELL WITHIN DURATION)
		Thread.Sleep(200);
		var value2 = cache.GetOrDefault<int>("test", -1, token: TestContext.Current.CancellationToken);
		Assert.Equal(42, value2);

		Thread.Sleep(200);
		var value3 = cache.GetOrDefault<int>("test", -1, token: TestContext.Current.CancellationToken);
		Assert.Equal(42, value3);

		// WAIT PAST ORIGINAL DURATION DESPITE FREQUENT ACCESS
		Thread.Sleep(200); // Total time ~600ms, should exceed 500ms duration
		var value4 = cache.GetOrDefault<int>("test", -1, token: TestContext.Current.CancellationToken);
		Assert.Equal(-1, value4); // Should be expired despite frequent access
	}

	[Fact]
	public void SlidingExpirationDoesNotTriggerFactoryOnAccess()
	{
		using var cache = new FusionCache(new FusionCacheOptions());
		int factoryCalls = 0;

		// INITIAL GET OR SET
		var value1 = cache.GetOrSet<int>("test", _ => ++factoryCalls, 
			opt => opt.SetSliding(TimeSpan.FromMilliseconds(600)), 
			token: TestContext.Current.CancellationToken);
		Assert.Equal(1, value1);
		Assert.Equal(1, factoryCalls);

		// ACCESS VIA GetOrDefault - SHOULD NOT TRIGGER FACTORY
		Thread.Sleep(300);
		var value2 = cache.GetOrDefault<int>("test", -999, token: TestContext.Current.CancellationToken);
		Assert.Equal(1, value2); // Same value
		Assert.Equal(1, factoryCalls); // Factory not called

		Thread.Sleep(300);
		var value3 = cache.GetOrDefault<int>("test", -999, token: TestContext.Current.CancellationToken);
		Assert.Equal(1, value3);
		Assert.Equal(1, factoryCalls); // Still not called

		// ACCESS VIA GetOrSet SHOULD ALSO NOT TRIGGER FACTORY DUE TO SLIDING RENEWAL
		Thread.Sleep(300);
		var value4 = cache.GetOrSet<int>("test", _ => ++factoryCalls, 
			opt => opt.SetSliding(TimeSpan.FromMilliseconds(600)), 
			token: TestContext.Current.CancellationToken);
		Assert.Equal(1, value4);
		Assert.Equal(1, factoryCalls); // Factory should still not be called due to renewal

		// WAIT PAST SLIDING WINDOW
		Thread.Sleep(700);
		var value5 = cache.GetOrSet<int>("test", _ => ++factoryCalls, 
			opt => opt.SetSliding(TimeSpan.FromMilliseconds(600)), 
			token: TestContext.Current.CancellationToken);
		Assert.Equal(2, value5);
		Assert.Equal(2, factoryCalls); // Now factory should be called
	}

	[Fact]
	public void SlidingExpirationRemovedManually()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// SET WITH SLIDING EXPIRATION
		cache.Set<int>("test", 42, opt => opt.SetSliding(TimeSpan.FromMinutes(10)), token: TestContext.Current.CancellationToken);

		// VERIFY PRESENT
		var value1 = cache.GetOrDefault<int>("test", -1, token: TestContext.Current.CancellationToken);
		Assert.Equal(42, value1);

		// REMOVE MANUALLY
		cache.Remove("test", token: TestContext.Current.CancellationToken);

		// VERIFY GONE
		var value2 = cache.GetOrDefault<int>("test", -1, token: TestContext.Current.CancellationToken);
		Assert.Equal(-1, value2);
	}

	[Fact]
	public void CanHandleDispose()
	{
		var cache = new FusionCache(new FusionCacheOptions());

		cache.Dispose();

		Assert.Throws<ObjectDisposedException>(() =>
		{
			cache.Set("foo", 42, token: TestContext.Current.CancellationToken);
		});

		Assert.Throws<ObjectDisposedException>(() =>
		{
			cache.TryGet<object>("foo", token: TestContext.Current.CancellationToken);
		});

		Assert.Throws<ObjectDisposedException>(() =>
		{
			cache.GetOrDefault<object>("foo", token: TestContext.Current.CancellationToken);
		});

		Assert.Throws<ObjectDisposedException>(() =>
		{
			cache.Expire("foo", token: TestContext.Current.CancellationToken);
		});

		Assert.Throws<ObjectDisposedException>(() =>
		{
			cache.Remove("foo", token: TestContext.Current.CancellationToken);
		});

		Assert.Throws<ObjectDisposedException>(() =>
		{
			cache.RemoveByTag("tag-1", token: TestContext.Current.CancellationToken);
		});

		Assert.Throws<ObjectDisposedException>(() =>
		{
			cache.RemoveByTag(["tag-1"], token: TestContext.Current.CancellationToken);
		});

		Assert.Throws<ObjectDisposedException>(() =>
		{
			cache.Clear(token: TestContext.Current.CancellationToken);
		});
	}
}
