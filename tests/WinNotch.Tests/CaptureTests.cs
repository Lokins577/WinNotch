using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using WinNotch.Capture;
using WinNotch.Models;
using WinNotch.Platform.Windows.Capture;
using WinNotch.Settings;
using WinNotch.Views;
using WinNotch.Services;
using Xunit;
using ZXing;
using ZXing.Common;

namespace WinNotch.Tests;

public sealed class CaptureTests
{
    [Theory]
    [InlineData(255, 0, 0, "hsl(0, 100%, 50%)", "hsv(0, 100%, 100%)")]
    [InlineData(128, 128, 128, "hsl(0, 0%, 50%)", "hsv(0, 0%, 50%)")]
    public void ColorFormatter_ProducesStableHslAndHsv(
        byte red, byte green, byte blue, string hsl, string hsv)
    {
        var color = new CapturedColor(red, green, blue);

        Assert.Equal(hsl, color.Hsl);
        Assert.Equal(hsv, color.Hsv);
    }

    [Fact]
    public void QrDecoder_DecodesGeneratedQrCodeOffline()
    {
        const string expected = "https://example.com/winnotch";
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 240, Height = 240, Margin = 2 }
        };
        var pixels = writer.Write(expected);
        using var bitmap = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try { Marshal.Copy(pixels.Pixels, 0, data.Scan0, pixels.Pixels.Length); }
        finally { bitmap.UnlockBits(data); }

        var result = new OfflineImageRecognitionService().DecodeQrCodes(bitmap);

        Assert.Equal(expected, Assert.Single(result).Text);
    }

    [Fact]
    public async Task Ocr_ProcessesBitmapOfflineWhenLanguagePackExists()
    {
        using var bitmap = new Bitmap(160, 60);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("WinNotch", SystemFonts.DefaultFont, Brushes.Black, 8, 18);
        }

        try
        {
            var result = await new OfflineImageRecognitionService().RecognizeTextAsync(bitmap);
            Assert.False(string.IsNullOrWhiteSpace(result.Language));
        }
        catch (NotSupportedException exception)
        {
            Assert.Contains("language pack", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Save_WritesRequestedLocalFormat()
    {
        string root = Path.Combine(Path.GetTempPath(), "winnotch-capture-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "capture.png");
        try
        {
            using var bitmap = new Bitmap(8, 8);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.CornflowerBlue);

            new WindowsCaptureService().Save(bitmap, path, CaptureImageFormat.Png);

            Assert.True(File.Exists(path));
            using var saved = new Bitmap(path);
            Assert.Equal(new Size(8, 8), saved.Size);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void EditorAndPinnedWindow_BuildOnStaThreadWithoutTouchingClipboard()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var bitmap = new Bitmap(40, 30);
                var settings = new AppSettings();
                settings.CapturePreferences.CopyAfterCapture = false;
                var editor = new CaptureEditorWindow(
                    bitmap,
                    new WindowsCaptureService(),
                    new OfflineImageRecognitionService(),
                    settings,
                    new MemoryStore());
                var pinned = new PinnedImageWindow(bitmap);
                using var feature = new CaptureFeatureService();
                var tool = new CaptureToolWindow(feature, settings, new MemoryStore());
                Assert.NotNull(editor.Content);
                Assert.NotNull(pinned.Content);
                Assert.NotNull(tool.Content);
                editor.Close();
                pinned.Close();
                tool.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Capture UI test timed out.");
        Assert.Null(failure);
    }

    private sealed class MemoryStore : IAppSettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
        public void ApplyStartOnBoot(bool enabled) { }
    }
}
