using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Plugins;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Manages the lifecycle of all plugins attached to a <see cref="FusionCache"/> instance.
/// Provides thread-safe methods to add/remove plugins, calls their Start/Stop hooks,
/// maintains a local list of active plugins and handles removal via <see cref="Dispose"/>
/// by ensuring each plugin is stopped.
/// </summary>
internal sealed class FusionCachePluginManager
{
    private readonly FusionCache _cache;
    private readonly FusionCacheOptions _options;
    private readonly ILogger<FusionCache>? _logger;
    private readonly object _pluginsLock = new();
    private readonly List<IFusionCachePlugin> _plugins;

    internal FusionCachePluginManager(FusionCache cache, FusionCacheOptions options, ILogger<FusionCache>? logger, List<IFusionCachePlugin> pluginsList)
    {
        _cache = cache;
        _options = options;
        _logger = logger;
        _plugins = pluginsList;
    }

    public void AddPlugin(IFusionCachePlugin plugin)
    {
        if (plugin is null)
            throw new ArgumentNullException(nameof(plugin));
        lock (_pluginsLock)
        {
            if (_plugins.Contains(plugin))
            {
                if (_logger?.IsEnabled(_options.PluginsErrorsLogLevel) ?? false)
                    _logger?.Log(_options.PluginsErrorsLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: the same plugin instance already exists (TYPE={PluginType})", _cache.CacheName, _cache.InstanceId, plugin.GetType().FullName);
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
            lock (_pluginsLock)
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
        lock (_pluginsLock)
        {
            if (_plugins.Contains(plugin) == false)
            {
                if (_logger?.IsEnabled(_options.PluginsErrorsLogLevel) ?? false)
                    _logger?.Log(_options.PluginsErrorsLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: the plugin cannot be removed because is not part of this FusionCache instance (TYPE={PluginType})", _cache.CacheName, _cache.InstanceId, plugin.GetType().FullName);
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
        // snapshot to avoid modification during enumeration
        foreach (var plugin in _plugins.ToArray())
        {
            RemovePlugin(plugin);
        }
    }

    public IFusionCachePlugin[] GetPluginsList()
    {
        return _plugins.ToArray();
    }
}
