using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Dependencies;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion.Internals.Dependencies;

/// <summary>
/// Handles cascade invalidation of dependent cache entries.
/// </summary>
internal sealed class CascadeInvalidator
{
	private readonly FusionCache _cache;
	private readonly DependencyTracker _dependencyTracker;
	private readonly CascadeOptions _cascadeOptions;
	private readonly ILogger? _logger;
	private readonly string _cacheName;
	private readonly string _instanceId;

	public CascadeInvalidator(FusionCache cache, DependencyTracker dependencyTracker, CascadeOptions cascadeOptions, ILogger? logger, string cacheName, string instanceId)
	{
		_cache = cache;
		_dependencyTracker = dependencyTracker;
		_cascadeOptions = cascadeOptions;
		_logger = logger;
		_cacheName = cacheName;
		_instanceId = instanceId;
	}

	/// <summary>
	/// Performs cascade invalidation for a parent key.
	/// </summary>
	/// <param name="parentKey">The parent key that changed.</param>
	/// <param name="operationId">The operation ID for tracking.</param>
	/// <param name="token">Cancellation token.</param>
	/// <param name="sendBackplaneMessage">Whether to send a backplane message for this cascade.</param>
	public void CascadeInvalidateByKey(string parentKey, string operationId, CancellationToken token = default, bool sendBackplaneMessage = true)
	{
		var dependentChildren = _dependencyTracker.GetDependentChildren(parentKey);
		if (dependentChildren.Count == 0)
			return;

		if (_logger?.IsEnabled(LogLevel.Debug) == true)
		{
			_logger.LogDebug("FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): Cascade invalidating {ChildCount} children for parent key {ParentKey}",
				_cacheName, _instanceId, operationId, dependentChildren.Count, parentKey);
		}

		// SEND BACKPLANE MESSAGE
		if (sendBackplaneMessage && _cache.HasBackplane)
		{
			var message = BackplaneMessage.CreateForCascadeInvalidateByKey(_cache.InstanceId, parentKey, FusionCacheInternalUtils.GetCurrentTimestamp());
			var bpa = _cache.BackplaneAccessor;
			if (bpa?.IsCurrentlyUsable(operationId, parentKey) == true)
			{
				bpa.PublishCascadeInvalidateByKey(operationId, parentKey, FusionCacheInternalUtils.GetCurrentTimestamp(), _cache.CreateEntryOptions(), false, false, token);
			}
		}

		CascadeInvalidateInternal(dependentChildren, operationId, 0, token);
	}

	/// <summary>
	/// Performs cascade invalidation for parent tags.
	/// </summary>
	/// <param name="parentTags">The parent tags that changed.</param>
	/// <param name="operationId">The operation ID for tracking.</param>
	/// <param name="token">Cancellation token.</param>
	/// <param name="sendBackplaneMessage">Whether to send a backplane message for this cascade.</param>
	public void CascadeInvalidateByTags(IEnumerable<string> parentTags, string operationId, CancellationToken token = default, bool sendBackplaneMessage = true)
	{
		var dependentChildren = _dependencyTracker.GetDependentChildrenByTags(parentTags);
		if (dependentChildren.Count == 0)
			return;

		if (_logger?.IsEnabled(LogLevel.Debug) == true)
		{
			_logger.LogDebug("FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): Cascade invalidating {ChildCount} children for parent tags {ParentTags}",
				_cacheName, _instanceId, operationId, dependentChildren.Count, string.Join(", ", parentTags));
		}

		// SEND BACKPLANE MESSAGES
		if (sendBackplaneMessage && _cache.HasBackplane)
		{
			var bpa = _cache.BackplaneAccessor;
			if (bpa?.IsCurrentlyUsable(operationId, "") == true)
			{
				foreach (var parentTag in parentTags)
				{
					bpa.PublishCascadeInvalidateByTag(operationId, parentTag, FusionCacheInternalUtils.GetCurrentTimestamp(), _cache.CreateEntryOptions(), false, false, token);
				}
			}
		}

		CascadeInvalidateInternal(dependentChildren, operationId, 0, token);
	}

	/// <summary>
	/// Performs async cascade invalidation for a parent key.
	/// </summary>
	/// <param name="parentKey">The parent key that changed.</param>
	/// <param name="operationId">The operation ID for tracking.</param>
	/// <param name="token">Cancellation token.</param>
	/// <param name="sendBackplaneMessage">Whether to send a backplane message for this cascade.</param>
	public async ValueTask CascadeInvalidateByKeyAsync(string parentKey, string operationId, CancellationToken token = default, bool sendBackplaneMessage = true)
	{
		var dependentChildren = _dependencyTracker.GetDependentChildren(parentKey);
		if (dependentChildren.Count == 0)
			return;

		if (_logger?.IsEnabled(LogLevel.Debug) == true)
		{
			_logger.LogDebug("FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): Cascade invalidating {ChildCount} children for parent key {ParentKey}",
				_cacheName, _instanceId, operationId, dependentChildren.Count, parentKey);
		}

		// SEND BACKPLANE MESSAGE
		if (sendBackplaneMessage && _cache.HasBackplane)
		{
			var message = BackplaneMessage.CreateForCascadeInvalidateByKey(_cache.InstanceId, parentKey, FusionCacheInternalUtils.GetCurrentTimestamp());
			var bpa = _cache.BackplaneAccessor;
			if (bpa?.IsCurrentlyUsable(operationId, parentKey) == true)
			{
				await bpa.PublishCascadeInvalidateByKeyAsync(operationId, parentKey, FusionCacheInternalUtils.GetCurrentTimestamp(), _cache.CreateEntryOptions(), false, false, token).ConfigureAwait(false);
			}
		}

		await CascadeInvalidateInternalAsync(dependentChildren, operationId, 0, token).ConfigureAwait(false);
	}

	/// <summary>
	/// Performs async cascade invalidation for parent tags.
	/// </summary>
	/// <param name="parentTags">The parent tags that changed.</param>
	/// <param name="operationId">The operation ID for tracking.</param>
	/// <param name="token">Cancellation token.</param>
	/// <param name="sendBackplaneMessage">Whether to send a backplane message for this cascade.</param>
	public async ValueTask CascadeInvalidateByTagsAsync(IEnumerable<string> parentTags, string operationId, CancellationToken token = default, bool sendBackplaneMessage = true)
	{
		var dependentChildren = _dependencyTracker.GetDependentChildrenByTags(parentTags);
		if (dependentChildren.Count == 0)
			return;

		if (_logger?.IsEnabled(LogLevel.Debug) == true)
		{
			_logger.LogDebug("FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): Cascade invalidating {ChildCount} children for parent tags {ParentTags}",
				_cacheName, _instanceId, operationId, dependentChildren.Count, string.Join(", ", parentTags));
		}

		// SEND BACKPLANE MESSAGES
		if (sendBackplaneMessage && _cache.HasBackplane)
		{
			var bpa = _cache.BackplaneAccessor;
			if (bpa?.IsCurrentlyUsable(operationId, "") == true)
			{
				foreach (var parentTag in parentTags)
				{
					await bpa.PublishCascadeInvalidateByTagAsync(operationId, parentTag, FusionCacheInternalUtils.GetCurrentTimestamp(), _cache.CreateEntryOptions(), false, false, token).ConfigureAwait(false);
				}
			}
		}

		await CascadeInvalidateInternalAsync(dependentChildren, operationId, 0, token).ConfigureAwait(false);
	}

	private void CascadeInvalidateInternal(IReadOnlyCollection<string> childKeys, string operationId, int depth, CancellationToken token)
	{
		if (depth >= _cascadeOptions.MaxCascadeDepth)
		{
			if (_logger?.IsEnabled(LogLevel.Warning) == true)
			{
				_logger.LogWarning("FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): Cascade invalidation stopped at depth {Depth} (max: {MaxDepth})",
					_cacheName, _instanceId, operationId, depth, _cascadeOptions.MaxCascadeDepth);
			}
			return;
		}

		if (childKeys.Count > _cascadeOptions.MaxCascadeFanout)
		{
			if (_logger?.IsEnabled(LogLevel.Warning) == true)
			{
				_logger.LogWarning("FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): Cascade invalidation limited to {MaxFanout} entries (requested: {RequestedCount})",
					_cacheName, _instanceId, operationId, _cascadeOptions.MaxCascadeFanout, childKeys.Count);
			}
			childKeys = childKeys.Take(_cascadeOptions.MaxCascadeFanout).ToArray();
		}

		foreach (var childKey in childKeys)
		{
			try
			{
				// Get dependent children before removing the entry
				var grandChildren = _dependencyTracker.GetDependentChildren(childKey);

				// Remove the child entry
				_cache.Remove(childKey, token: token);

				// Recursively invalidate grandchildren
				if (grandChildren.Count > 0)
				{
					CascadeInvalidateInternal(grandChildren, operationId, depth + 1, token);
				}
			}
			catch (Exception ex)
			{
				if (_logger?.IsEnabled(LogLevel.Warning) == true)
				{
					_logger.LogWarning(ex, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): Error during cascade invalidation of child key {ChildKey}",
						_cacheName, _instanceId, operationId, childKey);
				}
			}
		}
	}

	private async ValueTask CascadeInvalidateInternalAsync(IReadOnlyCollection<string> childKeys, string operationId, int depth, CancellationToken token)
	{
		if (depth >= _cascadeOptions.MaxCascadeDepth)
		{
			if (_logger?.IsEnabled(LogLevel.Warning) == true)
			{
				_logger.LogWarning("FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): Cascade invalidation stopped at depth {Depth} (max: {MaxDepth})",
					_cacheName, _instanceId, operationId, depth, _cascadeOptions.MaxCascadeDepth);
			}
			return;
		}

		if (childKeys.Count > _cascadeOptions.MaxCascadeFanout)
		{
			if (_logger?.IsEnabled(LogLevel.Warning) == true)
			{
				_logger.LogWarning("FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): Cascade invalidation limited to {MaxFanout} entries (requested: {RequestedCount})",
					_cacheName, _instanceId, operationId, _cascadeOptions.MaxCascadeFanout, childKeys.Count);
			}
			childKeys = childKeys.Take(_cascadeOptions.MaxCascadeFanout).ToArray();
		}

		foreach (var childKey in childKeys)
		{
			try
			{
				// Get dependent children before removing the entry
				var grandChildren = _dependencyTracker.GetDependentChildren(childKey);

				// Remove the child entry
				await _cache.RemoveAsync(childKey, token: token).ConfigureAwait(false);

				// Recursively invalidate grandchildren
				if (grandChildren.Count > 0)
				{
					await CascadeInvalidateInternalAsync(grandChildren, operationId, depth + 1, token).ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				if (_logger?.IsEnabled(LogLevel.Warning) == true)
				{
					_logger.LogWarning(ex, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId}): Error during cascade invalidation of child key {ChildKey}",
						_cacheName, _instanceId, operationId, childKey);
				}
			}
		}
	}
}