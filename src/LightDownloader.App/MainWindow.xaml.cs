using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using LightDownloader.Core.Models;
using LightDownloader.Core.Queue;
using LightDownloader.Core.Util;

namespace LightDownloader.App;

public partial class MainWindow : Window
{
    private readonly DownloadManager _mgr = App.Current.Manager;
    private readonly ObservableCollection<DownloadItem> _items = new();
    private readonly System.Windows.Threading.DispatcherTimer _timer;

    public bool AllowClose;

    public MainWindow()
    {
        InitializeComponent();
        Grid.ItemsSource = _items;

        foreach (var i in _mgr.Items) _items.Add(i);

        _mgr.ItemAdded += OnItemAdded;
        _mgr.ItemRemoved += OnItemRemoved;

        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        UpdateBridgeStatus();
    }

    private void OnItemAdded(DownloadItem item)
        => Dispatcher.Invoke(() => { if (!_items.Contains(item)) _items.Add(item); });

    private void OnItemRemoved(DownloadItem item)
        => Dispatcher.Invoke(() => _items.Remove(item));

    private void Refresh()
    {
        double total = 0;
        foreach (var i in _items)
        {
            i.Notify();
            if (i.Status == DownloadStatus.Downloading) total += i.SpeedBytesPerSec;
        }
        TotalSpeed.Text = "Total: " + Formatting.Speed(total);
    }

    private void UpdateBridgeStatus()
    {
        var s = App.Current.Server;
        BridgeStatus.Text = s.Running ? $"Bridge: 127.0.0.1:{s.Port}" : "Bridge: off";
    }

    private DownloadItem? Selected => Grid.SelectedItem as DownloadItem;

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var w = new AddUrlWindow { Owner = this };
        if (w.ShowDialog() == true && !string.IsNullOrWhiteSpace(w.Url))
        {
            _mgr.Add(new DownloadRequest
            {
                Url = w.Url.Trim(),
                FileName = string.IsNullOrWhiteSpace(w.FileName) ? null : w.FileName.Trim()
            });
        }
    }

    private void OnResume(object sender, RoutedEventArgs e)
    {
        foreach (DownloadItem i in Grid.SelectedItems) _mgr.Start(i.Id);
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        foreach (DownloadItem i in Grid.SelectedItems) _mgr.Pause(i.Id);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        var sel = Grid.SelectedItems.Cast<DownloadItem>().ToList();
        if (sel.Count == 0) return;
        var res = MessageBox.Show(this,
            $"Remove {sel.Count} item(s)?\n\nYes = also delete the file(s) on disk\nNo = keep file(s)",
            "Remove", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (res == MessageBoxResult.Cancel) return;
        bool del = res == MessageBoxResult.Yes;
        foreach (var i in sel) _mgr.Remove(i.Id, del);
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var i = Selected;
        var folder = i?.SaveFolder ?? _mgr.Settings.DownloadFolder;
        try
        {
            if (i != null && File.Exists(i.FullPath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{i.FullPath}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow(_mgr.Settings) { Owner = this };
        if (w.ShowDialog() == true) _mgr.UpdateSettings(w.Result);
    }

    private void OnAddVideo(object sender, RoutedEventArgs e)
    {
        var w = new AddVideoWindow { Owner = this };
        if (w.ShowDialog() != true || string.IsNullOrWhiteSpace(w.Url)) return;
        if (!_mgr.Tools.HasYtDlp)
        {
            MessageBox.Show(this,
                "Video tools (yt-dlp) are still downloading in the background. Please try again shortly.",
                "Light Downloader", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _mgr.Add(new DownloadRequest { Url = w.Url.Trim(), Kind = "video" });
    }

    private void OnConvert(object sender, RoutedEventArgs e)
    {
        var i = Selected;
        if (i == null || i.Status != DownloadStatus.Completed)
        {
            MessageBox.Show(this, "Select a completed download to convert.",
                "Light Downloader", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!_mgr.Tools.HasFfmpeg)
        {
            MessageBox.Show(this, "ffmpeg is still downloading in the background. Please try again shortly.",
                "Light Downloader", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var w = new ConvertWindow(i.FileName) { Owner = this };
        if (w.ShowDialog() == true)
        {
            var outName = Path.GetFileNameWithoutExtension(i.FileName) + w.TargetExtension;
            _mgr.AddConvert(i.FullPath, outName);
        }
    }

    private void OnScheduler(object sender, RoutedEventArgs e)
    {
        var w = new SchedulerWindow(_mgr.Settings) { Owner = this };
        if (w.ShowDialog() == true) _mgr.UpdateSettings(w.Result);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); } // minimize to tray instead of exiting
        base.OnClosing(e);
    }
}
