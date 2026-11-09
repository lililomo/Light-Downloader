using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
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
    private bool _detailsHidden;
    private string? _iconFor;

    public ProgressWindow(DownloadItem item)
    {
        InitializeComponent();
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

    private void Refresh()
    {
        var it = _item;
        var segments = it.Segments; // the engine swaps in a new list, so this reference is stable
        bool known = it.TotalSize > 0 || it.PercentOverride.HasValue;
        bool running = _mgr.IsRunning(it.Id);
        bool done = it.Status == DownloadStatus.Completed;
        double pct = it.ProgressPercent;

        Title = done ? "Download complete" : (known ? $"{pct:0}% " : "") + it.FileName;
        if (_iconFor != it.FileName)
        {
            _iconFor = it.FileName;
            TypeIcon.Source = ShellIcons.ForFile(it.Kind == DownloadKind.Video && !System.IO.Path.HasExtension(it.FileName) ? "v.mp4" : it.FileName, large: true);
        }
        StatusText.Text = Describe(it);

        SizeVal.Text = it.TotalSize > 0 ? Formatting.Bytes(it.TotalSize) : "(unknown)";
        DoneVal.Text = it.Kind == DownloadKind.Http
            ? (it.TotalSize > 0 ? $"{Formatting.Bytes(it.Downloaded)} ( {pct:0.00} % )" : Formatting.Bytes(it.Downloaded))
            : $"{pct:0.0} %";
        RateVal.Text = it.IsActive && it.SpeedBytesPerSec > 0 ? Formatting.Speed(it.SpeedBytesPerSec) : "";
        EtaVal.Text = done ? "" : it.EtaShort;
        ResumeVal.Text = it.Kind switch
        {
            DownloadKind.Video => "Yes",
            DownloadKind.Convert => "No",
            _ when segments.Count == 0 => "",
            _ => it.SupportsRanges ? "Yes" : "No"
        };
        PathVal.Text = it.FullPath;
        PathVal.ToolTip = it.FullPath;

        MainBar.Value = done ? 100 : pct;
        MainBar.IsIndeterminate = !done && it.IsActive && !known;
        ErrorText.Visibility = it.Status == DownloadStatus.Error ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Text = it.Error ?? "";

        bool hasMap = it.Kind == DownloadKind.Http;
        DetailsButton.Visibility = hasMap ? Visibility.Visible : Visibility.Collapsed;
        DetailsPanel.Visibility = hasMap && !_detailsHidden ? Visibility.Visible : Visibility.Collapsed;
        if (hasMap && !_detailsHidden)
        {
            Segments.Update(segments, it.TotalSize);
            UpdateRows(segments, running);
        }

        OpenFileButton.Visibility = OpenFolderButton.Visibility = done ? Visibility.Visible : Visibility.Collapsed;
        PauseButton.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
        PauseButton.Content = running || it.Status == DownloadStatus.Queued ? "Pause"
            : it.Status == DownloadStatus.Error ? "Retry" : "Resume";
        CancelButton.Content = done || !running ? "Close" : "Cancel";
        LimitBox.IsEnabled = LimitValue.IsEnabled = it.Kind == DownloadKind.Http && !done;

        if (done && !_finishHandled)
        {
            _finishHandled = true;
            if (OpenFolderBox.IsChecked == true) Shell.Reveal(it.FullPath, it.SaveFolder);
            if (AutoCloseBox.IsChecked == true) Close();
            else Activate();
        }
    }

    private static string Describe(DownloadItem it) => it.Status switch
    {
        DownloadStatus.Connecting => "Connecting…",
        DownloadStatus.Downloading when it.Kind == DownloadKind.Video =>
            it.PercentOverride is null ? "Getting video information…" : "Receiving data…",
        DownloadStatus.Downloading when it.Kind == DownloadKind.Convert => "Converting…",
        DownloadStatus.Downloading => "Receiving data…",
        DownloadStatus.Paused => "Stopped",
        DownloadStatus.Queued => "Waiting in the queue",
        DownloadStatus.Completed => "The download is complete.",
        DownloadStatus.Error => "Error",
        _ => it.Status.ToString()
    };

    private void UpdateRows(IReadOnlyList<SegmentState> segments, bool running)
    {
        if (_rows.Count != segments.Count)
        {
            _rows.Clear();
            for (int i = 0; i < segments.Count; i++) _rows.Add(new ConnectionRow(i + 1));
        }
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            string range = s.End >= 0 ? $"{Formatting.Bytes(s.Start)} – {Formatting.Bytes(s.End + 1)}" : "Whole file";
            string info = s.IsComplete ? "Complete" : running ? "Receiving data…" : "";
            _rows[i].Update(range, Formatting.Bytes(s.Downloaded), info);
        }
    }

    // ---- actions -----------------------------------------------------------------

    private void OnToggleDetails(object sender, RoutedEventArgs e)
    {
        _detailsHidden = !_detailsHidden;
        DetailsButton.Content = _detailsHidden ? "Show details >>" : "<< Hide details";
        Refresh();
    }

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

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        Shell.Open(_item.FullPath);
        Close();
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        Shell.Reveal(_item.FullPath, _item.SaveFolder);
        Close();
    }

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
        long kb = long.TryParse(LimitValue.Text.Trim(), out var v) ? Math.Clamp(v, 1, 10_000_000) : 500;
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
    public string Info { get; private set; } = "";

    public void Update(string range, string received, string info)
    {
        if (Range == range && Received == received && Info == info) return;
        Range = range;
        Received = received;
        Info = info;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); // refresh all
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
