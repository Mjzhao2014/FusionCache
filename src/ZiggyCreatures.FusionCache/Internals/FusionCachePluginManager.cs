using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Plugins;

namespace ZiggyCreatures.Caching.Fusion.Internals
{
    /// <summary>
    /// Helper class responsible for managing the addition/removal and lifecycle of FusionCache plugins.
    /// </summary>
    internal sealed class FusionCachePluginManager : IDisposable
    {
        private readonly FusionCache _cache;
        private readonly FusionCacheOptions _options;
        private readonly ILogger<FusionCache>? _logger;
        private readonly List<IFusionCachePlugin> _plugins = new();
        private readonly object _lock = new();

        public FusionCachePluginManager(FusionCache cache, FusionCacheOptions options, ILogger<FusionCache>? logger)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
        }

        public void AddPlugin(IFusionCachePlugin plugin)
        {
            if (plugin is null)
                throw new ArgumentNullException(nameof(plugin));

            lock (_lock)
            {
                if (_plugins.Contains(plugin))
                {
                    if (_logger?.IsEnabled(_options.PluginsErrorsLogLevel) ?? false)
                        _logger.Log(_options.PluginsErrorsLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: the same plugin instance already exists (TYPE={PluginType})", _cache.CacheName, _cache.InstanceId, plugin.GetType().FullName);

                    throw new InvalidOperationException($"FUSION [N={_cache.CacheName}]: the same plugin instance already exists (TYPE={plugin.GetType().FullName})");
                }

                _plugins.Add(plugin);
            }

            try
            {
                plugin.Start(_cache);
            }
            catch (Exception exc)
            {
                lock (_lock)
                {
                    _plugins.Remove(plugin);
                }

                if (_logger?.IsEnabled(_options.PluginsErrorsLogLevel) ?? false)
                    _logger.Log(_options.PluginsErrorsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}]: an error occurred while starting a plugin (TYPE={PluginType})", _cache.CacheName, _cache.InstanceId, plugin.GetType().FullName);

                throw new InvalidOperationException($"FUSION [N={_cache.CacheName}]: an error occurred while starting a plugin (TYPE={plugin.GetType().FullName})", exc);
            }

            if (_logger?.IsEnabled(_options.PluginsInfoLogLevel) ?? false)
                _logger?.Log(_options.PluginsInfoLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: a plugin has been added and started (TYPE={PluginType})", _cache.CacheName, _cache.InstanceId, plugin.GetType().FullName);
        }

        public bool RemovePlugin(IFusionCachePlugin plugin)
        {
            if (plugin is null)
                throw new ArgumentNullException(nameof(plugin));

            lock (_lock)
            {
                if (_plugins.Contains(plugin) == false)
                {
                    if (_logger?.IsEnabled(_options.PluginsErrorsLogLevel) ?? false)
                        _logger.Log(_options.PluginsErrorsLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: the plugin cannot be removed because is not part of this FusionCache instance (TYPE={PluginType})", _cache.CacheName, _cache.InstanceId, plugin.GetType().FullName);
                    return false;
                }

                try
                {
                    plugin.Stop(_cache);
                }
                catch (Exception exc)
                {
                    if (_logger?.IsEnabled(_options.PluginsErrorsLogLevel) ?? false)
                        _logger.Log(_options.PluginsErrorsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}]: an error occurred while stopping a plugin (TYPE={PluginType})", _cache.CacheName, _cache.InstanceId, plugin.GetType().FullName);
                    throw new InvalidOperationException($"FUSION [N={_cache.CacheName}]: an error occurred while stopping a plugin (TYPE={plugin.GetType().FullName})", exc);
                }
                finally
                {
                    _plugins.Remove(plugin);
                }
            }

            if (_logger?.IsEnabled(_options.PluginsInfoLogLevel) ?? false)
                _logger?.Log(_options.PluginsInfoLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: a plugin has been stopped and removed (TYPE={PluginType})", _cache.CacheName, _cache.InstanceId, plugin.GetType().FullName);

            return true;
        }

        public void RemoveAllPlugins()
        {
            lock (_lock)
            {
                if (_plugins.Count == 0)
                    return;

                // Snapshot to avoid issues while iterating
                var snapshot = _plugins.ToArray();
                foreach (var plugin in snapshot)
                {
                    RemovePlugin(plugin);
                }
            }
        }

        /// <summary>
        /// Returns the underlying plugin list (for tests/backwards compatibility).
        /// </summary>
		public List<IFusionCachePlugin> GetPluginsList()
		{
			// return the internal collection for introspection/backwards-compat
			return _plugins;
		}

        public void Dispose()
        {
            RemoveAllPlugins();
        }
    }
}
