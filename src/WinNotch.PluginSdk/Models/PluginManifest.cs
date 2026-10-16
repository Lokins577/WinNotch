using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinNotch.Plugins;

/// <summary>
/// Metadata about a plugin from the plugin library.
/// </summary>
public class PluginManifest
{
    /// <summary>Unique plugin identifier.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Plugin version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Author name.</summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>Description of the plugin.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Minimum WinNotch version required.</summary>
    [JsonPropertyName("minimumWinNotchVersion")]
    public string MinimumWinNotchVersion { get; set; } = "0.2.3";

    /// <summary>Direct download URL for the plugin DLL.</summary>
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Homepage or repository URL.</summary>
    [JsonPropertyName("homepage")]
    public string Homepage { get; set; } = string.Empty;

    /// <summary>Icon URL (optional).</summary>
    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    /// <summary>Plugin category.</summary>
    [JsonPropertyName("category")]
    public PluginCategory Category { get; set; } = PluginCategory.Other;

    /// <summary>Required permissions.</summary>
    [JsonPropertyName("permissions")]
    public string[] Permissions { get; set; } = Array.Empty<string>();

    /// <summary>Plugin dependencies (other plugin IDs).</summary>
    [JsonPropertyName("dependencies")]
    public string[] Dependencies { get; set; } = Array.Empty<string>();

    /// <summary>SHA256 hash of the DLL for verification (optional but recommended).</summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    /// <summary>Release date.</summary>
    [JsonPropertyName("releaseDate")]
    public DateTime? ReleaseDate { get; set; }

    /// <summary>Whether this plugin is verified/official.</summary>
    [JsonPropertyName("isVerified")]
    public bool IsVerified { get; set; }
}

[JsonConverter(typeof(PluginCategoryJsonConverter))]
public enum PluginCategory
{
    Animation,
    Integration,
    Productivity,
    Media,
    SystemUtility,
    Theme,
    Widget,
    Fun,
    Other
}

internal sealed class PluginCategoryJsonConverter : JsonConverter<PluginCategory>
{
    public override PluginCategory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return PluginCategory.Other;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var rawValue = reader.GetString();
            if (TryParse(rawValue, out var category))
            {
                return category;
            }

            return PluginCategory.Other;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numericValue))
        {
            if (Enum.IsDefined(typeof(PluginCategory), numericValue))
            {
                return (PluginCategory)numericValue;
            }

            return PluginCategory.Other;
        }

        throw new JsonException($"Unexpected token {reader.TokenType} when parsing plugin category.");
    }

    public override void Write(Utf8JsonWriter writer, PluginCategory value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    private static bool TryParse(string? rawValue, out PluginCategory category)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            category = PluginCategory.Other;
            return false;
        }

        var normalized = rawValue.Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        return normalized.ToLowerInvariant() switch
        {
            "animation" => Match(PluginCategory.Animation, out category),
            "integration" => Match(PluginCategory.Integration, out category),
            "productivity" => Match(PluginCategory.Productivity, out category),
            "media" => Match(PluginCategory.Media, out category),
            "systemutility" => Match(PluginCategory.SystemUtility, out category),
            "system" => Match(PluginCategory.SystemUtility, out category),
            "utility" => Match(PluginCategory.SystemUtility, out category),
            "theme" => Match(PluginCategory.Theme, out category),
            "widget" => Match(PluginCategory.Widget, out category),
            "fun" => Match(PluginCategory.Fun, out category),
            "other" => Match(PluginCategory.Other, out category),
            _ => Enum.TryParse(rawValue, ignoreCase: true, out category)
        };
    }

    private static bool Match(PluginCategory categoryValue, out PluginCategory category)
    {
        category = categoryValue;
        return true;
    }
}
