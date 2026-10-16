using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using WinNotch.Models;

namespace WinNotch.Services;

/// <summary>
/// Manages the file shelf — temporary file staging area on the notch.
/// </summary>
public class ShelfService
{
    public ObservableCollection<ShelfItem> Items { get; } = new();
    private readonly string _storagePath;

    public event Action? ShelfChanged;

    public bool HasItems => Items.Count > 0;

    public ShelfService(string? storagePath = null)
    {
        _storagePath = Path.GetFullPath(storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinNotch", "Shelf", "pinned.json"));
        LoadPinned();
    }

    public void AddFiles(string[] paths)
    {
        foreach (var path in paths)
        {
            // Avoid duplicates
            bool exists = false;
            foreach (var item in Items)
            {
                if (string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
            if (exists) continue;

            bool isFolder = Directory.Exists(path);
            var name = isFolder ? new DirectoryInfo(path).Name : Path.GetFileName(path);
            var ext = isFolder ? "" : Path.GetExtension(path);

            var item2 = new ShelfItem
            {
                FilePath = path,
                FileName = name,
                Extension = ext,
                IsFolder = isFolder,
                Icon = GetFileIcon(path, isFolder),
                AddedAt = DateTime.Now
            };

            Items.Add(item2);
        }

        ShelfChanged?.Invoke();
    }

    public void Remove(ShelfItem item)
    {
        Items.Remove(item);
        SavePinned();
        ShelfChanged?.Invoke();
    }

    public void SetPinned(ShelfItem item, bool pinned)
    {
        int index = Items.IndexOf(item);
        if (index < 0 || item.IsPinned == pinned) return;
        item.IsPinned = pinned;
        Items.RemoveAt(index);
        Items.Insert(index, item);
        SavePinned();
        ShelfChanged?.Invoke();
    }

    public void Clear()
    {
        foreach (var item in Items.Where(item => !item.IsPinned).ToArray())
            Items.Remove(item);
        ShelfChanged?.Invoke();
    }

    private void LoadPinned()
    {
        try
        {
            if (!File.Exists(_storagePath)) return;
            var records = JsonSerializer.Deserialize<List<PinnedRecord>>(File.ReadAllText(_storagePath)) ?? [];
            foreach (var record in records.Where(record => !string.IsNullOrWhiteSpace(record.Path)))
            {
                string path = record.Path;
                bool isFolder = record.IsFolder || Directory.Exists(path);
                bool exists = Directory.Exists(path) || File.Exists(path);
                Items.Add(new ShelfItem
                {
                    Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
                    FilePath = path,
                    FileName = isFolder ? new DirectoryInfo(path).Name : Path.GetFileName(path),
                    Extension = isFolder ? string.Empty : Path.GetExtension(path),
                    AddedAt = record.AddedAt,
                    IsFolder = isFolder,
                    IsPinned = true,
                    IsMissing = !exists,
                    Icon = exists ? GetFileIcon(path, isFolder) : null
                });
            }
        }
        catch
        {
            // A corrupt shelf file must not prevent the island from starting.
        }
    }

    private void SavePinned()
    {
        try
        {
            string? directory = Path.GetDirectoryName(_storagePath);
            if (directory == null) return;
            Directory.CreateDirectory(directory);
            var records = Items.Where(item => item.IsPinned)
                .Select(item => new PinnedRecord(item.Id, item.FilePath, item.AddedAt, item.IsFolder))
                .ToArray();
            string temporary = _storagePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(records, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            File.Move(temporary, _storagePath, overwrite: true);
        }
        catch
        {
            // Persistence is best effort; live shelf items remain usable.
        }
    }

    private sealed record PinnedRecord(Guid Id, string Path, DateTime AddedAt, bool IsFolder);

    /// <summary>
    /// Extract the system file icon as an ImageSource.
    /// </summary>
    private static ImageSource? GetFileIcon(string path, bool isFolder)
    {
        try
        {
            Icon? icon;
            if (isFolder)
            {
                // Use shell to get folder icon
                var shInfo = new NativeMethods.SHFILEINFO();
                NativeMethods.SHGetFileInfo(path, 0, ref shInfo,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf(shInfo),
                    NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);
                if (shInfo.hIcon == IntPtr.Zero) return null;
                icon = System.Drawing.Icon.FromHandle(shInfo.hIcon);
            }
            else
            {
                // Use shell for file icon too (gets the associated app icon)
                var shInfo = new NativeMethods.SHFILEINFO();
                NativeMethods.SHGetFileInfo(path, 0, ref shInfo,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf(shInfo),
                    NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);
                if (shInfo.hIcon == IntPtr.Zero) return null;
                icon = System.Drawing.Icon.FromHandle(shInfo.hIcon);
            }

            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();

            NativeMethods.DestroyIcon(icon.Handle);
            return bitmapSource;
        }
        catch
        {
            return null;
        }
    }
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_LARGEICON = 0x000000000;

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
