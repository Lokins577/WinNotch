using System.Windows;
using System.Windows.Controls;
using WinNotch.Agents;
using WinNotch.Services;

namespace WinNotch.Views;

public partial class AgentCompactView : UserControl
{
    private AgentActivityService? _service;

    public AgentCompactView()
    {
        InitializeComponent();
    }

    public void Bind(AgentActivityService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_service != null)
            _service.Updated -= OnUpdated;
        _service = service;
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
        var working = snapshot.Activities.FirstOrDefault(item => item.State == AgentRunState.Working);
        Visibility = working == null ? Visibility.Collapsed : Visibility.Visible;
        ActivityText.Text = working == null ? string.Empty : $"{working.Provider}: {working.Project}";
    }
}
