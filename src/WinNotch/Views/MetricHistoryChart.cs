using System.Windows;
using System.Windows.Media;
using MediaPen = System.Windows.Media.Pen;

namespace WinNotch.Views;

public sealed class MetricHistoryChart : FrameworkElement
{
    private IReadOnlyList<double> _values = [];
    private double _maximum = 100;

    public void SetValues(IEnumerable<double?> values, double maximum = 100)
    {
        _values = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        _maximum = Math.Max(0.001, maximum);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var gridPen = new MediaPen(new SolidColorBrush(Color.FromArgb(35, 180, 180, 190)), 1);
        for (int index = 1; index < 4; index++)
        {
            double y = ActualHeight * index / 4;
            drawingContext.DrawLine(gridPen, new Point(0, y), new Point(ActualWidth, y));
        }
        if (_values.Count < 2 || ActualWidth <= 0 || ActualHeight <= 0) return;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (int index = 0; index < _values.Count; index++)
            {
                double x = index * ActualWidth / Math.Max(1, _values.Count - 1);
                double normalized = Math.Clamp(_values[index] / _maximum, 0, 1);
                var point = new Point(x, ActualHeight * (1 - normalized));
                if (index == 0) context.BeginFigure(point, false, false);
                else context.LineTo(point, true, false);
            }
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(
            null,
            new MediaPen(new SolidColorBrush(Color.FromRgb(89, 156, 255)), 2),
            geometry);
    }
}
