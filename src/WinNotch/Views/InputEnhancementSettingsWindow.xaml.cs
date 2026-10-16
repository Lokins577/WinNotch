using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WinNotch.InputEnhancements;
using WinNotch.Models;
using WinNotch.Services;
using WinNotch.Settings;

namespace WinNotch.Views;

public partial class InputEnhancementSettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _store;
    private readonly InputEnhancementFeatureService _service;
    private readonly List<TextExpansionSnippet> _snippets;

    public InputEnhancementSettingsWindow(
        AppSettings settings,
        IAppSettingsStore store,
        InputEnhancementFeatureService service)
    {
        _settings = settings;
        _store = store;
        _service = service;
        _snippets = settings.InputEnhancementPreferences.TextExpansions
            .Select(value => new TextExpansionSnippet { Id = value.Id, Trigger = value.Trigger, Replacement = value.Replacement })
            .ToList();
        InitializeComponent();
        X1Box.ItemsSource = Enum.GetValues<SideButtonAction>();
        X2Box.ItemsSource = Enum.GetValues<SideButtonAction>();
        SuperBox.ItemsSource = Enum.GetValues<SuperKeyAction>();
        LoadValues();
        RefreshSnippets();
    }

    private void LoadValues()
    {
        var value = _settings.InputEnhancementPreferences;
        ReverseVerticalCheck.IsChecked = value.ReverseVerticalScroll;
        ReverseHorizontalCheck.IsChecked = value.ReverseHorizontalScroll;
        ScrollMultiplierText.Text = value.ScrollMultiplier.ToString(CultureInfo.InvariantCulture);
        FocusCheck.IsChecked = value.FocusFollowsMouse;
        FocusDelayText.Text = value.FocusDelayMilliseconds.ToString();
        AccelerationCheck.IsChecked = value.OverrideMouseAcceleration;
        SpeedText.Text = value.MouseSpeed.ToString();
        Threshold1Text.Text = value.AccelerationThreshold1.ToString();
        Threshold2Text.Text = value.AccelerationThreshold2.ToString();
        AccelerationModeText.Text = value.AccelerationMode.ToString();
        X1Box.SelectedItem = value.XButton1Action;
        X2Box.SelectedItem = value.XButton2Action;
        SuperBox.SelectedItem = value.RightSuperKeyAction;
        DebounceText.Text = value.SideButtonDebounceMilliseconds.ToString();
        ExpansionCheck.IsChecked = value.TextExpansionEnabled;
        ExcludedText.Text = string.Join(", ", value.ExcludedApplications);
    }

    private void RefreshSnippets(TextExpansionSnippet? selected = null)
    {
        SnippetList.ItemsSource = null;
        SnippetList.ItemsSource = _snippets;
        SnippetList.SelectedItem = selected;
    }

    private void OnSnippetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (SnippetList.SelectedItem is not TextExpansionSnippet value) return;
        TriggerText.Text = value.Trigger;
        ReplacementText.Text = value.Replacement;
    }

    private void OnNewSnippet(object sender, RoutedEventArgs e)
    {
        SnippetList.SelectedItem = null;
        TriggerText.Clear();
        ReplacementText.Clear();
    }

    private void OnSaveSnippet(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TriggerText.Text)) return;
        var value = SnippetList.SelectedItem as TextExpansionSnippet ?? new TextExpansionSnippet();
        value.Trigger = TriggerText.Text.Trim();
        value.Replacement = ReplacementText.Text;
        if (!_snippets.Contains(value)) _snippets.Add(value);
        RefreshSnippets(value);
    }

    private void OnDeleteSnippet(object sender, RoutedEventArgs e)
    {
        if (SnippetList.SelectedItem is TextExpansionSnippet value) _snippets.Remove(value);
        RefreshSnippets();
        OnNewSnippet(sender, e);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var value = _settings.InputEnhancementPreferences;
        value.ReverseVerticalScroll = ReverseVerticalCheck.IsChecked == true;
        value.ReverseHorizontalScroll = ReverseHorizontalCheck.IsChecked == true;
        value.ScrollMultiplier = Double(ScrollMultiplierText.Text, 1);
        value.FocusFollowsMouse = FocusCheck.IsChecked == true;
        value.FocusDelayMilliseconds = Integer(FocusDelayText.Text, 250);
        value.OverrideMouseAcceleration = AccelerationCheck.IsChecked == true;
        value.MouseSpeed = Integer(SpeedText.Text, 10);
        value.AccelerationThreshold1 = Integer(Threshold1Text.Text, 6);
        value.AccelerationThreshold2 = Integer(Threshold2Text.Text, 10);
        value.AccelerationMode = Integer(AccelerationModeText.Text, 1);
        value.XButton1Action = X1Box.SelectedItem is SideButtonAction x1 ? x1 : SideButtonAction.None;
        value.XButton2Action = X2Box.SelectedItem is SideButtonAction x2 ? x2 : SideButtonAction.None;
        value.RightSuperKeyAction = SuperBox.SelectedItem is SuperKeyAction super ? super : SuperKeyAction.None;
        value.SideButtonDebounceMilliseconds = Integer(DebounceText.Text, 120);
        value.TextExpansionEnabled = ExpansionCheck.IsChecked == true;
        value.TextExpansions = _snippets.Select(snippet => new TextExpansionSnippet
        {
            Id = snippet.Id,
            Trigger = snippet.Trigger,
            Replacement = snippet.Replacement
        }).ToList();
        value.ExcludedApplications = ExcludedText.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        value.Normalize();
        _store.Save(_settings);
        _service.ApplyPreferences();
        DialogResult = true;
    }

    private static int Integer(string value, int fallback) => int.TryParse(value, out int parsed) ? parsed : fallback;
    private static double Double(string value, double fallback)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
