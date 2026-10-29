using System.IO;
using System.Windows;
using LightDownloader.Core.Models;
using Microsoft.Win32;

namespace LightDownloader.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _working;

    public AppSettings Result { get; private set; }

    public SettingsWindow(AppSettings s)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        Result = s;
        _working = s.Clone();

        FolderBox.Text = s.DownloadFolder;
        ConnBox.Text = s.DefaultConnections.ToString();
        ConcBox.Text = s.MaxConcurrentDownloads.ToString();
        SpeedBox.Text = (s.SpeedLimitBytesPerSec / 1024).ToString();
        PortBox.Text = s.ServerPort.ToString();
        AskBox.IsChecked = s.AskWhereToSave;
        ProgressBox.IsChecked = s.ShowProgressWindow;
        MinimizedBox.IsChecked = s.StartMinimized;
        ShowRemembered();
    }

    private void ShowRemembered()
    {
        var folders = _working.CategoryFolders;
        RememberedText.Text = folders.Count == 0
            ? "None yet. Tick \"Remember this folder\" in the New download dialog to save one per category."
            : string.Join(Environment.NewLine, folders.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}  →  {kv.Value}"));
        ForgetButton.IsEnabled = folders.Count > 0;
    }

    private void OnForget(object sender, RoutedEventArgs e)
    {
        _working.CategoryFolders.Clear();
        ShowRemembered();
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Default download folder" };
        try { if (Directory.Exists(FolderBox.Text)) dlg.InitialDirectory = FolderBox.Text; } catch { }
        if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _working.DownloadFolder = string.IsNullOrWhiteSpace(FolderBox.Text) ? Result.DownloadFolder : FolderBox.Text.Trim();
        _working.DefaultConnections = ParseInt(ConnBox.Text, 8, 1, 32);
        _working.MaxConcurrentDownloads = ParseInt(ConcBox.Text, 3, 1, 20);
        _working.SpeedLimitBytesPerSec = ParseLong(SpeedBox.Text, 0, 0, long.MaxValue / 1024) * 1024;
        _working.ServerPort = ParseInt(PortBox.Text, 7414, 1, 65535);
        _working.AskWhereToSave = AskBox.IsChecked == true;
        _working.ShowProgressWindow = ProgressBox.IsChecked == true;
        _working.StartMinimized = MinimizedBox.IsChecked == true;
        Result = _working;
        DialogResult = true;
    }

    private static int ParseInt(string s, int def, int min, int max)
        => int.TryParse(s, out var v) ? Math.Clamp(v, min, max) : def;

    private static long ParseLong(string s, long def, long min, long max)
        => long.TryParse(s, out var v) ? Math.Clamp(v, min, max) : def;
}
