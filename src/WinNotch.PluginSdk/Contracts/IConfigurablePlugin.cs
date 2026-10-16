using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WinNotch.Plugins;

/// <summary>
/// Interface for plugins that expose configurable settings to the WinNotch plugin manager.
/// </summary>
public interface IConfigurablePlugin : IPlugin
{
    /// <summary>
    /// Returns the current configuration schema and values for the plugin manager UI.
    /// </summary>
    PluginConfigurationDefinition GetConfigurationDefinition();

    /// <summary>
    /// Applies the submitted configuration values.
    /// </summary>
    Task<PluginConfigurationResult> ApplyConfigurationAsync(IReadOnlyDictionary<string, string?> values);
}

public sealed class PluginConfigurationDefinition
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public bool RequiresConfiguration { get; init; }
    public bool IsConfigured { get; init; }
    public IReadOnlyList<PluginConfigurationSection> Sections { get; init; } = Array.Empty<PluginConfigurationSection>();
}

public sealed class PluginConfigurationSection
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<PluginConfigurationField> Fields { get; init; } = Array.Empty<PluginConfigurationField>();
}

public sealed class PluginConfigurationField
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string HelpText { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Placeholder { get; init; } = string.Empty;
    public PluginConfigurationFieldType FieldType { get; init; } = PluginConfigurationFieldType.Text;
    public bool IsRequired { get; init; }
    public IReadOnlyList<PluginConfigurationOption> Options { get; init; } = Array.Empty<PluginConfigurationOption>();
}

public sealed class PluginConfigurationOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public enum PluginConfigurationFieldType
{
    Text,
    Multiline,
    Number,
    Choice
}

public sealed class PluginConfigurationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    public static PluginConfigurationResult Ok(string message)
    {
        return new PluginConfigurationResult
        {
            Success = true,
            Message = message
        };
    }

    public static PluginConfigurationResult Fail(string message)
    {
        return new PluginConfigurationResult
        {
            Success = false,
            Message = message
        };
    }
}
