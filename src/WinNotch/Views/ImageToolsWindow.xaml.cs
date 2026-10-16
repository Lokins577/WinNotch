using System.IO;
using System.Windows;
using WinNotch.MediaTools;
using WinNotch.Platform.Windows.MediaTools;
using Forms = System.Windows.Forms;

namespace WinNotch.Views;

public partial class ImageToolsWindow : Window
{
    private readonly MediaExportService _exporter;
    private string[] _files = [];

    public ImageToolsWindow(MediaExportService exporter)
    {
        _exporter = exporter;
        InitializeComponent();
    }

    private void OnChooseFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose images",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        _files = dialog.FileNames;
        FilesText.Text = $"{_files.Length} image{(_files.Length == 1 ? string.Empty : "s")} selected";
    }

    private void OnConvert(object sender, RoutedEventArgs e)
    {
        if (_files.Length == 0) return;
        using var dialog = new Forms.FolderBrowserDialog { Description = "Choose output folder", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        bool jpeg = FormatBox.SelectedIndex == 1;
        int? maximum = int.TryParse(MaxDimensionText.Text, out int parsed) && parsed > 0 ? parsed : null;
        var options = new ImageTransformOptions(maximum, (int)QualitySlider.Value, WatermarkText.Text);
        int completed = 0;
        foreach (string input in _files)
        {
            try
            {
                string output = Path.Combine(dialog.SelectedPath,
                    Path.GetFileNameWithoutExtension(input) + (jpeg ? ".jpg" : ".png"));
                _exporter.TransformImage(input, output, jpeg ? MediaExportFormat.Jpeg : MediaExportFormat.Png, options);
                completed++;
            }
            catch { }
        }
        StatusText.Text = $"Converted {completed}/{_files.Length} images.";
    }
}
