using System.IO;
using System.Windows;
using LightDownloader.Core.Models;
using LightDownloader.Core.Queue;
using Microsoft.Win32;

namespace LightDownloader.App;

/// <summary>Moves or renames a download (its partial or finished file moves with it).</summary>
public partial class RelocateWindow : Window
{
    private readonly DownloadManager _mgr = App.Current.Manager;
    private readonly DownloadItem _item;

    public RelocateWindow(DownloadItem item)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        _item = item;

        CurrentText.Text = "Currently: " + item.FullPath;
        FolderBox.Text = item.SaveFolder;
        NameBox.Text = item.FileName;
        NoteText.Text = _mgr.IsRunning(item.Id)
            ? "This download is running. It will be paused, moved, and can be resumed from the new location."
            : item.Status == DownloadStatus.Completed
                ? "The finished file will be moved to the new location."
                : "Any part already downloaded moves with it; resuming continues in the new location.";
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose the new folder" };
        try { if (Directory.Exists(FolderBox.Text)) dlg.InitialDirectory = FolderBox.Text; } catch { }
        if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
    }

    private async void OnMove(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text.Trim();
        var name = NameBox.Text.Trim();
        if (folder.Length == 0 || name.Length == 0) { ShowError("Enter both a folder and a file name."); return; }

        MoveButton.IsEnabled = false;
        try
        {
            if (_mgr.IsRunning(_item.Id))
            {
                _mgr.Pause(_item.Id);
                for (int i = 0; i < 50 && _mgr.IsRunning(_item.Id); i++) await Task.Delay(100);
            }
            _mgr.Relocate(_item.Id, folder, name);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            MoveButton.IsEnabled = true;
        }
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }
}
