using System.Drawing;
using System.Windows;
using System.Windows.Input;
using WinNotch.Helpers;
using Forms = System.Windows.Forms;
using DrawingPoint = System.Drawing.Point;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace WinNotch.Views;

public partial class PointPickerOverlay : Window
{
    private readonly Forms.Screen _screen;
    public DrawingPoint? SelectedPoint { get; private set; }

    public PointPickerOverlay(Forms.Screen screen)
    {
        _screen = screen;
        double scale = ScreenHelper.GetScaleForScreen(screen);
        InitializeComponent();
        Width = screen.Bounds.Width / scale;
        Height = screen.Bounds.Height / scale;
        SourceInitialized += (_, _) => WindowHelper.SetWindowRectPhysical(
            this, screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height);
        Loaded += (_, _) => Focus();
    }

    private void OnPick(object sender, MouseButtonEventArgs e)
    {
        SelectedPoint = Forms.Cursor.Position;
        DialogResult = true;
    }

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}
