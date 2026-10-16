using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WinNotch.Helpers;
using WinNotch.QuickTools;

namespace WinNotch.Views;

public sealed class RadialMenuWindow : Window
{
    private const double Diameter = 420;
    private readonly Func<RadialMenuItem, Task> _execute;
    private bool _acting;

    public RadialMenuWindow(
        IReadOnlyList<RadialMenuItem> items,
        Func<RadialMenuItem, Task> execute)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        Width = Diameter;
        Height = Diameter;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;

        var canvas = new Canvas { Width = Diameter, Height = Diameter };
        var background = new Ellipse
        {
            Width = Diameter - 30,
            Height = Diameter - 30,
            Fill = new SolidColorBrush(Color.FromArgb(226, 22, 22, 25)),
            Stroke = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            StrokeThickness = 1
        };
        canvas.Children.Add(background);
        Canvas.SetLeft(background, 15);
        Canvas.SetTop(background, 15);

        var visible = items.Take(RadialMenuConfiguration.MaximumItems).ToArray();
        double radius = 145;
        for (int index = 0; index < visible.Length; index++)
        {
            var item = visible[index];
            double angle = -Math.PI / 2 + index * Math.PI * 2 / visible.Length;
            var button = new Button
            {
                Content = item.Label,
                Width = 88,
                Height = 44,
                Tag = item,
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += OnItemClick;
            Canvas.SetLeft(button, Diameter / 2 + Math.Cos(angle) * radius - button.Width / 2);
            Canvas.SetTop(button, Diameter / 2 + Math.Sin(angle) * radius - button.Height / 2);
            canvas.Children.Add(button);
        }

        var close = new Button { Content = "×", Width = 54, Height = 54, FontSize = 20 };
        close.Click += (_, _) => Close();
        Canvas.SetLeft(close, Diameter / 2 - 27);
        Canvas.SetTop(close, Diameter / 2 - 27);
        canvas.Children.Add(close);
        Content = canvas;

        SourceInitialized += (_, _) => PositionAtPointer();
        Deactivated += (_, _) => { if (!_acting) Close(); };
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.Escape) Close();
        };
    }

    private void PositionAtPointer()
    {
        var pointer = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(pointer);
        double scale = ScreenHelper.GetScaleForScreen(screen);
        int width = (int)Math.Round(Diameter * scale);
        int height = (int)Math.Round(Diameter * scale);
        int x = Math.Clamp(pointer.X - width / 2, screen.WorkingArea.Left, screen.WorkingArea.Right - width);
        int y = Math.Clamp(pointer.Y - height / 2, screen.WorkingArea.Top, screen.WorkingArea.Bottom - height);
        WindowHelper.SetWindowRectPhysical(this, x, y, width, height);
    }

    private async void OnItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RadialMenuItem item }) return;
        _acting = true;
        try
        {
            await _execute(item);
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Radial action failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            _acting = false;
        }
    }
}
