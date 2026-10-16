using System;
using System.Windows;
using System.Windows.Media;
using WinNotch.Helpers;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private long _lastBackdropCaptureTicks;

    private void ApplyLiquidGlassEffect(bool enable)
    {
        _isLiquidGlassActive = enable;

        if (enable)
        {
            WindowHelper.SetExcludeFromCapture(this, true);

            // Cache DPI once
            if (!_dpiCached)
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                _cachedDpiX = dpi.DpiScaleX;
                _cachedDpiY = dpi.DpiScaleY;
                _dpiCached = true;
            }

            GlassSpecularPath.Opacity = 1.0;
            GlassBorderPath.Opacity = 1.0;
            GlassInnerShadowPath.Opacity = 1.0;
            BackdropBlurPath.Opacity = 1.0;

            // Idempotent: remove first so repeated enables never double-subscribe
            CompositionTarget.Rendering -= OnRenderBackdrop;
            CompositionTarget.Rendering += OnRenderBackdrop;
        }
        else
        {
            CompositionTarget.Rendering -= OnRenderBackdrop;
            WindowHelper.SetExcludeFromCapture(this, false);
            GlassSpecularPath.Opacity = 0;
            GlassBorderPath.Opacity = 0;
            GlassInnerShadowPath.Opacity = 0;
            BackdropBlurPath.Opacity = 0;
            BackdropBlurPath.Fill = null;
            _backdropBrush = null;
            _backdropWb = null;
        }
    }

    private void OnRenderBackdrop(object? sender, EventArgs e)
    {
        if (!_isLiquidGlassActive || _isDragging) return;
        if (_currentWidth < 10 || _currentHeight < 10) return;

        // 30fps is indistinguishable under the blur and halves the capture cost
        long now = Environment.TickCount64;
        if (now - _lastBackdropCaptureTicks < 33) return;
        _lastBackdropCaptureTicks = now;

        try
        {
            double canvasLeft = System.Windows.Controls.Canvas.GetLeft(NotchPath);
            double canvasTop = System.Windows.Controls.Canvas.GetTop(NotchPath);
            if (double.IsNaN(canvasLeft)) canvasLeft = 0;
            if (double.IsNaN(canvasTop)) canvasTop = 0;

            var winRect = WindowHelper.GetWindowRectPhysical(this);
            int px = winRect.Left + (int)(canvasLeft * _cachedDpiX);
            int py = winRect.Top + (int)(canvasTop * _cachedDpiY);
            int pw = (int)(_currentWidth * _cachedDpiX);
            int ph = (int)(_currentHeight * _cachedDpiY);

            if (pw < 4 || ph < 4) return;

            // Ensure WriteableBitmap matches current size
            if (_backdropWb == null || _wbW != pw || _wbH != ph)
            {
                _backdropWb = new System.Windows.Media.Imaging.WriteableBitmap(
                    pw, ph, 96, 96, PixelFormats.Bgra32, null);
                _wbW = pw;
                _wbH = ph;

                _backdropBrush = new ImageBrush(_backdropWb)
                {
                    Stretch = Stretch.Fill,
                    TileMode = TileMode.None
                };
                BackdropBlurPath.Fill = _backdropBrush;
            }

            // Capture directly into the WriteableBitmap (zero alloc)
            // Padding feeds the edge-lens: rim pixels sample from this outside band
            BackdropBlurHelper.CaptureInto(_backdropWb, px, py, pw, ph, padding: 8);
        }
        catch
        {
            // Silently ignore capture failures
        }
    }

    private void CompactVisualizer_SetVisible(bool visible)
    {
        // The visualizer is inside LiveActivity; toggle via its property
        if (LiveActivity.FindName("CompactVisualizer") is UIElement viz)
            viz.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
