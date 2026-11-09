using System.IO;
using System.Windows;
using System.Windows.Controls;
using LightDownloader.Core.Models;
using LightDownloader.Core.Util;
using Microsoft.Win32;

namespace LightDownloader.App;

/// <summary>The Options dialog (IDM's General / File Types / Connection / Save To / Downloads tabs).</summary>
public partial class SettingsWindow : Window
{
    private const string General = "General";

    private readonly AppSettings _working;
    private readonly Dictionary<string, CheckBox> _browserBoxes = new();
    private readonly Dictionary<string, string> _folders; // category (or General) → folder; missing = default
    private string _shownCategory = General;

    public AppSettings Result { get; private set; }

    public SettingsWindow(AppSettings s, IReadOnlyDictionary<string, DateTime> seenBrowsers)
    {
        InitializeComponent();
        Result = s;
        _working = s.Clone();

        StartupBox.IsChecked = s.RunAtStartup;
        MinimizedBox.IsChecked = s.StartMinimized;
        CaptureBox.IsChecked = s.BrowserCapture;
        VideoButtonBox.IsChecked = s.VideoButton;
        foreach (var (key, name) in AppSettings.Browsers)
        {
            var box = new CheckBox
            {
                Content = seenBrowsers.ContainsKey(key) ? $"{name}  (extension connected)" : name,
                IsChecked = !s.DisabledBrowsers.Contains(key, StringComparer.OrdinalIgnoreCase),
                Margin = new Thickness(0, 2, 0, 2)
            };
            _browserBoxes[key] = box;
            BrowsersPanel.Children.Add(box);
        }
        OnCaptureToggled(this, new RoutedEventArgs());

        TypesBox.Text = s.CaptureFileTypes;
        SitesBox.Text = s.SkipSites;

        ConnBox.Text = s.DefaultConnections.ToString();
        ConcBox.Text = s.MaxConcurrentDownloads.ToString();
        SpeedLimitBox.IsChecked = s.SpeedLimitBytesPerSec > 0;
        SpeedBox.Text = s.SpeedLimitBytesPerSec > 0 ? (s.SpeedLimitBytesPerSec / 1024).ToString() : "1024";
        SpeedBox.IsEnabled = SpeedLimitBox.IsChecked == true;
        PortBox.Text = s.ServerPort.ToString();

        _folders = new Dictionary<string, string>(s.CategoryFolders) { [General] = s.DownloadFolder };
        SaveCategoryBox.ItemsSource = new[] { General }.Concat(Categories.All).ToList();
        SaveCategoryBox.SelectedIndex = 0;

        AskBox.IsChecked = s.AskWhereToSave;
        ProgressBox.IsChecked = s.ShowProgressWindow;
    }

    private void OnCaptureToggled(object sender, RoutedEventArgs e)
    {
        if (BrowsersPanel == null) return;
        BrowsersPanel.IsEnabled = CaptureBox.IsChecked == true;
    }

    private void OnSpeedLimitToggled(object sender, RoutedEventArgs e)
    {
        if (SpeedBox != null) SpeedBox.IsEnabled = SpeedLimitBox.IsChecked == true;
    }

    private void OnDefaultTypes(object sender, RoutedEventArgs e) => TypesBox.Text = AppSettings.DefaultFileTypes;

    private void OnChromeExtension(object sender, RoutedEventArgs e) => Shell.OpenIn("chrome.exe", Shell.ChromeExtensionUrl);
    private void OnFirefoxExtension(object sender, RoutedEventArgs e) => Shell.OpenIn("firefox.exe", Shell.FirefoxAddonUrl);

    // ---- Save To ---------------------------------------------------------------------

    private void CommitFolder()
    {
        var text = SaveFolderBox.Text.Trim();
        if (_shownCategory == General) { if (text.Length > 0) _folders[General] = text; }
        else if (text.Length == 0 || string.Equals(text, _folders[General], StringComparison.OrdinalIgnoreCase)) _folders.Remove(_shownCategory);
        else _folders[_shownCategory] = text;
    }

    private void OnSaveCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SaveCategoryBox.SelectedItem is not string category) return;
        if (IsLoaded) CommitFolder();
        _shownCategory = category;
        SaveFolderLabel.Text = category == General ? "Default download folder:" : $"Save \"{category}\" files to:";
        SaveFolderBox.Text = _folders.TryGetValue(category, out var f) ? f : _folders[General];
        UseDefaultButton.IsEnabled = category != General;
    }

    private void OnUseDefaultFolder(object sender, RoutedEventArgs e)
    {
        _folders.Remove(_shownCategory);
        SaveFolderBox.Text = _folders[General];
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = SaveFolderLabel.Text.TrimEnd(':') };
        try { if (Directory.Exists(SaveFolderBox.Text)) dlg.InitialDirectory = SaveFolderBox.Text; } catch { }
        if (dlg.ShowDialog(this) == true) SaveFolderBox.Text = dlg.FolderName;
    }

    // ---- OK --------------------------------------------------------------------------

    private void OnSave(object sender, RoutedEventArgs e)
    {
        CommitFolder();
        _working.RunAtStartup = StartupBox.IsChecked == true;
        _working.StartMinimized = MinimizedBox.IsChecked == true;
        _working.BrowserCapture = CaptureBox.IsChecked == true;
        _working.VideoButton = VideoButtonBox.IsChecked == true;
        _working.DisabledBrowsers = _browserBoxes.Where(kv => kv.Value.IsChecked != true).Select(kv => kv.Key).ToList();

        _working.CaptureFileTypes = string.Join(" ", AppSettings.SplitList(TypesBox.Text).Select(t => t.TrimStart('.').ToUpperInvariant()));
        _working.SkipSites = string.Join(" ", AppSettings.SplitList(SitesBox.Text));

        _working.DefaultConnections = ParseInt(ConnBox.Text, 8, 1, 32);
        _working.MaxConcurrentDownloads = ParseInt(ConcBox.Text, 3, 1, 20);
        _working.SpeedLimitBytesPerSec = SpeedLimitBox.IsChecked == true ? ParseLong(SpeedBox.Text, 1024, 1, long.MaxValue / 1024) * 1024 : 0;
        _working.ServerPort = ParseInt(PortBox.Text, 7414, 1, 65535);

        _working.DownloadFolder = _folders[General];
        _working.CategoryFolders = _folders.Where(kv => kv.Key != General).ToDictionary(kv => kv.Key, kv => kv.Value);

        _working.AskWhereToSave = AskBox.IsChecked == true;
        _working.ShowProgressWindow = ProgressBox.IsChecked == true;
        Result = _working;
        DialogResult = true;
    }

    private static int ParseInt(string s, int def, int min, int max)
        => int.TryParse(s.Trim(), out var v) ? Math.Clamp(v, min, max) : def;

    private static long ParseLong(string s, long def, long min, long max)
        => long.TryParse(s.Trim(), out var v) ? Math.Clamp(v, min, max) : def;
}
