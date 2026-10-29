using System.IO;
using System.Windows;
using LightDownloader.Core.Models;
using Microsoft.Win32;

namespace LightDownloader.App;

public partial class SettingsWindow : Window
{
    public AppSettings Result { get; private set; }

    public SettingsWindow(AppSettings s)
    {
        InitializeComponent();
        Result = s;
        FolderBox.Text = s.DownloadFolder;
        ConnBox.Text = s.DefaultConnections.ToString();
        ConcBox.Text = s.MaxConcurrentDownloads.ToString();
        SpeedBox.Text = (s.SpeedLimitBytesPerSec / 1024).ToString(); // shown as KB/s
        PortBox.Text = s.ServerPort.ToString();
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        try { if (Directory.Exists(FolderBox.Text)) dlg.InitialDirectory = FolderBox.Text; } catch { }
        if (dlg.ShowDialog() == true) FolderBox.Text = dlg.FolderName;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = new AppSettings
        {
            DownloadFolder = string.IsNullOrWhiteSpace(FolderBox.Text) ? Result.DownloadFolder : FolderBox.Text.Trim(),
            DefaultConnections = ParseInt(ConnBox.Text, 8, 1, 32),
            MaxConcurrentDownloads = ParseInt(ConcBox.Text, 3, 1, 20),
            SpeedLimitBytesPerSec = ParseLong(SpeedBox.Text, 0, 0, long.MaxValue / 1024) * 1024,
            ServerPort = ParseInt(PortBox.Text, 7414, 1, 65535),
            StartMinimized = Result.StartMinimized,
            SchedulerEnabled = Result.SchedulerEnabled,
            StartTime = Result.StartTime,
            StopTime = Result.StopTime
        };
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private static int ParseInt(string s, int def, int min, int max)
        => int.TryParse(s, out var v) ? Math.Clamp(v, min, max) : def;

    private static long ParseLong(string s, long def, long min, long max)
        => long.TryParse(s, out var v) ? Math.Clamp(v, min, max) : def;
}
