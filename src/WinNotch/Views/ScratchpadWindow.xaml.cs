using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using WinNotch.Scratchpad;
using WinNotch.Services;

namespace WinNotch.Views;

public partial class ScratchpadWindow : Window
{
    private readonly ScratchpadService _service;
    private readonly DispatcherTimer _saveTimer;
    private bool _loading;
    private ScratchpadDocument? _currentDocument;

    public ScratchpadWindow(ScratchpadService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        InitializeComponent();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => SaveCurrent();
        RefreshDocuments();
        DocumentList.SelectedIndex = 0;
    }

    private void RefreshDocuments(Guid? selected = null)
    {
        bool wasLoading = _loading;
        _loading = true;
        DocumentList.ItemsSource = null;
        DocumentList.ItemsSource = _service.Documents;
        if (selected != null)
            DocumentList.SelectedItem = _service.Documents.FirstOrDefault(document => document.Id == selected);
        _loading = wasLoading;
    }

    private void OnDocumentSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SaveCurrent();
        if (DocumentList.SelectedItem is not ScratchpadDocument document) return;
        _currentDocument = document;
        _loading = true;
        TitleBox.Text = document.Title;
        Editor.Text = document.Content;
        _loading = false;
        RenderPreview();
    }

    private void OnContentChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _saveTimer.Stop();
        _saveTimer.Start();
        if (PreviewCheck.IsChecked == true) RenderPreview();
    }

    private void SaveCurrent()
    {
        _saveTimer.Stop();
        if (_loading || _currentDocument == null) return;
        _service.Update(_currentDocument.Id, TitleBox.Text, Editor.Text);
        RefreshDocuments(_currentDocument.Id);
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        SaveCurrent();
        var document = _service.Add();
        RefreshDocuments(document.Id);
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (DocumentList.SelectedItem is not ScratchpadDocument document) return;
        _currentDocument = null;
        _service.Remove(document.Id);
        RefreshDocuments();
        DocumentList.SelectedIndex = 0;
    }

    private void OnPreviewChanged(object sender, RoutedEventArgs e)
    {
        bool preview = PreviewCheck.IsChecked == true;
        Preview.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = preview ? Visibility.Collapsed : Visibility.Visible;
        if (preview) RenderPreview();
    }

    private void RenderPreview()
    {
        Preview.Document = MarkdownPreview.Render(Editor.Text);
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (DocumentList.SelectedItem is not ScratchpadDocument document) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = SafeFileName(document.Title) + ".md",
            Filter = "Markdown|*.md|Text|*.txt"
        };
        if (dialog.ShowDialog(this) == true)
            File.WriteAllText(dialog.FileName, Editor.Text);
    }

    private static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string value = new(title.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(value) ? "Note" : value;
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveCurrent();
        _saveTimer.Stop();
        base.OnClosed(e);
    }
}

internal static class MarkdownPreview
{
    public static FlowDocument Render(string markdown)
    {
        var document = new FlowDocument { PagePadding = new Thickness(12) };
        foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("### ")) document.Blocks.Add(Heading(line[4..], 15));
            else if (line.StartsWith("## ")) document.Blocks.Add(Heading(line[3..], 18));
            else if (line.StartsWith("# ")) document.Blocks.Add(Heading(line[2..], 22));
            else if (line.StartsWith("- ") || line.StartsWith("* "))
                document.Blocks.Add(new Paragraph(new Run("• " + line[2..])) { Margin = new Thickness(12, 2, 0, 2) });
            else if (string.IsNullOrWhiteSpace(line)) document.Blocks.Add(new Paragraph { Margin = new Thickness(0, 4, 0, 4) });
            else document.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0, 2, 0, 2) });
        }
        return document;
    }

    private static Paragraph Heading(string text, double size)
        => new(new Run(text)) { FontSize = size, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) };
}
