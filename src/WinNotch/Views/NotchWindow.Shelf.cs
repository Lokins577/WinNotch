using System.Windows;
using WinNotch.Models;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private void UpdateShelfVisibility()
    {
        bool hasItems = _shelfService.HasItems;
        ShelfPanel.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;

        // Adjust open height if notch is open
        if (_vm.NotchState == NotchState.Open && !_isSettingsExpanded)
        {
            double targetHeight = GetTopOpenHeight();
            _heightSpring.AnimateTo(targetHeight, _currentHeight);
        }
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        // The file shelf lives in the horizontal (top dock) layout only
        if (_dock != NotchDock.Top) return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;

            // Open the notch if closed/peeking to show drop zone
            if (_vm.NotchState != NotchState.Open)
                TransitionToOpen();

            // Show drop zone and expand for shelf
            ShelfPanel.Visibility = Visibility.Visible;
            ShelfPanel.ShowDropZone();

            if (!_isSettingsExpanded)
            {
                _heightSpring.AnimateTo(GetTopOpenHeight(), _currentHeight);
            }

            e.Handled = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (_dock != NotchDock.Top) return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        ShelfPanel.HideDropZone();

        // If no items, collapse shelf and shrink back
        if (!_shelfService.HasItems)
        {
            ShelfPanel.Visibility = Visibility.Collapsed;
            if (_vm.NotchState == NotchState.Open && !_isSettingsExpanded)
            {
                _heightSpring.AnimateTo(GetTopOpenHeight(), _currentHeight);
            }
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_dock != NotchDock.Top) return;

        ShelfPanel.HideDropZone();

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
            {
                _shelfService.AddFiles(files);
            }
            e.Handled = true;
        }
    }
}
