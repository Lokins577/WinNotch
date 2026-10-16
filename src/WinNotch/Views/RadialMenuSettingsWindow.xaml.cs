using System.IO;
using System.Windows;
using System.Windows.Controls;
using WinNotch.Models;
using WinNotch.QuickTools;
using WinNotch.Settings;
using Forms = System.Windows.Forms;

namespace WinNotch.Views;

public partial class RadialMenuSettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _store;
    private bool _loading;

    public RadialMenuSettingsWindow(AppSettings settings, IAppSettingsStore store)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings.RadialMenu ??= RadialMenuConfiguration.CreateDefault();
        _settings.RadialMenu.Normalize();
        InitializeComponent();
        KindBox.ItemsSource = Enum.GetValues<RadialActionKind>();
        RefreshList();
        OnNew(this, new RoutedEventArgs());
    }

    private void RefreshList(RadialMenuItem? selection = null)
    {
        ItemList.ItemsSource = null;
        ItemList.ItemsSource = _settings.RadialMenu.Items;
        ItemList.SelectedItem = selection;
        StatusText.Text = $"{_settings.RadialMenu.Items.Count}/{RadialMenuConfiguration.MaximumItems} actions";
    }

    private void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ItemList.SelectedItem is not RadialMenuItem item) return;
        _loading = true;
        LabelText.Text = item.Label;
        KindBox.SelectedItem = item.Kind;
        TargetText.Text = item.Target ?? string.Empty;
        _loading = false;
        UpdateTargetControls();
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        ItemList.SelectedItem = null;
        _loading = true;
        LabelText.Clear();
        KindBox.SelectedItem = RadialActionKind.CommandBar;
        TargetText.Clear();
        _loading = false;
        UpdateTargetControls();
        LabelText.Focus();
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) UpdateTargetControls();
    }

    private void UpdateTargetControls()
    {
        var kind = KindBox.SelectedItem is RadialActionKind selected
            ? selected
            : RadialActionKind.CommandBar;
        bool needsTarget = RadialActionTarget.RequiresTarget(kind);
        TargetLabel.Visibility = needsTarget ? Visibility.Visible : Visibility.Collapsed;
        TargetText.Visibility = needsTarget ? Visibility.Visible : Visibility.Collapsed;
        BrowsePanel.Visibility = kind is RadialActionKind.Application or RadialActionKind.File
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChooseFolderButton.Visibility = kind == RadialActionKind.File
            ? Visibility.Visible
            : Visibility.Collapsed;
        TargetLabel.Text = kind == RadialActionKind.Url ? "HTTP or HTTPS URL" : "Absolute local path";
        ActionHint.Text = kind switch
        {
            RadialActionKind.Application => "Launches only the saved application path.",
            RadialActionKind.File => "Opens only the saved file or folder with its registered Windows handler.",
            RadialActionKind.Url => "Opens only an absolute HTTP or HTTPS address.",
            _ => "Runs a built-in WinNotch action and does not use a target."
        };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        string label = LabelText.Text.Trim();
        if (label.Length == 0)
        {
            ShowValidation("Enter a label.");
            return;
        }

        var kind = KindBox.SelectedItem is RadialActionKind selected
            ? selected
            : RadialActionKind.CommandBar;
        if (!RadialActionTarget.TryResolve(kind, TargetText.Text, out var target, out var error))
        {
            ShowValidation(error);
            return;
        }

        var item = ItemList.SelectedItem as RadialMenuItem;
        if (item == null)
        {
            if (_settings.RadialMenu.Items.Count >= RadialMenuConfiguration.MaximumItems)
            {
                ShowValidation($"The radial menu supports at most {RadialMenuConfiguration.MaximumItems} actions.");
                return;
            }
            item = new RadialMenuItem();
            _settings.RadialMenu.Items.Add(item);
        }

        item.Label = label;
        item.Kind = kind;
        item.Target = RadialActionTarget.RequiresTarget(kind) ? target : null;
        SaveAndRefresh(item);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not RadialMenuItem item) return;
        _settings.RadialMenu.Items.Remove(item);
        if (_settings.RadialMenu.Items.Count == 0)
            _settings.RadialMenu = RadialMenuConfiguration.CreateDefault();
        SaveAndRefresh();
        OnNew(sender, e);
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveSelection(-1);
    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveSelection(1);

    private void MoveSelection(int offset)
    {
        if (ItemList.SelectedItem is not RadialMenuItem item) return;
        int index = _settings.RadialMenu.Items.IndexOf(item);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= _settings.RadialMenu.Items.Count) return;
        _settings.RadialMenu.Items.RemoveAt(index);
        _settings.RadialMenu.Items.Insert(target, item);
        SaveAndRefresh(item);
    }

    private void OnChooseFile(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = KindBox.SelectedItem is RadialActionKind.Application
                ? "Choose an application"
                : "Choose a file",
            Filter = KindBox.SelectedItem is RadialActionKind.Application
                ? "Applications|*.exe;*.com;*.bat;*.cmd;*.ps1|All files|*.*"
                : "All files|*.*"
        };
        if (dialog.ShowDialog(this) == true) TargetText.Text = dialog.FileName;
    }

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose a folder for the radial action",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
            TargetText.Text = Path.GetFullPath(dialog.SelectedPath);
    }

    private void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Replace the current radial actions with the six defaults?",
                "Restore radial menu defaults",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _settings.RadialMenu = RadialMenuConfiguration.CreateDefault();
        SaveAndRefresh();
        OnNew(sender, e);
    }

    private void SaveAndRefresh(RadialMenuItem? selection = null)
    {
        _settings.RadialMenu.Normalize();
        _store.Save(_settings);
        RefreshList(selection);
        StatusText.Text = $"Saved · {_settings.RadialMenu.Items.Count}/{RadialMenuConfiguration.MaximumItems} actions";
    }

    private void ShowValidation(string message)
    {
        StatusText.Text = message;
        System.Media.SystemSounds.Beep.Play();
    }
}
