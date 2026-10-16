using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using WinNotch.Helpers;
using Forms = System.Windows.Forms;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace WinNotch.Views;

public partial class RegionCaptureOverlay : Window
{
    private readonly Forms.Screen _screen;
    private readonly double _scale;
    private DrawingPoint _start;
    private bool _dragging;
    public DrawingRectangle? SelectedRegion { get; private set; }

    public RegionCaptureOverlay(Forms.Screen screen)
    {
        _screen = screen;
        _scale = ScreenHelper.GetScaleForScreen(screen);
        InitializeComponent();
        Width = screen.Bounds.Width / _scale;
        Height = screen.Bounds.Height / _scale;
        SourceInitialized += (_, _) => WindowHelper.SetWindowRectPhysical(
            this, screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height);
        Loaded += (_, _) => Focus();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _start = Forms.Cursor.Position;
        _dragging = true;
        Selection.Visibility = Visibility.Visible;
        UpdateSelection(_start);
        CaptureMouse();
    }

    private void OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_dragging) UpdateSelection(Forms.Cursor.Position);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        DrawingPoint end = Forms.Cursor.Position;
        var region = DrawingRectangle.FromLTRB(
            Math.Min(_start.X, end.X), Math.Min(_start.Y, end.Y),
            Math.Max(_start.X, end.X), Math.Max(_start.Y, end.Y));
        if (region.Width >= 2 && region.Height >= 2) SelectedRegion = region;
        DialogResult = SelectedRegion.HasValue;
    }

    private void UpdateSelection(DrawingPoint end)
    {
        double left = (Math.Min(_start.X, end.X) - _screen.Bounds.Left) / _scale;
        double top = (Math.Min(_start.Y, end.Y) - _screen.Bounds.Top) / _scale;
        Canvas.SetLeft(Selection, left);
        Canvas.SetTop(Selection, top);
        Selection.Width = Math.Abs(end.X - _start.X) / _scale;
        Selection.Height = Math.Abs(end.Y - _start.Y) / _scale;
    }

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}
