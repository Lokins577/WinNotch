using System.Text.Json.Serialization;

namespace WinNotch.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotchDock
{
    Top,
    Left,
    Right
}
