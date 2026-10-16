using System.IO;
using System.Windows;
using System.Windows.Controls;
using WinNotch.CommandBar;
using WinNotch.Models;
using WinNotch.Settings;
using Forms = System.Windows.Forms;

namespace WinNotch.Views;

public partial class CommandBarSettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _store;
    private readonly Action _settingsChanged;

    public CommandBarSettingsWindow(
        AppSettings settings,
        IAppSettingsStore store,
        Action settingsChanged)
    {
        _settings = settings;
        _store = store;
        _settingsChanged = settingsChanged;
        InitializeComponent();
        RefreshLists();
    }

    private void RefreshLists()
    {
        FolderList.ItemsSource = null;
        FolderList.ItemsSource = _settings.CommandBarSearchFolders;
        SnippetList.ItemsSource = null;
        SnippetList.ItemsSource = _settings.CommandSnippets;
        ScriptList.ItemsSource = null;
        ScriptList.ItemsSource = _settings.CommandScripts;
    }

    private void Save()
    {
        _store.Save(_settings);
        _settingsChanged();
        RefreshLists();
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose a folder to index by file name",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        string path = Path.GetFullPath(dialog.SelectedPath);
        if (!_settings.CommandBarSearchFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
            _settings.CommandBarSearchFolders.Add(path);
        Save();
    }

    private void OnRemoveFolder(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is not string path) return;
        _settings.CommandBarSearchFolders.Remove(path);
        Save();
    }

    private void OnSnippetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (SnippetList.SelectedItem is not CommandSnippet snippet) return;
        SnippetName.Text = snippet.Name;
        SnippetTrigger.Text = snippet.Trigger;
        SnippetContent.Text = snippet.Content;
    }

    private void OnNewSnippet(object sender, RoutedEventArgs e)
    {
        SnippetList.SelectedItem = null;
        SnippetName.Clear(); SnippetTrigger.Clear(); SnippetContent.Clear();
    }

    private void OnSaveSnippet(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SnippetName.Text) || string.IsNullOrWhiteSpace(SnippetContent.Text)) return;
        var snippet = SnippetList.SelectedItem as CommandSnippet ?? new CommandSnippet();
        snippet.Name = SnippetName.Text.Trim();
        snippet.Trigger = SnippetTrigger.Text.Trim();
        snippet.Content = SnippetContent.Text;
        if (!_settings.CommandSnippets.Contains(snippet)) _settings.CommandSnippets.Add(snippet);
        Save();
        SnippetList.SelectedItem = snippet;
    }

    private void OnDeleteSnippet(object sender, RoutedEventArgs e)
    {
        if (SnippetList.SelectedItem is not CommandSnippet snippet) return;
        _settings.CommandSnippets.Remove(snippet);
        Save();
        OnNewSnippet(sender, e);
    }

    private void OnScriptSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ScriptList.SelectedItem is not SavedCommandScript script) return;
        ScriptName.Text = script.Name;
        ScriptExecutable.Text = script.Executable;
        ScriptArguments.Text = script.Arguments;
        ScriptWorkingDirectory.Text = script.WorkingDirectory ?? string.Empty;
    }

    private void OnNewScript(object sender, RoutedEventArgs e)
    {
        ScriptList.SelectedItem = null;
        ScriptName.Clear(); ScriptExecutable.Clear(); ScriptArguments.Clear(); ScriptWorkingDirectory.Clear();
    }

    private void OnBrowseExecutable(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an executable or script",
            Filter = "Programs and scripts|*.exe;*.cmd;*.bat;*.ps1;*.com|All files|*.*"
        };
        if (dialog.ShowDialog(this) == true) ScriptExecutable.Text = dialog.FileName;
    }

    private void OnSaveScript(object sender, RoutedEventArgs e)
    {
        string executable = ScriptExecutable.Text.Trim();
        if (string.IsNullOrWhiteSpace(ScriptName.Text) || !Path.IsPathRooted(executable) || !File.Exists(executable)) return;
        var script = ScriptList.SelectedItem as SavedCommandScript ?? new SavedCommandScript();
        script.Name = ScriptName.Text.Trim();
        script.Executable = executable;
        script.Arguments = ScriptArguments.Text;
        script.WorkingDirectory = string.IsNullOrWhiteSpace(ScriptWorkingDirectory.Text)
            ? null : ScriptWorkingDirectory.Text.Trim();
        if (!_settings.CommandScripts.Contains(script)) _settings.CommandScripts.Add(script);
        Save();
        ScriptList.SelectedItem = script;
    }

    private void OnDeleteScript(object sender, RoutedEventArgs e)
    {
        if (ScriptList.SelectedItem is not SavedCommandScript script) return;
        _settings.CommandScripts.Remove(script);
        Save();
        OnNewScript(sender, e);
    }
}
