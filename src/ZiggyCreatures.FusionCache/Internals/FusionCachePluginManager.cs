using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Plugins;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Manages the complete plugin lifecycle including addition, removal, starting, and stopping of cache plugins.
/// This component handles all plugin-related operations with proper error handling and logging.
/// </summary>
internal sealed class FusionCachePluginManager : IDisposable
{
	private readonly FusionCache _cache;
	private readonly FusionCacheOptions _options;
	private readonly ILogger<FusionCache>? _logger;
	private List<IFusionCachePlugin>? _plugins;
	private readonly object _pluginsLock = new();

	/// <summary>
	/// Initializes a new instance of the FusionCachePluginManager.
	/// </summary>
	/// <param name="cache">The FusionCache instance that owns this plugin manager.</param>
	/// <param name="options">The cache options.</param>
	/// <param name="logger">The logger instance.</param>
	public FusionCachePluginManager(FusionCache cache, FusionCacheOptions options, ILogger<FusionCache>? logger)
	{
		_cache = cache ?? throw new ArgumentNullException(nameof(cache));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_logger = logger;
		_plugins = [];
	}

	/// <summary>
	/// Adds a plugin to the cache and starts it.
	/// </summary>
	/// <param name="plugin">The plugin to add.</param>
	/// <exception cref="ArgumentNullException">Thrown when plugin is null.</exception>
	/// <exception cref="InvalidOperationException">Thrown when plugin already exists or fails to start.</exception>
	public void AddPlugin(IFusionCachePlugin plugin)
	{
		if (plugin is null)
			throw new ArgumentNullException(nameof(plugin));

		// ADD THE PLUGIN
		lock (_pluginsLock)
		{
			_plugins ??= [];

			if (_plugins.Contains(plugin))
			{
				if (_logger?.IsEnabled(_options.PluginsErrorsLogLevel) ?? false)
					_logger?.Log(_options.PluginsErrorsLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: the same plugin instance already exists (TYPE={PluginType})", _options.CacheName, _options.InstanceId, plugin.GetType().FullName);

				throw new InvalidOperationException($"FUSION [N={_options.CacheName}]: the same plugin instance already exists (TYPE={plugin.GetType().FullName})");
			}

			_plugins.Add(plugin);
		}

		// START THE PLUGIN
		try
		{
			plugin.Start(_cache);
		}
		catch (Exception exc)
		{
			lock (_pluginsLock)
			{
				_plugins.Remove(plugin);
			}

			if (_logger?.IsEnabled(_options.PluginsErrorsLogLevel) ?? false)
				_logger.Log(_options.PluginsErrorsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}]: an error occurred while starting a plugin (TYPE={PluginType})", _options.CacheName, _options.InstanceId, plugin.GetType().FullName);

			throw new InvalidOperationException($"FUSION [N={_options.CacheName}]: an error occurred while starting a plugin (TYPE={plugin.GetType().FullName})", exc);
		}

		if (_logger?.IsEnabled(_options.PluginsInfoLogLevel) ?? false)
			_logger?.Log(_options.PluginsInfoLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: a plugin has been added and started (TYPE={PluginType})", _options.CacheName, _options.InstanceId, plugin.GetType().FullName);
	}

	/// <summary>
	/// Removes a plugin from the cache and stops it.
	/// </summary>
	/// <param name="plugin">The plugin to remove.</param>
	/// <returns>True if the plugin was successfully removed, false if it wasn't found.</returns>
	/// <exception cref="ArgumentNullException">Thrown when plugin is null.</exception>
	/// <exception cref="InvalidOperationException">Thrown when plugin fails to stop.</exception>
	public bool RemovePlugin(IFusionCachePlugin plugin)
	{
		if (plugin is null)
			throw new ArgumentNullException(nameof(plugin));

		lock (_pluginsLock)
		{
			_plugins ??= [];

			if (_plugins.Contains(plugin) == false)
			{
				if (_logger?.IsEnabled(_options.PluginsErrorsLogLevel) ?? false)
					_logger?.Log(_options.PluginsErrorsLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: the plugin cannot be removed because is not part of this FusionCache instance (TYPE={PluginType})", _options.CacheName, _options.InstanceId, plugin.GetType().FullName);

				return false;
			}

			// STOP THE PLUGIN
			try
			{
				plugin.Stop(_cache);
			}
			catch (Exception exc)
			{
				if (_logger?.IsEnabled(_options.PluginsErrorsLogLevel) ?? false)
					_logger.Log(_options.PluginsErrorsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}]: an error occurred while stopping a plugin (TYPE={PluginType})", _options.CacheName, _options.InstanceId, plugin.GetType().FullName);

				throw new InvalidOperationException($"FUSION [N={_options.CacheName}]: an error occurred while stopping a plugin (TYPE={plugin.GetType().FullName})", exc);
			}
			finally
			{
				// REMOVE THE PLUGIN
				_plugins.Remove(plugin);
			}
		}

		if (_logger?.IsEnabled(_options.PluginsInfoLogLevel) ?? false)
			_logger?.Log(_options.PluginsInfoLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: a plugin has been stopped and removed (TYPE={PluginType})", _options.CacheName, _options.InstanceId, plugin.GetType().FullName);

		return true;
	}

	/// <summary>
	/// Removes all plugins from the cache.
	/// </summary>
	public void RemoveAllPlugins()
	{
		if (_plugins is null)
			return;

		foreach (var plugin in _plugins.ToArray())
		{
			RemovePlugin(plugin);
		}
	}

	/// <summary>
	/// Gets a list of all active plugins for backward compatibility.
	/// </summary>
	/// <returns>A list of active plugins or null if none exist.</returns>
	public List<IFusionCachePlugin>? GetPluginsList()
	{
		lock (_pluginsLock)
		{
			return _plugins;
		}
	}

	/// <summary>
	/// Disposes the plugin manager by removing all plugins.
	/// </summary>
	public void Dispose()
	{
		RemoveAllPlugins();
	}
}