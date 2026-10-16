using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinNotch.Features;
using WinNotch.Models;
using Brush = System.Windows.Media.Brush;

namespace WinNotch.Views;

public sealed class FeatureCatalogWindow : Window
{
    private readonly HashSet<FeatureId> _toggleableFeatures;
    private readonly Func<FeatureDescriptor, bool, Task>? _setAvailability;

    public FeatureCatalogWindow(
        AppSettings settings,
        IEnumerable<FeatureId>? toggleableFeatures = null,
        Func<FeatureDescriptor, bool, Task>? setAvailability = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _toggleableFeatures = toggleableFeatures?.ToHashSet() ?? new HashSet<FeatureId>();
        _setAvailability = setAvailability;

        Title = "WinNotch Features";
        Width = 640;
        Height = 720;
        MinWidth = 480;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = GetBrush("WindowBackgroundBrush", Color.FromRgb(20, 20, 22));
        Foreground = GetBrush("TextPrimaryBrush", Colors.White);

        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock
        {
            Text = "Features",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = Foreground
        });
        content.Children.Add(new TextBlock
        {
            Text = "Available features are implemented today. Planned entries define the frozen delivery roadmap.",
            Margin = new Thickness(0, 6, 0, 20),
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetBrush("TextSecondaryBrush", Color.FromRgb(190, 190, 196))
        });

        foreach (var group in Enum.GetValues<FeatureGroup>())
        {
            var features = BuiltInFeatures.Catalog.Features
                .Where(feature => feature.Group == group)
                .OrderBy(feature => feature.Maturity)
                .ThenBy(feature => feature.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            if (features.Length == 0) continue;

            content.Children.Add(new TextBlock
            {
                Text = FormatGroup(group).ToUpperInvariant(),
                Margin = new Thickness(0, 14, 0, 6),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetBrush("TextSubtleBrush", Color.FromRgb(145, 145, 152))
            });

            foreach (var feature in features)
                content.Children.Add(CreateFeatureRow(feature, settings));
        }

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content
        };
    }

    private FrameworkElement CreateFeatureRow(FeatureDescriptor feature, AppSettings settings)
    {
        var title = new TextBlock
        {
            Text = feature.DisplayName,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Foreground
        };
        var description = new TextBlock
        {
            Text = feature.Description,
            Margin = new Thickness(0, 4, 12, 0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = GetBrush("TextSecondaryBrush", Color.FromRgb(190, 190, 196))
        };
        FrameworkElement status = _toggleableFeatures.Contains(feature.Id) && _setAvailability != null
            ? CreateAvailabilityToggle(feature, settings)
            : CreateStatusBadge(feature, settings);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(title);
        text.Children.Add(description);
        grid.Children.Add(text);
        Grid.SetColumn(status, 1);
        grid.Children.Add(status);

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(14, 12, 14, 12),
            CornerRadius = new CornerRadius(10),
            Background = GetBrush("SurfaceRaisedBrush", Color.FromRgb(31, 31, 34)),
            Child = grid
        };
    }

    private FrameworkElement CreateStatusBadge(FeatureDescriptor feature, AppSettings settings)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Background = feature.Maturity == FeatureMaturity.Available
                ? new SolidColorBrush(Color.FromArgb(48, 52, 199, 89))
                : new SolidColorBrush(Color.FromArgb(42, 142, 142, 147)),
            Child = new TextBlock
            {
                Text = feature.Maturity == FeatureMaturity.Available
                    ? (settings.IsFeatureAvailable(feature) ? "Available" : "Disabled")
                    : feature.Maturity.ToString(),
                FontSize = 10,
                Foreground = feature.Maturity == FeatureMaturity.Available
                    ? new SolidColorBrush(Color.FromRgb(96, 220, 124))
                    : GetBrush("TextSubtleBrush", Color.FromRgb(160, 160, 166))
            }
        };
    }

    private FrameworkElement CreateAvailabilityToggle(FeatureDescriptor feature, AppSettings settings)
    {
        var toggle = new CheckBox
        {
            Content = settings.IsFeatureAvailable(feature) ? "On" : "Off",
            IsChecked = settings.IsFeatureAvailable(feature),
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = GetBrush("TextSecondaryBrush", Color.FromRgb(190, 190, 196))
        };
        bool updating = false;

        async void Apply(object sender, RoutedEventArgs args)
        {
            if (updating || _setAvailability == null) return;
            bool enabled = toggle.IsChecked == true;
            toggle.IsEnabled = false;
            try
            {
                await _setAvailability(feature, enabled);
                toggle.Content = enabled ? "On" : "Off";
            }
            catch (Exception exception)
            {
                updating = true;
                toggle.IsChecked = !enabled;
                toggle.Content = !enabled ? "On" : "Off";
                updating = false;
                MessageBox.Show(exception.Message, "Feature change failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                toggle.IsEnabled = true;
            }
        }

        toggle.Checked += Apply;
        toggle.Unchecked += Apply;
        return toggle;
    }

    private static string FormatGroup(FeatureGroup group)
    {
        return group switch
        {
            FeatureGroup.ClipboardAndFiles => "Clipboard and files",
            FeatureGroup.EnergyAndDisplay => "Energy and display",
            _ => group.ToString()
        };
    }

    private Brush GetBrush(string key, Color fallback)
    {
        return TryFindResource(key) as Brush
            ?? Application.Current?.TryFindResource(key) as Brush
            ?? new SolidColorBrush(fallback);
    }
}
