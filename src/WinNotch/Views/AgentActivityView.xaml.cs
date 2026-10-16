using System.Windows;
using System.Windows.Controls;
using WinNotch.Agents;
using WinNotch.Services;
using Brush = System.Windows.Media.Brush;

namespace WinNotch.Views;

public partial class AgentActivityView : UserControl
{
    private AgentActivityService? _service;
    private bool _compact;

    public AgentActivityView()
    {
        InitializeComponent();
    }

    public void Bind(AgentActivityService service, bool compact = false)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_service != null)
            _service.Updated -= OnUpdated;
        _service = service;
        _compact = compact;
        _service.Updated += OnUpdated;
        Render(service.Snapshot);
    }

    private void OnUpdated(AgentActivitySnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Render(snapshot));
            return;
        }
        Render(snapshot);
    }

    private void Render(AgentActivitySnapshot snapshot)
    {
        ProviderText.Text = snapshot.AvailableProviders.Count == 0
            ? "Local only"
            : string.Join(" · ", snapshot.AvailableProviders.Order().Select(ShortName));
        EmptyText.Visibility = snapshot.Activities.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ActivityHost.Children.Clear();
        foreach (var activity in snapshot.Activities.Take(_compact ? 3 : 8))
            ActivityHost.Children.Add(CreateRow(activity));
    }

    private static FrameworkElement CreateRow(AgentActivity activity)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var provider = new TextBlock
        {
            Text = ShortName(activity.Provider),
            Width = 48,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = GetBrush("TextSubtleBrush", System.Windows.Media.Brushes.Gray)
        };
        var details = new StackPanel();
        details.Children.Add(new TextBlock
        {
            Text = activity.Project,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = GetBrush("TextPrimaryBrush", System.Windows.Media.Brushes.White)
        });
        if (!string.IsNullOrWhiteSpace(activity.Model))
        {
            details.Children.Add(new TextBlock
            {
                Text = activity.Model,
                FontSize = 8,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = GetBrush("TextDimBrush", System.Windows.Media.Brushes.Gray)
            });
        }
        var state = new TextBlock
        {
            Text = activity.State.ToString(),
            FontSize = 9,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = activity.State == AgentRunState.Working
                ? new System.Windows.Media.SolidColorBrush(Color.FromRgb(80, 210, 120))
                : GetBrush("TextSecondaryBrush", System.Windows.Media.Brushes.LightGray)
        };
        grid.Children.Add(provider);
        Grid.SetColumn(details, 1);
        grid.Children.Add(details);
        Grid.SetColumn(state, 2);
        grid.Children.Add(state);
        return grid;
    }

    private static string ShortName(AgentProvider provider)
    {
        return provider switch
        {
            AgentProvider.OpenCode => "OpenCode",
            AgentProvider.Copilot => "Copilot",
            _ => provider.ToString()
        };
    }

    private static Brush GetBrush(string key, Brush fallback)
    {
        return Application.Current?.TryFindResource(key) as Brush ?? fallback;
    }
}
