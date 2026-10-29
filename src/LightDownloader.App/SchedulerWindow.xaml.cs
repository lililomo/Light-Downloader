using System.Windows;
using LightDownloader.Core.Models;

namespace LightDownloader.App;

public partial class SchedulerWindow : Window
{
    public AppSettings Result { get; private set; }

    public SchedulerWindow(AppSettings s)
    {
        InitializeComponent();
        Result = s;
        EnabledBox.IsChecked = s.SchedulerEnabled;
        StartBox.Text = s.StartTime ?? "";
        StopBox.Text = s.StopTime ?? "";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = Clone(Result);
        Result.SchedulerEnabled = EnabledBox.IsChecked == true;
        Result.StartTime = Normalize(StartBox.Text);
        Result.StopTime = Normalize(StopBox.Text);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string? Normalize(string? s)
        => TimeSpan.TryParse(s?.Trim(), out var t) ? t.ToString(@"hh\:mm") : null;

    private static AppSettings Clone(AppSettings s) => new()
    {
        DownloadFolder = s.DownloadFolder,
        DefaultConnections = s.DefaultConnections,
        MaxConcurrentDownloads = s.MaxConcurrentDownloads,
        SpeedLimitBytesPerSec = s.SpeedLimitBytesPerSec,
        ServerPort = s.ServerPort,
        StartMinimized = s.StartMinimized,
        SchedulerEnabled = s.SchedulerEnabled,
        StartTime = s.StartTime,
        StopTime = s.StopTime
    };
}
