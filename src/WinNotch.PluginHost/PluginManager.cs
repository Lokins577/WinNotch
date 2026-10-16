using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace WinNotch.Plugins;

/// <summary>
/// Manages plugin loading, initialization, and lifecycle.
/// </summary>
public class PluginManager : IDisposable
{
    private readonly IPluginContext _context;
    private readonly Version _hostVersion;
    private readonly List<IPlugin> _loadedPlugins = new();
    private readonly Dictionary<string, bool> _pluginEnabledState = new();
    private readonly Dictionary<string, string> _pluginSourceFiles = new();
    private readonly string _pluginsDirectory;

    private const string UninstallMarkerExtension = ".uninstall";

    public IReadOnlyList<IPlugin> LoadedPlugins => _loadedPlugins.AsReadOnly();

    public event Action<IPlugin>? PluginLoaded;

    /// <summary>Fired after a plugin is enabled or disabled (for live UI refresh).</summary>
    public event Action? PluginStateChanged;
    public event Action<IPlugin>? PluginUnloaded;
    public event Action<IPlugin, Exception>? PluginError;

    public PluginManager(IPluginContext context, string hostVersion)
    {
        _context = context;
        _hostVersion = Version.TryParse(hostVersion, out var parsedVersion)
            ? parsedVersion
            : new Version(0, 0, 0);

        _pluginsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinNotch",
            "Plugins");

        Directory.CreateDirectory(_pluginsDirectory);
        LoadPluginStates();
    }

    /// <summary>
    /// Discover and load all plugins from the plugins directory.
    /// </summary>
    public async Task LoadAllPluginsAsync()
    {
        _context.Log("Starting plugin discovery...", PluginLogLevel.Info);

        // Remove plugins queued for uninstall, then apply any pending updates
        // (both were blocked by file locks while the previous instance ran)
        ApplyQueuedUninstalls();
        ApplyPendingUpdates();

        var pluginFiles = Directory.GetFiles(_pluginsDirectory, "*.dll", SearchOption.AllDirectories);

        foreach (var file in pluginFiles)
        {
            try
            {
                await LoadPluginFromFileAsync(file);
            }
            catch (Exception ex)
            {
                _context.Log($"Failed to load plugin from {file}: {ex.Message}", PluginLogLevel.Error);
            }
        }

        _context.Log($"Loaded {_loadedPlugins.Count} plugins", PluginLogLevel.Info);
    }

    /// <summary>
    /// Load a plugin from a DLL file.
    /// </summary>
    public async Task<IPlugin?> LoadPluginFromFileAsync(string filePath)
    {
        try
        {
            _context.Log($"Loading plugin from: {filePath}", PluginLogLevel.Debug);

            var assembly = Assembly.LoadFrom(filePath);
            List<Type> pluginTypes;
            try
            {
                pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                    .ToList();
            }
            catch (ReflectionTypeLoadException ex)
            {
                _context.Log($"Type load failure in {filePath}: {ex.Message}", PluginLogLevel.Error);
                foreach (var loaderException in ex.LoaderExceptions.Where(exception => exception != null))
                {
                    _context.Log($"Loader exception for {Path.GetFileName(filePath)}: {loaderException!.Message}", PluginLogLevel.Error);
                }

                throw;
            }

            if (pluginTypes.Count == 0)
            {
                _context.Log($"No plugin types found in {filePath}", PluginLogLevel.Warning);
                return null;
            }

            IPlugin? firstLoaded = null;

            // Load every plugin type in the assembly, not just the first one
            foreach (var type in pluginTypes)
            {
                var plugin = Activator.CreateInstance(type) as IPlugin;
                if (plugin == null) continue;

                // Check if plugin is already loaded
                if (_loadedPlugins.Any(p => p.Id == plugin.Id))
                {
                    _context.Log($"Plugin {plugin.Id} is already loaded", PluginLogLevel.Warning);
                    continue;
                }

                // Verify minimum WinNotch version
                if (!IsVersionCompatible(plugin.MinimumWinNotchVersion))
                {
                    _context.Log($"Plugin {plugin.Id} requires WinNotch {plugin.MinimumWinNotchVersion} or higher", PluginLogLevel.Error);
                    continue;
                }

                // Initialize the plugin
                await plugin.InitializeAsync(_context);
                _loadedPlugins.Add(plugin);
                _pluginSourceFiles[plugin.Id] = filePath;

                // Auto-enable if previously enabled or first time
                if (!_pluginEnabledState.ContainsKey(plugin.Id))
                {
                    _pluginEnabledState[plugin.Id] = true; // Default to enabled
                }

                if (_pluginEnabledState[plugin.Id])
                {
                    await plugin.OnEnableAsync();
                }

                PluginLoaded?.Invoke(plugin);
                _context.Log($"Successfully loaded plugin: {plugin.Name} v{plugin.Version}", PluginLogLevel.Info);

                firstLoaded ??= plugin;
            }

            return firstLoaded;
        }
        catch (Exception ex)
        {
            _context.Log($"Error loading plugin from {filePath}: {ex.Message}", PluginLogLevel.Error);
            throw;
        }
    }

    /// <summary>
    /// Enable a plugin by ID.
    /// </summary>
    public async Task<bool> EnablePluginAsync(string pluginId)
    {
        var plugin = _loadedPlugins.FirstOrDefault(p => p.Id == pluginId);
        if (plugin == null) return false;

        try
        {
            await plugin.OnEnableAsync();
            _pluginEnabledState[pluginId] = true;
            SavePluginStates();
            PluginStateChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _context.Log($"Error enabling plugin {pluginId}: {ex.Message}", PluginLogLevel.Error);
            PluginError?.Invoke(plugin, ex);
            return false;
        }
    }

    /// <summary>
    /// Disable a plugin by ID.
    /// </summary>
    public async Task<bool> DisablePluginAsync(string pluginId)
    {
        var plugin = _loadedPlugins.FirstOrDefault(p => p.Id == pluginId);
        if (plugin == null) return false;

        try
        {
            await plugin.OnDisableAsync();
            _pluginEnabledState[pluginId] = false;
            SavePluginStates();
            PluginStateChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _context.Log($"Error disabling plugin {pluginId}: {ex.Message}", PluginLogLevel.Error);
            PluginError?.Invoke(plugin, ex);
            return false;
        }
    }

    /// <summary>
    /// Unload a plugin by ID.
    /// </summary>
    public async Task<bool> UnloadPluginAsync(string pluginId)
    {
        var plugin = _loadedPlugins.FirstOrDefault(p => p.Id == pluginId);
        if (plugin == null) return false;

        try
        {
            if (_pluginEnabledState.GetValueOrDefault(pluginId))
            {
                await plugin.OnDisableAsync();
            }

            await plugin.ShutdownAsync();
            plugin.Dispose();

            _loadedPlugins.Remove(plugin);
            PluginUnloaded?.Invoke(plugin);

            _context.Log($"Unloaded plugin: {plugin.Name}", PluginLogLevel.Info);
            return true;
        }
        catch (Exception ex)
        {
            _context.Log($"Error unloading plugin {pluginId}: {ex.Message}", PluginLogLevel.Error);
            PluginError?.Invoke(plugin, ex);
            return false;
        }
    }

    /// <summary>
    /// Check if a plugin is enabled.
    /// </summary>
    public bool IsPluginEnabled(string pluginId)
    {
        return _pluginEnabledState.GetValueOrDefault(pluginId, false);
    }

    /// <summary>
    /// Get all UI plugins.
    /// </summary>
    public IEnumerable<IUIPlugin> GetUIPlugins()
    {
        return _loadedPlugins.OfType<IUIPlugin>()
            .Where(p => _pluginEnabledState.GetValueOrDefault(p.Id, false));
    }

    /// <summary>
    /// Get all service plugins.
    /// </summary>
    public IEnumerable<IServicePlugin> GetServicePlugins()
    {
        return _loadedPlugins.OfType<IServicePlugin>()
            .Where(p => _pluginEnabledState.GetValueOrDefault(p.Id, false));
    }

    /// <summary>
    /// Get all animation plugins.
    /// </summary>
    public IEnumerable<IAnimationPlugin> GetAnimationPlugins()
    {
        return _loadedPlugins.OfType<IAnimationPlugin>()
            .Where(p => _pluginEnabledState.GetValueOrDefault(p.Id, false));
    }

    /// <summary>
    /// Get plugin directory path.
    /// </summary>
    public string GetPluginsDirectory() => _pluginsDirectory;

    private bool IsVersionCompatible(string requiredVersion)
    {
        try
        {
            var required = new Version(requiredVersion);
            return _hostVersion >= required;
        }
        catch
        {
            return true; // If parsing fails, allow it
        }
    }

    private void LoadPluginStates()
    {
        try
        {
            var statePath = Path.Combine(_pluginsDirectory, "plugin-states.json");
            if (File.Exists(statePath))
            {
                var json = File.ReadAllText(statePath);
                var states = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
                if (states != null)
                {
                    foreach (var kvp in states)
                    {
                        _pluginEnabledState[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _context.Log($"Failed to load plugin states: {ex.Message}", PluginLogLevel.Warning);
        }
    }

    /// <summary>
    /// Queue a plugin for removal on next restart (the loaded DLL is file-locked).
    /// Writes a marker next to the DLL; ApplyPendingUpdates deletes both at startup.
    /// </summary>
    public bool QueueUninstall(string pluginId)
    {
        if (!_pluginSourceFiles.TryGetValue(pluginId, out var sourceFile))
            return false;

        try
        {
            File.WriteAllText(sourceFile + UninstallMarkerExtension, pluginId);
            return true;
        }
        catch (Exception ex)
        {
            _context.Log($"Failed to queue uninstall for {pluginId}: {ex.Message}", PluginLogLevel.Error);
            return false;
        }
    }

    /// <summary>Cancel a queued removal.</summary>
    public bool CancelUninstall(string pluginId)
    {
        if (!_pluginSourceFiles.TryGetValue(pluginId, out var sourceFile))
            return false;

        try
        {
            var marker = sourceFile + UninstallMarkerExtension;
            if (File.Exists(marker))
                File.Delete(marker);
            return true;
        }
        catch (Exception ex)
        {
            _context.Log($"Failed to cancel uninstall for {pluginId}: {ex.Message}", PluginLogLevel.Error);
            return false;
        }
    }

    /// <summary>Whether the plugin is queued for removal on next restart.</summary>
    public bool IsUninstallQueued(string pluginId)
    {
        return _pluginSourceFiles.TryGetValue(pluginId, out var sourceFile)
            && File.Exists(sourceFile + UninstallMarkerExtension);
    }

    private void ApplyQueuedUninstalls()
    {
        try
        {
            var markers = Directory.GetFiles(_pluginsDirectory, "*" + UninstallMarkerExtension, SearchOption.AllDirectories);
            foreach (var marker in markers)
            {
                var dllPath = marker[..^UninstallMarkerExtension.Length];
                try
                {
                    if (File.Exists(dllPath))
                        File.Delete(dllPath);
                    File.Delete(marker);

                    // Remove the plugin's directory if nothing meaningful is left
                    var dir = Path.GetDirectoryName(dllPath);
                    if (dir != null && dir != _pluginsDirectory && Directory.Exists(dir) &&
                        !Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories).Any() &&
                        !Directory.EnumerateFiles(dir, "*.pending", SearchOption.AllDirectories).Any())
                    {
                        Directory.Delete(dir, recursive: true);
                    }

                    _context.Log($"Removed plugin file: {Path.GetFileName(dllPath)}", PluginLogLevel.Info);
                }
                catch (Exception ex)
                {
                    _context.Log($"Failed to remove {dllPath}: {ex.Message}", PluginLogLevel.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            _context.Log($"Failed to scan for queued uninstalls: {ex.Message}", PluginLogLevel.Warning);
        }
    }

    private void ApplyPendingUpdates()
    {
        try
        {
            var pendingFiles = Directory.GetFiles(_pluginsDirectory, "*.pending", SearchOption.AllDirectories);
            foreach (var pendingFile in pendingFiles)
            {
                var targetFile = pendingFile[..^".pending".Length]; // Remove .pending extension
                try
                {
                    if (File.Exists(targetFile))
                        File.Delete(targetFile);
                    File.Move(pendingFile, targetFile);
                    _context.Log($"Applied pending plugin update: {Path.GetFileName(targetFile)}", PluginLogLevel.Info);
                }
                catch (Exception ex)
                {
                    _context.Log($"Failed to apply pending update {pendingFile}: {ex.Message}", PluginLogLevel.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            _context.Log($"Failed to scan for pending updates: {ex.Message}", PluginLogLevel.Warning);
        }
    }

    private void SavePluginStates()
    {
        try
        {
            var statePath = Path.Combine(_pluginsDirectory, "plugin-states.json");
            var json = System.Text.Json.JsonSerializer.Serialize(_pluginEnabledState, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(statePath, json);
        }
        catch (Exception ex)
        {
            _context.Log($"Failed to save plugin states: {ex.Message}", PluginLogLevel.Warning);
        }
    }

    public void Dispose()
    {
        // Synchronous, bounded shutdown: `async void` here would let the process
        // exit before plugins finish (or crash it on a plugin exception).
        foreach (var plugin in _loadedPlugins.ToList())
        {
            try
            {
                var shutdown = plugin.ShutdownAsync();
                if (!shutdown.Wait(TimeSpan.FromSeconds(2)))
                    _context.Log($"Plugin {plugin.Id} shutdown timed out", PluginLogLevel.Warning);
                plugin.Dispose();
            }
            catch (Exception ex)
            {
                _context.Log($"Error disposing plugin {plugin.Id}: {ex.Message}", PluginLogLevel.Error);
            }
        }

        _loadedPlugins.Clear();
        GC.SuppressFinalize(this);
    }
}
