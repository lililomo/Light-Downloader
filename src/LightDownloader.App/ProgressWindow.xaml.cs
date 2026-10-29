using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LightDownloader.Core.Models;
using LightDownloader.Core.Queue;
using LightDownloader.Core.Util;

namespace LightDownloader.App;

/// <summary>Per-download status window (IDM's download progress dialog).</summary>
public partial class ProgressWindow : Window
{
    private readonly DownloadManager _mgr = App.Current.Manager;
    private readonly DownloadItem _item;
    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<ConnectionRow> _rows = new();
    private bool _finishHandled;

    public ProgressWindow(DownloadItem item)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        _item = item;

        ConnList.ItemsSource = _rows;
        UrlText.Text = item.Url;
        UrlText.ToolTip = item.Url;
        LimitBox.IsChecked = item.SpeedLimitBytesPerSec > 0;
        if (item.SpeedLimitBytesPerSec > 0) LimitValue.Text = (item.SpeedLimitBytesPerSec / 1024).ToString();
        // Already-finished downloads shouldn't immediately auto-close.
        _finishHandled = item.Status == DownloadStatus.Completed;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        Refresh();
    }

    public Guid ItemId => _item.Id;

    private Brush Res(string key) => (Brush)FindResource(key);

    private void Refresh()
    {
        var it = _item;
        var segments = it.Segments; // the engine swaps in a new list, so this reference is stable
        bool known = it.TotalSize > 0 || it.PercentOverride.HasValue;
        bool running = _mgr.IsRunning(it.Id);
        double pct = it.ProgressPercent;

        Title = (known ? $"{pct:0}% · " : "") + it.FileName;
        NameText.Text = it.FileName;
        TypeBadge.Background = CategoryVisuals.TintFor(it.Category);
        TypeGlyph.Text = CategoryVisuals.GlyphFor(it.Category);
        TypeGlyph.Foreground = CategoryVisuals.BrushFor(it.Category);

        var statusBrush = it.Status switch
        {
            DownloadStatus.Completed => Res("SuccessBrush"),
            DownloadStatus.Paused => Res("WarningBrush"),
            DownloadStatus.Error => Res("DangerBrush"),
            DownloadStatus.Queued => Res("MutedBrush"),
            _ => Res("AccentGradientBrush")
        };
        PercentText.Text = known ? $"{pct:0.0}%" : it.Downloaded > 0 ? Formatting.Bytes(it.Downloaded) : "—";
        MainBar.Value = it.Status == DownloadStatus.Completed ? 100 : pct;
        MainBar.Foreground = statusBrush;
        StatusDot.Fill = it.Status is DownloadStatus.Downloading or DownloadStatus.Connecting ? Res("AccentBrush") : statusBrush;
        StatusText.Text = Describe(it);

        SizeVal.Text = it.TotalSize > 0 ? Formatting.Bytes(it.TotalSize) : "Unknown";
        DoneVal.Text = it.Kind == DownloadKind.Http
            ? (it.TotalSize > 0 ? $"{Formatting.Bytes(it.Downloaded)}  ({pct:0.0}%)" : Formatting.Bytes(it.Downloaded))
            : $"{pct:0.0}%";
        RateVal.Text = it.IsActive && it.SpeedBytesPerSec > 0 ? Formatting.Speed(it.SpeedBytesPerSec) : "—";
        EtaVal.Text = it.Status == DownloadStatus.Completed ? "Done" : it.EtaText;
        ResumeVal.Text = it.Kind switch
        {
            DownloadKind.Video => "Yes (yt-dlp)",
            DownloadKind.Convert => "No",
            _ when segments.Count == 0 => "Checking…",
            _ => it.SupportsRanges ? "Yes" : "No"
        };
        PathVal.Text = it.FullPath;
        PathVal.ToolTip = it.FullPath;

        if (it.Kind == DownloadKind.Http)
        {
            int open = segments.Count(s => !s.IsComplete);
            ConnVal.Text = segments.Count == 0 ? "—"
                : running ? $"{open} active of {segments.Count}" : $"{segments.Count}";
        }
        else ConnVal.Text = it.Kind == DownloadKind.Video ? "Managed by yt-dlp" : "ffmpeg";

        bool showMap = it.Kind == DownloadKind.Http && it.TotalSize > 0 && segments.Count > 0;
        SegmentsPanel.Visibility = showMap ? Visibility.Visible : Visibility.Collapsed;
        if (showMap)
        {
            Segments.Update(segments, it.TotalSize, it.Status is DownloadStatus.Downloading or DownloadStatus.Connecting
                ? Res("AccentBrush") : statusBrush);
            SegmentsCaption.Text = segments.Count == 1 ? "Single connection" : $"{segments.Count} connections — one block each";
        }
        UpdateRows(segments, it, running);

        // Buttons & outcome banners.
        bool done = it.Status == DownloadStatus.Completed;
        DonePanel.Visibility = done ? Visibility.Visible : Visibility.Collapsed;
        if (done) DoneInfo.Text = $"{Formatting.Bytes(it.TotalSize)} saved to {it.SaveFolder}";
        ErrorPanel.Visibility = it.Status == DownloadStatus.Error ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Text = it.Error ?? "";

        OpenFileButton.Visibility = done ? Visibility.Visible : Visibility.Collapsed;
        PauseButton.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
        PauseButton.Content = running || it.Status == DownloadStatus.Queued ? "Pause"
            : it.Status == DownloadStatus.Error ? "Retry" : "Resume";
        CancelButton.Content = done || !running ? "Close" : "Cancel";
        LimitBox.IsEnabled = LimitValue.IsEnabled = it.Kind == DownloadKind.Http && !done;

        if (done && !_finishHandled)
        {
            _finishHandled = true;
            if (AutoCloseBox.IsChecked == true) Close();
            else Activate();
        }
    }

    private static string Describe(DownloadItem it) => it.Status switch
    {
        DownloadStatus.Connecting => "Connecting to the server…",
        DownloadStatus.Downloading when it.Kind == DownloadKind.Video =>
            it.PercentOverride is null ? "Fetching video info…" : "Downloading video…",
        DownloadStatus.Downloading when it.Kind == DownloadKind.Convert => "Converting with ffmpeg…",
        DownloadStatus.Downloading => "Receiving data…",
        DownloadStatus.Paused => "Paused",
        DownloadStatus.Queued => "Waiting in the queue",
        DownloadStatus.Completed => "Completed",
        DownloadStatus.Error => "Failed",
        _ => it.Status.ToString()
    };

    private void UpdateRows(IReadOnlyList<SegmentState> segments, DownloadItem it, bool running)
    {
        if (it.Kind != DownloadKind.Http) { _rows.Clear(); return; }
        if (_rows.Count != segments.Count)
        {
            _rows.Clear();
            for (int i = 0; i < segments.Count; i++) _rows.Add(new ConnectionRow(i + 1));
        }
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            string range = s.End >= 0
                ? $"{Formatting.Bytes(s.Start)} – {Formatting.Bytes(s.End + 1)}"
                : "Whole file";
            double pct = s.Length > 0 ? Math.Clamp(s.Downloaded * 100.0 / s.Length, 0, 100) : 0;
            string info = s.IsComplete ? "Complete" : running ? "Receiving data" : "Idle";
            _rows[i].Update(range, Formatting.Bytes(s.Downloaded), pct, info);
        }
    }

    // ---- actions -----------------------------------------------------------------

    private void OnPauseResume(object sender, RoutedEventArgs e)
    {
        if (_mgr.IsRunning(_item.Id) || _item.Status == DownloadStatus.Queued) _mgr.Pause(_item.Id);
        else _mgr.Start(_item.Id);
        Refresh();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (_mgr.IsRunning(_item.Id) || _item.Status == DownloadStatus.Queued) _mgr.Pause(_item.Id);
        Close();
    }

    private void OnOpenFile(object sender, RoutedEventArgs e) => Shell.Open(_item.FullPath);
    private void OnOpenFolder(object sender, RoutedEventArgs e) => Shell.Reveal(_item.FullPath, _item.SaveFolder);

    private void OnLimitToggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyLimit();
    }

    private void OnApplyLimit(object sender, RoutedEventArgs e)
    {
        LimitBox.IsChecked = true;
        ApplyLimit();
    }

    private void ApplyLimit()
    {
        long kb = long.TryParse(LimitValue.Text.Trim(), out var v) ? Math.Clamp(v, 1, 10_000_000) : 1024;
        LimitValue.Text = kb.ToString();
        _mgr.SetItemSpeedLimit(_item.Id, LimitBox.IsChecked == true ? kb * 1024 : 0);
    }
}

/// <summary>One line of the connection table.</summary>
public sealed class ConnectionRow : INotifyPropertyChanged
{
    public ConnectionRow(int index) => Index = index;

    public int Index { get; }
    public string Range { get; private set; } = "";
    public string Received { get; private set; } = "";
    public double Percent { get; private set; }
    public string Info { get; private set; } = "";

    public void Update(string range, string received, double percent, string info)
    {
        Range = range;
        Received = received;
        Percent = percent;
        Info = info;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); // refresh all
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
