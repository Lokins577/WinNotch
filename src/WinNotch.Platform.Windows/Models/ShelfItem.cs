using System;
using System.Windows.Media;

namespace WinNotch.Models;

/// <summary>
/// Represents a file dropped onto the notch shelf.
/// </summary>
public class ShelfItem
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public ImageSource? Icon { get; set; }
    public bool IsFolder { get; set; }
}
