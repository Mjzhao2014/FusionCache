using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Plugins;

namespace ZiggyCreatures.Caching.Fusion.Internals;

/// <summary>
/// Internal helper responsible for managing plugin lifecycle on a FusionCache instance:
/// addition, removal, start/stop and disposal.
/// </summary>
internal class FusionCachePluginManager
{
    private readonly ILogger<FusionCache>? _logger;
    private readonly FusionCacheOptions _options;
    private readonly object _pluginsLock = new();
    internal List<IFusionCachePlugin> _plugins = new();
    private readonly FusionCache _owner;

    public FusionCachePluginManager(FusionCache owner, FusionCacheOptions options, ILogger<FusionCache>? logger)
    {
        _owner = owner;
        _options = options;
        _logger = logger;
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
                    _logger.Log(_options.PluginsErrorsLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: the same plugin instance already exists (TYPE={PluginType})", _options.CacheName, _options.InstanceId, plugin.GetType().FullName);
                throw new InvalidOperationException($"FUSION [N={_options.CacheName}]: the same plugin instance already exists (TYPE={plugin.GetType().FullName})");
            }
            _plugins.Add(plugin);
        }
        try
        {
            plugin.Start(_owner);
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
            _logger.Log(_options.PluginsInfoLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: a plugin has been added and started (TYPE={PluginType})", _options.CacheName, _options.InstanceId, plugin.GetType().FullName);
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
                    _logger.Log(_options.PluginsErrorsLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: the plugin cannot be removed because is not part of this FusionCache instance (TYPE={PluginType})", _options.CacheName, _options.InstanceId, plugin.GetType().FullName);
                return false;
            }
            try
            {
                plugin.Stop(_owner);
            }
            catch (Exception exc)
            {
                if (_logger?.IsEnabled(_options.PluginsErrorsLogLevel) ?? false)
                    _logger.Log(_options.PluginsErrorsLogLevel, exc, "FUSION [N={CacheName} I={CacheInstanceId}]: an error occurred while stopping a plugin (TYPE={PluginType})", _options.CacheName, _options.InstanceId, plugin.GetType().FullName);
                throw new InvalidOperationException($"FUSION [N={_options.CacheName}]: an error occurred while stopping a plugin (TYPE={plugin.GetType().FullName})", exc);
            }
            finally
            {
                _plugins.Remove(plugin);
            }
        }
        if (_logger?.IsEnabled(_options.PluginsInfoLogLevel) ?? false)
            _logger.Log(_options.PluginsInfoLogLevel, "FUSION [N={CacheName} I={CacheInstanceId}]: a plugin has been stopped and removed (TYPE={PluginType})", _options.CacheName, _options.InstanceId, plugin.GetType().FullName);
        return true;
    }

    public IList<IFusionCachePlugin> GetPluginsList()
    {
        lock (_pluginsLock)
        {
            // return the actual list for compatibility with tests reflecting on FusionCache._plugins
            return _plugins;
        }
    }

    public void RemoveAllPlugins()
    {
        foreach (var plugin in GetPluginsList().ToArray())
        {
            RemovePlugin(plugin);
        }
    }
}
