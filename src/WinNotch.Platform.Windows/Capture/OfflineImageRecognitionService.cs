using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using WinNotch.Capture;
using ZXing;
using ZXing.Common;

namespace WinNotch.Platform.Windows.Capture;

public sealed class OfflineImageRecognitionService
{
    public async Task<OcrDocument> RecognizeTextAsync(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(memory.ToArray());
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? throw new NotSupportedException("No Windows OCR language pack is installed.");
        var result = await engine.RecognizeAsync(softwareBitmap);
        var regions = result.Lines
            .SelectMany(line => line.Words)
            .Select(word => new OcrTextRegion(
                word.Text,
                (int)Math.Round(word.BoundingRect.X),
                (int)Math.Round(word.BoundingRect.Y),
                (int)Math.Round(word.BoundingRect.Width),
                (int)Math.Round(word.BoundingRect.Height)))
            .ToArray();
        return new OcrDocument(result.Text, regions, engine.RecognizerLanguage.LanguageTag);
    }

    public IReadOnlyList<QrCodeResult> DecodeQrCodes(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        using var source = bitmap.Clone(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            PixelFormat.Format24bppRgb);
        var data = source.LockBits(
            new Rectangle(0, 0, source.Width, source.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            int rowBytes = source.Width * 3;
            var pixels = new byte[rowBytes * source.Height];
            for (int row = 0; row < source.Height; row++)
                Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * rowBytes, rowBytes);
            var luminance = new RGBLuminanceSource(
                pixels, source.Width, source.Height, RGBLuminanceSource.BitmapFormat.BGR24);
            var reader = new MultiFormatReader();
            var hints = new Dictionary<DecodeHintType, object>
            {
                [DecodeHintType.POSSIBLE_FORMATS] = new[] { BarcodeFormat.QR_CODE },
                [DecodeHintType.TRY_HARDER] = true
            };
            var result = reader.decode(new BinaryBitmap(new HybridBinarizer(luminance)), hints);
            return result == null ? [] : [new QrCodeResult(result.Text, result.BarcodeFormat.ToString())];
        }
        catch (ReaderException) { return []; }
        finally { source.UnlockBits(data); }
    }
}
