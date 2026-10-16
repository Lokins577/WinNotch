using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch.Models;
using WinNotch.Plugins;

namespace TodoPeekPlugin;

[WinNotchPlugin("com.winnotch.todopeek", "Todo Peek", "1.0.2", "WinNotch Team")]
[PluginPermission("network", "Required to fetch tasks from Todoist or Microsoft Graph.")]
public sealed class TodoPeekPlugin : PluginBase, IUIPlugin, IConfigurablePlugin
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public override string Id => "com.winnotch.todopeek";
    public override string Name => "Todo Peek";
    public override string Version => "1.0.2";
    public override string Author => "WinNotch Team";
    public override string Description => "Expanded-view task widget with quick completion for Todoist, plus manual-token Microsoft To Do support.";
    public override string MinimumWinNotchVersion => "0.5.3";

    private IPluginThemeService? _themeService;
    private DispatcherTimer? _refreshTimer;
    private TodoPeekView? _openView;
    private TodoPeekView? _verticalView;
    private TodoPeekSettings _settings = TodoPeekSettings.CreateDefault();
    private readonly List<TodoTaskSnapshot> _tasks = new();
    private string? _settingsDirectory;
    private string? _settingsPath;
    private DateTime _settingsLastWriteUtc = DateTime.MinValue;
    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private string _statusText = "Preparing Todo Peek...";
    private string _detailText = "Create a provider token to start loading tasks.";
    private bool _isLight;
    private bool _isRefreshing;
    private bool _hasConfigError;

    public override async Task InitializeAsync(IPluginContext context)
    {
        await base.InitializeAsync(context);

        _themeService = context.Theme;
        _isLight = _themeService.IsLight;
        _themeService.ThemeChanged += OnThemeChanged;

        _settingsDirectory = context.GetPluginDataPath(Id);
        _settingsPath = Path.Combine(_settingsDirectory, "settings.json");

        await EnsureSettingsFileExistsAsync();
        await LoadSettingsAsync();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(_settings.RefreshMinutes) };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();

        _ = RefreshTasksAsync(force: true);
    }

    public UIElement? GetUIElement(UIPluginLocation location)
    {
        return location switch
        {
            UIPluginLocation.OpenContent => BuildOpenView(),
            UIPluginLocation.VerticalOpenContent => BuildVerticalView(),
            _ => null
        };
    }

    public void OnNotchStateChanged(NotchState newState)
    {
        if (newState != NotchState.Open)
            return;

        _ = OnOpenAsync();
    }

    private async Task OnOpenAsync()
    {
        await ReloadSettingsIfChangedAsync();

        if (DateTime.UtcNow - _lastRefreshUtc > TimeSpan.FromMinutes(Math.Max(1, _settings.RefreshMinutes)))
        {
            await RefreshTasksAsync(force: true);
        }
    }

    private UIElement BuildOpenView()
    {
        if (_openView != null)
            return _openView.Root;

        _openView = CreateView(width: 332, compact: false);
        UpdateViews();
        return _openView.Root;
    }

    private UIElement BuildVerticalView()
    {
        if (_verticalView != null)
            return _verticalView.Root;

        _verticalView = CreateView(width: 164, compact: true);
        _verticalView.Root.Margin = new Thickness(0, 0, 0, 2);
        UpdateViews();
        return _verticalView.Root;
    }

    private TodoPeekView CreateView(double width, bool compact)
    {
        var root = new Border
        {
            Width = width,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 4, 0, 0),
            BorderThickness = new Thickness(0)
        };

        var outer = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel();

        var titleText = new TextBlock
        {
            FontSize = compact ? 13 : 14,
            FontWeight = FontWeights.SemiBold,
            Text = "Todo Peek"
        };
        titleStack.Children.Add(titleText);

        var summaryText = new TextBlock
        {
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0)
        };
        titleStack.Children.Add(summaryText);

        Grid.SetColumn(titleStack, 0);
        header.Children.Add(titleStack);

        var refreshButton = new Button
        {
            Content = "Refresh",
            Padding = new Thickness(10, 3, 10, 3),
            FontSize = 10,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top
        };
        refreshButton.Click += async (_, _) => await RefreshTasksAsync(force: true);
        Grid.SetColumn(refreshButton, 1);
        header.Children.Add(refreshButton);

        outer.Children.Add(header);

        var statusText = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        outer.Children.Add(statusText);

        var detailText = new TextBlock
        {
            FontSize = 10,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        outer.Children.Add(detailText);

        var taskHost = new StackPanel
        {
            Margin = new Thickness(0, 10, 0, 0)
        };
        outer.Children.Add(taskHost);

        var footer = new Grid
        {
            Margin = new Thickness(0, 10, 0, 0)
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var configButton = new Button
        {
            Content = "Open Config",
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 10,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        configButton.Click += (_, _) => OpenConfigurationPanel();
        Grid.SetColumn(configButton, 0);
        footer.Children.Add(configButton);

        var docsButton = new Button
        {
            Content = "Provider Help",
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 10,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        docsButton.Click += (_, _) => OpenProviderHelp();
        Grid.SetColumn(docsButton, 1);
        footer.Children.Add(docsButton);

        outer.Children.Add(footer);
        root.Child = outer;

        return new TodoPeekView(root, titleText, summaryText, statusText, detailText, taskHost, refreshButton, configButton, docsButton, compact);
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshTasksAsync(force: true);
    }

    private async Task EnsureSettingsFileExistsAsync()
    {
        if (string.IsNullOrWhiteSpace(_settingsPath))
            return;

        if (File.Exists(_settingsPath))
            return;

        var defaultFile = """
{
  "provider": "Todoist",
  "maxTasks": 5,
  "refreshMinutes": 5,
  "todoist": {
    "apiToken": "",
    "filter": "(today | overdue | no date) & !subtask"
  },
  "microsoftTodo": {
    "accessToken": "",
    "listName": "Tasks"
  }
}
""";

        await File.WriteAllTextAsync(_settingsPath, defaultFile, Encoding.UTF8);
    }

    private async Task LoadSettingsAsync()
    {
        if (string.IsNullOrWhiteSpace(_settingsPath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath);
            var parsed = JsonSerializer.Deserialize<TodoPeekSettings>(json, JsonOptions) ?? TodoPeekSettings.CreateDefault();
            parsed.Normalize();

            _settings = parsed;
            _settingsLastWriteUtc = File.GetLastWriteTimeUtc(_settingsPath);
            _hasConfigError = false;

            if (_refreshTimer != null)
                _refreshTimer.Interval = TimeSpan.FromMinutes(_settings.RefreshMinutes);
        }
        catch (Exception ex)
        {
            _settings = TodoPeekSettings.CreateDefault();
            _hasConfigError = true;
            _statusText = "Todo Peek settings.json could not be read.";
            _detailText = $"Fix the JSON in {_settingsPath}.";
            Context?.Log($"Todo Peek settings load failed: {ex.Message}", PluginLogLevel.Warning);
        }
    }

    private async Task ReloadSettingsIfChangedAsync()
    {
        if (string.IsNullOrWhiteSpace(_settingsPath) || !File.Exists(_settingsPath))
            return;

        var updatedAt = File.GetLastWriteTimeUtc(_settingsPath);
        if (updatedAt <= _settingsLastWriteUtc)
            return;

        await LoadSettingsAsync();
        UpdateViews();
    }

    private async Task RefreshTasksAsync(bool force = false)
    {
        await ReloadSettingsIfChangedAsync();

        if (_isRefreshing)
            return;

        if (!force && DateTime.UtcNow - _lastRefreshUtc < TimeSpan.FromSeconds(20))
            return;

        _isRefreshing = true;
        _statusText = GetLoadingStatus();
        _detailText = GetLoadingDetail();
        UpdateViews();

        try
        {
            if (_hasConfigError)
            {
                UpdateViews();
                return;
            }

            if (!_settings.IsConfigured)
            {
                _tasks.Clear();
                _statusText = "Todo Peek needs a provider token before it can load tasks.";
                _detailText = GetSetupHint();
                _lastRefreshUtc = DateTime.UtcNow;
                UpdateViews();
                return;
            }

            var result = _settings.Provider switch
            {
                TodoProvider.MicrosoftTodo => await FetchMicrosoftTodoAsync(_settings),
                _ => await FetchTodoistAsync(_settings)
            };

            _tasks.Clear();
            _tasks.AddRange(result.Tasks.Take(_settings.MaxTasks));
            _statusText = result.Status;
            _detailText = result.Detail;
            _lastRefreshUtc = DateTime.UtcNow;
        }
        catch (TodoPeekAuthException)
        {
            _tasks.Clear();
            _statusText = "Todo Peek could not authenticate with the selected provider.";
            _detailText = GetAuthHint();
        }
        catch (Exception ex)
        {
            _tasks.Clear();
            _statusText = "Todo Peek could not refresh right now.";
            _detailText = ex.Message;
            Context?.Log($"Todo Peek refresh failed: {ex.Message}", PluginLogLevel.Warning);
        }
        finally
        {
            _isRefreshing = false;
            UpdateViews();
        }
    }

    private async Task<TodoFetchResult> FetchTodoistAsync(TodoPeekSettings settings)
    {
        var requestTasks = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.todoist.com/rest/v2/tasks?filter={Uri.EscapeDataString(settings.Todoist.Filter)}");
        requestTasks.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Todoist.ApiToken);

        var requestProjects = new HttpRequestMessage(HttpMethod.Get, "https://api.todoist.com/rest/v2/projects");
        requestProjects.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Todoist.ApiToken);

        using var tasksResponse = await HttpClient.SendAsync(requestTasks);
        if (tasksResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TodoPeekAuthException();

        tasksResponse.EnsureSuccessStatusCode();

        using var projectsResponse = await HttpClient.SendAsync(requestProjects);
        if (projectsResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TodoPeekAuthException();

        projectsResponse.EnsureSuccessStatusCode();

        await using var tasksStream = await tasksResponse.Content.ReadAsStreamAsync();
        await using var projectsStream = await projectsResponse.Content.ReadAsStreamAsync();

        var remoteTasks = await JsonSerializer.DeserializeAsync<List<TodoistTask>>(tasksStream, JsonOptions) ?? new List<TodoistTask>();
        var remoteProjects = await JsonSerializer.DeserializeAsync<List<TodoistProject>>(projectsStream, JsonOptions) ?? new List<TodoistProject>();
        var projectNames = remoteProjects
            .Where(project => !string.IsNullOrWhiteSpace(project.Id))
            .ToDictionary(project => project.Id!, project => project.Name ?? "Inbox");

        var snapshots = remoteTasks
            .Where(task => !string.IsNullOrWhiteSpace(task.Id) && !string.IsNullOrWhiteSpace(task.Content))
            .Select(task => MapTodoistTask(task, projectNames))
            .OrderBy(GetTaskSortBucket)
            .ThenByDescending(task => task.Priority)
            .ThenBy(task => task.DueSortValue ?? DateTime.MaxValue)
            .ThenBy(task => task.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var status = snapshots.Count == 0 ? "Todoist is clear right now." : $"{snapshots.Count} task{(snapshots.Count == 1 ? string.Empty : "s")} from Todoist";
        var detail = snapshots.Count == 0
            ? "Try widening the filter in settings.json if you want more items."
            : $"Filter: {settings.Todoist.Filter}";

        return new TodoFetchResult(snapshots, status, detail);
    }

    private async Task<TodoFetchResult> FetchMicrosoftTodoAsync(TodoPeekSettings settings)
    {
        using var listsRequest = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/todo/lists?$top=50");
        listsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.MicrosoftTodo.AccessToken);

        using var listsResponse = await HttpClient.SendAsync(listsRequest);
        if (listsResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TodoPeekAuthException();

        listsResponse.EnsureSuccessStatusCode();

        await using var listsStream = await listsResponse.Content.ReadAsStreamAsync();
        var listEnvelope = await JsonSerializer.DeserializeAsync<GraphEnvelope<GraphTodoList>>(listsStream, JsonOptions);
        var lists = listEnvelope?.Value ?? new List<GraphTodoList>();
        var chosenList = lists.FirstOrDefault(list =>
            string.Equals(list.DisplayName, settings.MicrosoftTodo.ListName, StringComparison.OrdinalIgnoreCase))
            ?? lists.FirstOrDefault();

        if (chosenList?.Id == null)
        {
            return new TodoFetchResult(Array.Empty<TodoTaskSnapshot>(), "Microsoft To Do has no visible lists.", "Check the access token permissions.");
        }

        using var tasksRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://graph.microsoft.com/v1.0/me/todo/lists/{Uri.EscapeDataString(chosenList.Id)}/tasks?$top=25");
        tasksRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.MicrosoftTodo.AccessToken);

        using var tasksResponse = await HttpClient.SendAsync(tasksRequest);
        if (tasksResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TodoPeekAuthException();

        tasksResponse.EnsureSuccessStatusCode();

        await using var tasksStream = await tasksResponse.Content.ReadAsStreamAsync();
        var taskEnvelope = await JsonSerializer.DeserializeAsync<GraphEnvelope<GraphTodoTask>>(tasksStream, JsonOptions);
        var snapshots = (taskEnvelope?.Value ?? new List<GraphTodoTask>())
            .Where(task => !string.Equals(task.Status, "completed", StringComparison.OrdinalIgnoreCase))
            .Where(task => !string.IsNullOrWhiteSpace(task.Id) && !string.IsNullOrWhiteSpace(task.Title))
            .Select(task => MapGraphTask(task, chosenList.DisplayName ?? "Tasks"))
            .OrderBy(GetTaskSortBucket)
            .ThenByDescending(task => task.Priority)
            .ThenBy(task => task.DueSortValue ?? DateTime.MaxValue)
            .ThenBy(task => task.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var status = snapshots.Count == 0
            ? $"Microsoft To Do list '{chosenList.DisplayName}' is clear."
            : $"{snapshots.Count} task{(snapshots.Count == 1 ? string.Empty : "s")} from {chosenList.DisplayName}";
        var detail = "Microsoft To Do access tokens currently need to be supplied manually in settings.json.";

        return new TodoFetchResult(snapshots, status, detail);
    }

    private static TodoTaskSnapshot MapTodoistTask(TodoistTask task, IReadOnlyDictionary<string, string> projectNames)
    {
        DateTime? dueLocal = ParseTodoistDue(task.Due);
        var dueLabel = FormatDueLabel(dueLocal, task.Due?.Date, false);
        var projectName = task.ProjectId != null && projectNames.TryGetValue(task.ProjectId, out var project)
            ? project
            : "Inbox";

        return new TodoTaskSnapshot(
            Provider: TodoProvider.Todoist,
            ParentListId: null,
            ParentListName: projectName,
            Id: task.Id ?? string.Empty,
            Title: task.Content?.Trim() ?? string.Empty,
            Description: task.Description?.Trim() ?? string.Empty,
            DueLabel: dueLabel,
            DueSortValue: dueLocal,
            Priority: Math.Clamp(task.Priority, 1, 4),
            Url: task.Url?.Trim() ?? string.Empty);
    }

    private static TodoTaskSnapshot MapGraphTask(GraphTodoTask task, string listName)
    {
        DateTime? dueLocal = ParseGraphDateTime(task.DueDateTime);
        var dueLabel = FormatDueLabel(dueLocal, null, false);
        var priority = task.Importance?.ToLowerInvariant() switch
        {
            "high" => 4,
            "normal" => 2,
            _ => 1
        };

        return new TodoTaskSnapshot(
            Provider: TodoProvider.MicrosoftTodo,
            ParentListId: null,
            ParentListName: listName,
            Id: task.Id ?? string.Empty,
            Title: task.Title?.Trim() ?? string.Empty,
            Description: task.Body?.Content?.Trim() ?? string.Empty,
            DueLabel: dueLabel,
            DueSortValue: dueLocal,
            Priority: priority,
            Url: string.Empty);
    }

    private static int GetTaskSortBucket(TodoTaskSnapshot task)
    {
        if (task.DueSortValue == null)
            return 2;

        var today = DateTime.Today;
        if (task.DueSortValue.Value.Date < today)
            return 0;

        if (task.DueSortValue.Value.Date == today)
            return 1;

        return 3;
    }

    private async Task CompleteTaskAsync(TodoTaskSnapshot task)
    {
        if (!_settings.IsConfigured || string.IsNullOrWhiteSpace(task.Id))
            return;

        _statusText = $"Marking '{task.Title}' complete...";
        _detailText = "Refreshing after the provider confirms the change.";
        UpdateViews();

        try
        {
            switch (task.Provider)
            {
                case TodoProvider.MicrosoftTodo:
                    await CompleteMicrosoftTaskAsync(task.Id);
                    break;

                default:
                    await CompleteTodoistTaskAsync(task.Id);
                    break;
            }

            _tasks.RemoveAll(item => item.Provider == task.Provider && item.Id == task.Id);
            _statusText = "Task completed.";
            _detailText = "Refreshing tasks...";
            UpdateViews();
            await RefreshTasksAsync(force: true);
        }
        catch (TodoPeekAuthException)
        {
            _statusText = "The provider rejected the completion request.";
            _detailText = GetAuthHint();
            UpdateViews();
        }
        catch (Exception ex)
        {
            _statusText = "Todo Peek could not complete that task.";
            _detailText = ex.Message;
            Context?.Log($"Todo Peek complete failed: {ex.Message}", PluginLogLevel.Warning);
            UpdateViews();
        }
    }

    private async Task CompleteTodoistTaskAsync(string taskId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.todoist.com/rest/v2/tasks/{Uri.EscapeDataString(taskId)}/close");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Todoist.ApiToken);

        using var response = await HttpClient.SendAsync(request);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TodoPeekAuthException();

        response.EnsureSuccessStatusCode();
    }

    private async Task CompleteMicrosoftTaskAsync(string taskId)
    {
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/todo/lists?$top=50");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.MicrosoftTodo.AccessToken);

        using var listResponse = await HttpClient.SendAsync(listRequest);
        if (listResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TodoPeekAuthException();

        listResponse.EnsureSuccessStatusCode();

        await using var listStream = await listResponse.Content.ReadAsStreamAsync();
        var listEnvelope = await JsonSerializer.DeserializeAsync<GraphEnvelope<GraphTodoList>>(listStream, JsonOptions);
        var chosenList = (listEnvelope?.Value ?? new List<GraphTodoList>()).FirstOrDefault(list =>
            string.Equals(list.DisplayName, _settings.MicrosoftTodo.ListName, StringComparison.OrdinalIgnoreCase))
            ?? (listEnvelope?.Value ?? new List<GraphTodoList>()).FirstOrDefault();

        if (string.IsNullOrWhiteSpace(chosenList?.Id))
            throw new InvalidOperationException("Microsoft To Do list could not be resolved.");

        using var updateRequest = new HttpRequestMessage(
            HttpMethod.Patch,
            $"https://graph.microsoft.com/v1.0/me/todo/lists/{Uri.EscapeDataString(chosenList.Id)}/tasks/{Uri.EscapeDataString(taskId)}");
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.MicrosoftTodo.AccessToken);
        updateRequest.Content = new StringContent("{\"status\":\"completed\"}", Encoding.UTF8, "application/json");

        using var updateResponse = await HttpClient.SendAsync(updateRequest);
        if (updateResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TodoPeekAuthException();

        updateResponse.EnsureSuccessStatusCode();
    }

    private void UpdateViews()
    {
        UpdateView(_openView);
        UpdateView(_verticalView);
    }

    private void UpdateView(TodoPeekView? view)
    {
        if (view == null)
            return;

        view.SummaryText.Text = _settings.Provider switch
        {
            TodoProvider.MicrosoftTodo => "Microsoft To Do",
            _ => "Todoist"
        };
        view.StatusText.Text = _statusText;
        view.DetailText.Text = _detailText;
        view.RefreshButton.IsEnabled = !_isRefreshing;
        view.ConfigButton.Content = _settings.IsConfigured ? "Configure" : "Finish Setup";
        view.ConfigButton.ToolTip = _settings.IsConfigured
            ? "Open Todo Peek settings in the Plugin Manager."
            : "Open the Plugin Manager to finish setting up Todo Peek.";
        view.DocsButton.ToolTip = _settings.Provider switch
        {
            TodoProvider.MicrosoftTodo => "Learn how to generate a Microsoft Graph token for Microsoft To Do.",
            _ => "Learn where to find your Todoist token and filter syntax."
        };
        view.TaskHost.Children.Clear();

        if (_tasks.Count == 0)
        {
            view.TaskHost.Children.Add(CreateEmptyState(view.Compact));
        }
        else
        {
            foreach (var task in _tasks)
            {
                view.TaskHost.Children.Add(CreateTaskRow(task, view.Compact));
            }
        }

        ApplyTheme(view);
    }

    private UIElement CreateEmptyState(bool compact)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 2, 0, 0)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(new Border
        {
            Width = 2,
            Margin = new Thickness(0, 1, 10, 1),
            Background = GetSeparatorBrush()
        });

        var stack = new StackPanel();

        var title = new TextBlock
        {
            Text = _settings.IsConfigured ? "Nothing queued right now." : "Paste a provider token to get started.",
            FontSize = compact ? 11 : 12,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(title);

        var body = new TextBlock
        {
            Text = GetSetupHint(),
            FontSize = 10,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(body);

        title.Foreground = GetPrimaryForeground();
        body.Foreground = GetSecondaryForeground();

        Grid.SetColumn(stack, 1);
        row.Children.Add(stack);
        return row;
    }

    private UIElement CreateTaskRow(TodoTaskSnapshot task, bool compact)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 0, 0, 10)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(new Border
        {
            Width = 2,
            Margin = new Thickness(0, 2, 10, 2),
            Background = GetTaskBarBrush(task)
        });

        var textStack = new StackPanel();

        var topLine = new TextBlock
        {
            Text = task.Title,
            FontSize = compact ? 11 : 12,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        textStack.Children.Add(topLine);

        var metaLine = new TextBlock
        {
            Text = $"{task.ParentListName}  {GetPriorityLabel(task.Priority)}  {task.DueLabel}",
            FontSize = 10,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        textStack.Children.Add(metaLine);

        if (!string.IsNullOrWhiteSpace(task.Description) && !compact)
        {
            var description = new TextBlock
            {
                Text = task.Description,
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            textStack.Children.Add(description);
            description.Foreground = GetMutedForeground();
        }

        topLine.Foreground = GetPrimaryForeground();
        metaLine.Foreground = GetSecondaryForeground();

        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        var actionStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };

        var doneButton = new Button
        {
            Content = "Done",
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 10,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brushes.Transparent,
            Foreground = GetAccentBrush(),
            FontWeight = FontWeights.SemiBold
        };
        doneButton.Click += async (_, _) =>
        {
            doneButton.IsEnabled = false;
            try
            {
                await CompleteTaskAsync(task);
            }
            finally
            {
                doneButton.IsEnabled = true;
            }
        };
        actionStack.Children.Add(doneButton);

        if (!compact && !string.IsNullOrWhiteSpace(task.Url))
        {
            var openButton = new Button
            {
                Content = "Open",
                Padding = new Thickness(8, 3, 8, 3),
                FontSize = 10,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(6, 0, 0, 0)
            };
            openButton.Click += (_, _) => OpenExternal(task.Url);
            actionStack.Children.Add(openButton);
            ApplyButtonTheme(openButton, secondary: true);
        }

        ApplyButtonTheme(doneButton, secondary: false);

        Grid.SetColumn(actionStack, 2);
        row.Children.Add(actionStack);
        return row;
    }

    private void ApplyTheme(TodoPeekView view)
    {
        view.Root.Background = Brushes.Transparent;
        view.Root.BorderBrush = Brushes.Transparent;
        view.Root.BorderThickness = new Thickness(0);
        view.TitleText.Foreground = GetPrimaryForeground();
        view.SummaryText.Foreground = GetSecondaryForeground();
        view.StatusText.Foreground = GetPrimaryForeground();
        view.DetailText.Foreground = GetSecondaryForeground();

        ApplyButtonTheme(view.RefreshButton, secondary: false);
        ApplyButtonTheme(view.ConfigButton, secondary: true);
        ApplyButtonTheme(view.DocsButton, secondary: true);
    }

    private void ApplyButtonTheme(Button button, bool secondary)
    {
        button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        button.Foreground = secondary ? GetSecondaryForeground() : GetAccentBrush();
        button.FontWeight = secondary ? FontWeights.Medium : FontWeights.SemiBold;
    }

    private void OnThemeChanged()
    {
        _isLight = _themeService?.IsLight ?? false;
        UpdateViews();
    }

    private void OpenConfigurationPanel()
    {
        OpenPluginManagerConfiguration();
    }

    private void OpenProviderHelp()
    {
        var url = _settings.Provider switch
        {
            TodoProvider.MicrosoftTodo => "https://learn.microsoft.com/en-us/graph/todo-concept-overview",
            _ => "https://developer.todoist.com/rest/v2/"
        };

        OpenExternal(url);
    }

    private static void OpenExternal(string pathOrUrl)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = pathOrUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Best effort only.
        }
    }

    private string GetSetupHint()
    {
        if (_settings.Provider == TodoProvider.MicrosoftTodo)
        {
            return "Open the Plugin Manager and add a Microsoft Graph access token plus the list name you want to show.";
        }

        return "Open the Plugin Manager and add your Todoist API token. The default filter shows overdue, today, and undated tasks.";
    }

    private string GetAuthHint()
    {
        if (_settings.Provider == TodoProvider.MicrosoftTodo)
        {
            return "Use a fresh Microsoft Graph token with Tasks.Read or Tasks.ReadWrite access in the Plugin Manager.";
        }

        return "Check your Todoist API token in the Plugin Manager and try refreshing again.";
    }

    private string GetLoadingStatus()
    {
        return _settings.Provider == TodoProvider.MicrosoftTodo
            ? "Refreshing Microsoft To Do..."
            : "Refreshing Todoist...";
    }

    private string GetLoadingDetail()
    {
        return _settings.Provider == TodoProvider.MicrosoftTodo
            ? "Checking your selected task list."
            : "Loading your filtered task list.";
    }

    private Brush GetSeparatorBrush()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(210, 216, 226)
            : Color.FromRgb(82, 88, 98));
    }

    private Brush GetAccentBrush()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(53, 116, 219)
            : Color.FromRgb(78, 148, 245));
    }

    private Brush GetPrimaryForeground()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(30, 35, 43)
            : Color.FromRgb(241, 244, 249));
    }

    private Brush GetSecondaryForeground()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(96, 103, 114)
            : Color.FromRgb(181, 187, 196));
    }

    private Brush GetMutedForeground()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(122, 129, 139)
            : Color.FromRgb(148, 156, 166));
    }

    private Brush GetTaskBarBrush(TodoTaskSnapshot task)
    {
        if (task.DueSortValue is { } dueDate && dueDate.Date < DateTime.Today)
        {
            return new SolidColorBrush(Color.FromRgb(214, 96, 96));
        }

        if (task.DueSortValue is { } todayDate && todayDate.Date == DateTime.Today)
        {
            return GetAccentBrush();
        }

        if (task.Priority >= 4)
        {
            return new SolidColorBrush(Color.FromRgb(230, 176, 72));
        }

        return GetSeparatorBrush();
    }

    private static DateTime? ParseTodoistDue(TodoistDue? due)
    {
        if (due == null)
            return null;

        if (!string.IsNullOrWhiteSpace(due.DateTime) &&
            DateTimeOffset.TryParse(due.DateTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var offset))
        {
            return offset.LocalDateTime;
        }

        if (!string.IsNullOrWhiteSpace(due.Date) &&
            DateTime.TryParse(due.Date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
        {
            return date.Date;
        }

        return null;
    }

    private static DateTime? ParseGraphDateTime(GraphDateTimeTimeZone? value)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.DateTime))
            return null;

        if (DateTime.TryParse(value.DateTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
            return date;

        return null;
    }

    private static string FormatDueLabel(DateTime? dueLocal, string? rawDate, bool includeTime)
    {
        if (dueLocal == null)
        {
            return string.IsNullOrWhiteSpace(rawDate) ? "No due date" : rawDate!;
        }

        var due = dueLocal.Value;
        var today = DateTime.Today;

        if (due.Date < today)
        {
            var overdueDays = (today - due.Date).Days;
            return overdueDays <= 1 ? "Overdue" : $"Overdue {overdueDays}d";
        }

        if (due.Date == today)
            return includeTime && due.TimeOfDay != TimeSpan.Zero ? $"Today {due:HH:mm}" : "Due today";

        if (due.Date == today.AddDays(1))
            return includeTime && due.TimeOfDay != TimeSpan.Zero ? $"Tomorrow {due:HH:mm}" : "Tomorrow";

        if (due.Date <= today.AddDays(6))
            return includeTime && due.TimeOfDay != TimeSpan.Zero ? $"{due:ddd HH:mm}" : due.ToString("ddd", CultureInfo.CurrentCulture);

        return includeTime && due.TimeOfDay != TimeSpan.Zero ? due.ToString("d MMM HH:mm", CultureInfo.CurrentCulture) : due.ToString("d MMM", CultureInfo.CurrentCulture);
    }

    private static string GetPriorityLabel(int priority)
    {
        return priority switch
        {
            >= 4 => "P1",
            3 => "P2",
            2 => "P3",
            _ => "P4"
        };
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        client.DefaultRequestHeaders.Add("User-Agent", "WinNotch-TodoPeek/1.0.2");
        return client;
    }

    public PluginConfigurationDefinition GetConfigurationDefinition()
    {
        return new PluginConfigurationDefinition
        {
            Title = "Todo Peek Setup",
            Description = "Connect Todo Peek to a task provider and choose how often it refreshes inside the notch.",
            StatusText = _hasConfigError
                ? "The saved Todo Peek settings could not be read. Review the fields below and apply again."
                : _settings.IsConfigured
                    ? $"{GetProviderDisplayName(_settings.Provider)} is configured and ready."
                    : "A provider token is required before Todo Peek can load tasks.",
            RequiresConfiguration = true,
            IsConfigured = _settings.IsConfigured && !_hasConfigError,
            Sections =
            [
                new PluginConfigurationSection
                {
                    Title = "General",
                    Description = "Choose your task provider and how much of it should appear in the expanded notch.",
                    Fields =
                    [
                        new PluginConfigurationField
                        {
                            Key = "provider",
                            Label = "Provider",
                            HelpText = "Pick Todoist for the simplest setup, or Microsoft To Do if you already have a Microsoft Graph token.",
                            Value = _settings.ProviderName,
                            FieldType = PluginConfigurationFieldType.Choice,
                            IsRequired = true,
                            Options =
                            [
                                new PluginConfigurationOption { Value = "Todoist", Label = "Todoist" },
                                new PluginConfigurationOption { Value = "MicrosoftTodo", Label = "Microsoft To Do" }
                            ]
                        },
                        new PluginConfigurationField
                        {
                            Key = "maxTasks",
                            Label = "Max tasks shown",
                            HelpText = "Controls how many tasks Todo Peek shows in the notch at once.",
                            Value = _settings.MaxTasks.ToString(CultureInfo.InvariantCulture),
                            Placeholder = "Between 1 and 8 tasks.",
                            FieldType = PluginConfigurationFieldType.Number,
                            IsRequired = true
                        },
                        new PluginConfigurationField
                        {
                            Key = "refreshMinutes",
                            Label = "Refresh every (minutes)",
                            HelpText = "How often Todo Peek should refresh in the background while WinNotch is running.",
                            Value = _settings.RefreshMinutes.ToString(CultureInfo.InvariantCulture),
                            Placeholder = "Between 1 and 60 minutes.",
                            FieldType = PluginConfigurationFieldType.Number,
                            IsRequired = true
                        }
                    ]
                },
                new PluginConfigurationSection
                {
                    Title = "Todoist",
                    Description = "Only needed when Todoist is selected as your provider.",
                    Fields =
                    [
                        new PluginConfigurationField
                        {
                            Key = "todoist.apiToken",
                            Label = "API token",
                            HelpText = "Find this in Todoist: Settings > Integrations > Developer.",
                            Value = _settings.Todoist.ApiToken,
                            Placeholder = "Paste your Todoist personal API token here.",
                            IsRequired = _settings.Provider == TodoProvider.Todoist
                        },
                        new PluginConfigurationField
                        {
                            Key = "todoist.filter",
                            Label = "Task filter",
                            HelpText = "Todoist filter syntax lets you control exactly which tasks appear in Todo Peek.",
                            Value = _settings.Todoist.Filter,
                            Placeholder = "Default: (today | overdue | no date) & !subtask"
                        }
                    ]
                },
                new PluginConfigurationSection
                {
                    Title = "Microsoft To Do",
                    Description = "Only needed when Microsoft To Do is selected as your provider.",
                    Fields =
                    [
                        new PluginConfigurationField
                        {
                            Key = "microsoftTodo.accessToken",
                            Label = "Access token",
                            HelpText = "Use a Microsoft Graph token with Tasks.Read or Tasks.ReadWrite access. Graph Explorer is the quickest place to generate one.",
                            Value = _settings.MicrosoftTodo.AccessToken,
                            Placeholder = "Paste a fresh Microsoft Graph access token here.",
                            IsRequired = _settings.Provider == TodoProvider.MicrosoftTodo
                        },
                        new PluginConfigurationField
                        {
                            Key = "microsoftTodo.listName",
                            Label = "List name",
                            HelpText = "Todo Peek will try to open this exact Microsoft To Do list name first.",
                            Value = _settings.MicrosoftTodo.ListName,
                            Placeholder = "Example: Tasks"
                        }
                    ]
                }
            ]
        };
    }

    public async Task<PluginConfigurationResult> ApplyConfigurationAsync(IReadOnlyDictionary<string, string?> values)
    {
        var settings = TodoPeekSettings.CreateDefault();
        settings.ProviderName = GetValue(values, "provider", _settings.ProviderName);

        if (!TryReadNumber(values, "maxTasks", out var maxTasks))
            return PluginConfigurationResult.Fail("Max tasks must be a whole number.");

        if (!TryReadNumber(values, "refreshMinutes", out var refreshMinutes))
            return PluginConfigurationResult.Fail("Refresh minutes must be a whole number.");

        settings.MaxTasks = maxTasks;
        settings.RefreshMinutes = refreshMinutes;
        settings.Todoist.ApiToken = GetValue(values, "todoist.apiToken", _settings.Todoist.ApiToken).Trim();
        settings.Todoist.Filter = GetValue(values, "todoist.filter", _settings.Todoist.Filter).Trim();
        settings.MicrosoftTodo.AccessToken = GetValue(values, "microsoftTodo.accessToken", _settings.MicrosoftTodo.AccessToken).Trim();
        settings.MicrosoftTodo.ListName = GetValue(values, "microsoftTodo.listName", _settings.MicrosoftTodo.ListName).Trim();
        settings.Normalize();

        if (settings.Provider == TodoProvider.Todoist && string.IsNullOrWhiteSpace(settings.Todoist.ApiToken))
            return PluginConfigurationResult.Fail("Todoist needs a personal API token before Todo Peek can load tasks.");

        if (settings.Provider == TodoProvider.MicrosoftTodo && string.IsNullOrWhiteSpace(settings.MicrosoftTodo.AccessToken))
            return PluginConfigurationResult.Fail("Microsoft To Do needs a Microsoft Graph access token before Todo Peek can load tasks.");

        await SaveSettingsAsync(settings);
        await RefreshTasksAsync(force: true);

        return PluginConfigurationResult.Ok("Todo Peek configuration saved.");
    }

    private async Task SaveSettingsAsync(TodoPeekSettings settings)
    {
        if (string.IsNullOrWhiteSpace(_settingsPath))
            throw new InvalidOperationException("Todo Peek settings path is not available.");

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_settingsPath, json, Encoding.UTF8);

        _settings = settings;
        _settingsLastWriteUtc = File.GetLastWriteTimeUtc(_settingsPath);
        _hasConfigError = false;

        if (_refreshTimer != null)
            _refreshTimer.Interval = TimeSpan.FromMinutes(_settings.RefreshMinutes);

        UpdateViews();
    }

    private static string GetValue(IReadOnlyDictionary<string, string?> values, string key, string fallback)
    {
        return values.TryGetValue(key, out var value) && value != null ? value : fallback;
    }

    private static bool TryReadNumber(IReadOnlyDictionary<string, string?> values, string key, out int value)
    {
        value = 0;
        if (!values.TryGetValue(key, out var rawValue))
            return false;

        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string GetProviderDisplayName(TodoProvider provider)
    {
        return provider switch
        {
            TodoProvider.MicrosoftTodo => "Microsoft To Do",
            _ => "Todoist"
        };
    }

    public override Task ShutdownAsync()
    {
        if (_themeService != null)
            _themeService.ThemeChanged -= OnThemeChanged;

        if (_refreshTimer != null)
            _refreshTimer.Tick -= OnRefreshTimerTick;

        _refreshTimer?.Stop();
        _refreshTimer = null;

        return base.ShutdownAsync();
    }

    public override void Dispose()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
        base.Dispose();
    }

    private sealed record TodoFetchResult(IEnumerable<TodoTaskSnapshot> Tasks, string Status, string Detail);

    private sealed record TodoTaskSnapshot(
        TodoProvider Provider,
        string? ParentListId,
        string ParentListName,
        string Id,
        string Title,
        string Description,
        string DueLabel,
        DateTime? DueSortValue,
        int Priority,
        string Url);

    private sealed record TodoPeekView(
        Border Root,
        TextBlock TitleText,
        TextBlock SummaryText,
        TextBlock StatusText,
        TextBlock DetailText,
        StackPanel TaskHost,
        Button RefreshButton,
        Button ConfigButton,
        Button DocsButton,
        bool Compact);

    private sealed class TodoPeekAuthException : Exception;

    private enum TodoProvider
    {
        Todoist,
        MicrosoftTodo
    }

    private sealed class TodoPeekSettings
    {
        [JsonPropertyName("provider")]
        public string ProviderName { get; set; } = "Todoist";

        [JsonPropertyName("maxTasks")]
        public int MaxTasks { get; set; } = 5;

        [JsonPropertyName("refreshMinutes")]
        public int RefreshMinutes { get; set; } = 5;

        [JsonPropertyName("todoist")]
        public TodoistSettings Todoist { get; set; } = new();

        [JsonPropertyName("microsoftTodo")]
        public MicrosoftTodoSettings MicrosoftTodo { get; set; } = new();

        [JsonIgnore]
        public TodoProvider Provider => Enum.TryParse<TodoProvider>(ProviderName, true, out var provider)
            ? provider
            : TodoProvider.Todoist;

        [JsonIgnore]
        public bool IsConfigured => Provider switch
        {
            TodoProvider.MicrosoftTodo => !string.IsNullOrWhiteSpace(MicrosoftTodo.AccessToken),
            _ => !string.IsNullOrWhiteSpace(Todoist.ApiToken)
        };

        public void Normalize()
        {
            MaxTasks = Math.Clamp(MaxTasks, 1, 8);
            RefreshMinutes = Math.Clamp(RefreshMinutes, 1, 60);
            ProviderName = string.IsNullOrWhiteSpace(ProviderName) ? "Todoist" : ProviderName.Trim();
            Todoist ??= new TodoistSettings();
            MicrosoftTodo ??= new MicrosoftTodoSettings();
            Todoist.Normalize();
            MicrosoftTodo.Normalize();
        }

        public static TodoPeekSettings CreateDefault()
        {
            var settings = new TodoPeekSettings();
            settings.Normalize();
            return settings;
        }
    }

    private sealed class TodoistSettings
    {
        [JsonPropertyName("apiToken")]
        public string ApiToken { get; set; } = string.Empty;

        [JsonPropertyName("filter")]
        public string Filter { get; set; } = "(today | overdue | no date) & !subtask";

        public void Normalize()
        {
            ApiToken = ApiToken?.Trim() ?? string.Empty;
            Filter = string.IsNullOrWhiteSpace(Filter) ? "(today | overdue | no date) & !subtask" : Filter.Trim();
        }
    }

    private sealed class MicrosoftTodoSettings
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("listName")]
        public string ListName { get; set; } = "Tasks";

        public void Normalize()
        {
            AccessToken = AccessToken?.Trim() ?? string.Empty;
            ListName = string.IsNullOrWhiteSpace(ListName) ? "Tasks" : ListName.Trim();
        }
    }

    private sealed class TodoistTask
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("project_id")]
        public string? ProjectId { get; set; }

        [JsonPropertyName("priority")]
        public int Priority { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("due")]
        public TodoistDue? Due { get; set; }
    }

    private sealed class TodoistDue
    {
        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("datetime")]
        public string? DateTime { get; set; }
    }

    private sealed class TodoistProject
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private sealed class GraphEnvelope<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = new();
    }

    private sealed class GraphTodoList
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }

    private sealed class GraphTodoTask
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("importance")]
        public string? Importance { get; set; }

        [JsonPropertyName("dueDateTime")]
        public GraphDateTimeTimeZone? DueDateTime { get; set; }

        [JsonPropertyName("body")]
        public GraphBody? Body { get; set; }
    }

    private sealed class GraphDateTimeTimeZone
    {
        [JsonPropertyName("dateTime")]
        public string? DateTime { get; set; }

        [JsonPropertyName("timeZone")]
        public string? TimeZone { get; set; }
    }

    private sealed class GraphBody
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
