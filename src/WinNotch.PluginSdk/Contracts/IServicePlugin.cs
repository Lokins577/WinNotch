using System.Threading.Tasks;

namespace WinNotch.Plugins;

/// <summary>
/// Interface for plugins that provide background services or features without UI.
/// Examples: API integrations, system monitoring, automation tasks.
/// </summary>
public interface IServicePlugin : IPlugin
{
    /// <summary>
    /// Start the background service.
    /// </summary>
    Task StartServiceAsync();

    /// <summary>
    /// Stop the background service.
    /// </summary>
    Task StopServiceAsync();
}
