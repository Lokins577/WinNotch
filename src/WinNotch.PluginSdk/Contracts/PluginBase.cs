using System;
using System.Threading.Tasks;

namespace WinNotch.Plugins;

/// <summary>
/// Abstract base class for plugins with default implementations.
/// Inherit from this to create a plugin more easily.
/// </summary>
public abstract class PluginBase : IPlugin
{
    protected IPluginContext? Context { get; private set; }

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Version { get; }
    public abstract string Author { get; }
    public abstract string Description { get; }
    public virtual string MinimumWinNotchVersion => "0.2.3";

    public virtual Task InitializeAsync(IPluginContext context)
    {
        Context = context;
        Context.Log($"Initializing plugin: {Name} v{Version}", PluginLogLevel.Info);
        return Task.CompletedTask;
    }

    public virtual Task OnEnableAsync()
    {
        Context?.Log($"Enabled plugin: {Name}", PluginLogLevel.Info);
        return Task.CompletedTask;
    }

    public virtual Task OnDisableAsync()
    {
        Context?.Log($"Disabled plugin: {Name}", PluginLogLevel.Info);
        return Task.CompletedTask;
    }

    public virtual Task ShutdownAsync()
    {
        Context?.Log($"Shutting down plugin: {Name}", PluginLogLevel.Info);
        return Task.CompletedTask;
    }

    protected void OpenPluginManagerConfiguration()
    {
        Context?.OpenConfiguration(Id);
    }

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
