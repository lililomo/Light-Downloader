using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using LightDownloader.Core.Models;
using LightDownloader.Core.Queue;
using LightDownloader.Core.Util;

namespace LightDownloader.App;

public partial class MainWindow : Window
{
    private readonly DownloadManager _mgr = App.Current.Manager;
    private readonly ObservableCollection<DownloadItem> _items = new();
    private readonly ObservableCollection<NavEntry> _nav = new();
    private readonly ICollectionView _view;
    private readonly DispatcherTimer _timer;
    private NavEntry? _currentNav;
    private string _search = "";

    public bool AllowClose;

    public MainWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);

        // Fit the work area (e.g. a 1080-px-wide portrait monitor) instead of overflowing it.
        var work = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, work.Width - 32));
        Height = Math.Max(MinHeight, Math.Min(Height, work.Height - 48));

        foreach (var i in _mgr.Items) _items.Add(i);
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = o => o is DownloadItem i && Matches(_currentNav?.Key ?? NavEntry.AllKey, i) && MatchesSearch(i);
        _view.SortDescriptions.Add(new SortDescription(nameof(DownloadItem.AddedAt), ListSortDirection.Descending));
        if (_view is ICollectionViewLiveShaping live && live.CanChangeLiveFiltering)
        {
            // Rows move between "Downloading"/"Finished" etc. as their status changes.
            live.LiveFilteringProperties.Add(nameof(DownloadItem.Status));
            live.LiveFilteringProperties.Add(nameof(DownloadItem.Category));
            live.IsLiveFiltering = true;
        }
        DownloadsGrid.ItemsSource = _view;

        BuildNav();
        NavList.ItemsSource = _nav;
        NavList.SelectedIndex = 0;

        _mgr.ItemAdded += i => Dispatcher.InvokeAsync(() => { if (!_items.Contains(i)) _items.Add(i); });
        _mgr.ItemRemoved += i => Dispatcher.InvokeAsync(() => _items.Remove(i));

        VersionText.Text = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    // ---- sidebar & filtering ---------------------------------------------------

    private void BuildNav()
    {
        Brush R(string key) => (Brush)FindResource(key);
        _nav.Add(new NavEntry(NavEntry.AllKey, "All downloads", "", R("AccentBrush")));
        _nav.Add(new NavEntry(NavEntry.ActiveKey, "Downloading", "", R("Accent2Brush")));
        _nav.Add(new NavEntry(NavEntry.UnfinishedKey, "Unfinished", "", R("WarningBrush")));
        _nav.Add(new NavEntry(NavEntry.FinishedKey, "Finished", "", R("SuccessBrush")));
        _nav.Add(NavEntry.Header("CATEGORIES"));
        foreach (var c in Categories.All)
            _nav.Add(new NavEntry(c, c, CategoryVisuals.GlyphFor(c), CategoryVisuals.BrushFor(c)));
    }

    private static bool Matches(string key, DownloadItem i) => key switch
    {
        NavEntry.AllKey => true,
        NavEntry.ActiveKey => i.IsActive || i.Status == DownloadStatus.Queued,
        NavEntry.UnfinishedKey => i.Status != DownloadStatus.Completed,
        NavEntry.FinishedKey => i.Status == DownloadStatus.Completed,
        _ => i.Category == key
    };

    private bool MatchesSearch(DownloadItem i)
        => _search.Length == 0
           || i.FileName.Contains(_search, StringComparison.OrdinalIgnoreCase)
           || i.Url.Contains(_search, StringComparison.OrdinalIgnoreCase);

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is NavEntry { IsHeader: false } n) _currentNav = n;
        _view.Refresh();
        UpdateHeader();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text.Trim();
        _view.Refresh();
        UpdateHeader();
    }

    // ---- periodic refresh --------------------------------------------------------

    private void Refresh()
    {
        double total = 0;
        int active = 0, queued = 0;
        foreach (var i in _items)
        {
            i.Notify();
            if (i.IsActive) { active++; total += i.SpeedBytesPerSec; }
            else if (i.Status == DownloadStatus.Queued) queued++;
        }
        foreach (var n in _nav)
            if (!n.IsHeader) n.Count = _items.Count(i => Matches(n.Key, i));

        SpeedBig.Text = total > 0 ? Formatting.Speed(total) : "0 B/s";
        SpeedSub.Text = active == 0 ? "No active downloads"
            : $"{active} downloading" + (queued > 0 ? $" · {queued} queued" : "");
        ActiveText.Text = $"{active} active · {queued} queued · {_items.Count} total";
        long limit = _mgr.Settings.SpeedLimitBytesPerSec;
        LimitText.Text = limit > 0 ? "Speed limit: " + Formatting.Speed(limit) : "Speed limit: off";

        var server = App.Current.Server;
        BridgeDot.Fill = (Brush)FindResource(server.Running ? "SuccessBrush" : "DangerBrush");
        BridgeText.Text = server.Running ? $"Browser bridge on 127.0.0.1:{server.Port}" : "Browser bridge offline";
        var tools = _mgr.Tools;
        ToolsText.Text = tools.Ready ? "Video tools ready"
            : tools.IsBusy ? "Downloading video tools…" : "Video tools not installed yet";

        UpdateHeader();
    }

    private void UpdateHeader()
    {
        int shown = _view.Cast<object>().Count();
        ViewTitle.Text = _currentNav?.Label ?? "All downloads";
        ViewCount.Text = shown == 1 ? "1 item" : $"{shown} items";
        EmptyState.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_items.Count == 0)
        {
            EmptyTitle.Text = "No downloads yet";
            EmptyHint.Text = "Click Add URL, or start a download in Chrome or Firefox.";
        }
        else
        {
            EmptyTitle.Text = "Nothing here";
            EmptyHint.Text = _search.Length > 0 ? "No downloads match your search." : "No downloads in this view.";
        }
    }

    // ---- selection helpers -------------------------------------------------------

    private List<DownloadItem> Selection => DownloadsGrid.SelectedItems.Cast<DownloadItem>().ToList();
    private DownloadItem? Selected => DownloadsGrid.SelectedItem as DownloadItem;

    private void Info(string title, string message)
        => DialogWindow.Show(this, title, message, "", (Brush)FindResource("AccentBrush"),
            new DialogButton("OK", ButtonKind.Primary));

    // ---- toolbar -------------------------------------------------------------------

    private void OnAddUrl(object sender, RoutedEventArgs e) => App.Current.ShowNewDownload(null, video: false, owner: this);
    private void OnAddVideo(object sender, RoutedEventArgs e) => App.Current.ShowNewDownload(null, video: true, owner: this);

    private void OnResume(object sender, RoutedEventArgs e)
    {
        var sel = Selection;
        foreach (var i in sel)
            if (i.Status != DownloadStatus.Completed && !_mgr.IsRunning(i.Id)) _mgr.Start(i.Id);
        if (sel.Count == 1 && sel[0].Status != DownloadStatus.Completed && _mgr.Settings.ShowProgressWindow)
            App.Current.ShowProgress(sel[0]);
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        foreach (var i in Selection) _mgr.Pause(i.Id);
    }

    private void OnPauseAll(object sender, RoutedEventArgs e) => _mgr.PauseAll();

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        var sel = Selection;
        if (sel.Count == 0) return;
        string what = sel.Count == 1 ? $"\"{sel[0].FileName}\"" : $"{sel.Count} downloads";
        int r = DialogWindow.Show(this, $"Remove {what}?",
            "Keep the downloaded files on disk, or delete them too.",
            "", (Brush)FindResource("DangerBrush"),
            new DialogButton("Cancel"), new DialogButton("Remove from list"), new DialogButton("Delete files", ButtonKind.Danger));
        if (r <= 0) return;
        foreach (var i in sel) _mgr.Remove(i.Id, deleteFile: r == 2);
    }

    private void OnClearCompleted(object sender, RoutedEventArgs e) => _mgr.RemoveCompleted();

    private void OnConvert(object sender, RoutedEventArgs e)
    {
        var i = Selected;
        if (i is not { Status: DownloadStatus.Completed })
        {
            Info("Select a finished download", "Convert works on a file that has finished downloading.");
            return;
        }
        if (!_mgr.Tools.HasFfmpeg)
        {
            Info("ffmpeg isn't ready yet", "The video tools are still downloading in the background. Try again in a moment.");
            return;
        }
        var w = new ConvertWindow(i.FileName) { Owner = this };
        if (w.ShowDialog() == true)
            _mgr.AddConvert(i.FullPath, Path.GetFileNameWithoutExtension(i.FileName) + w.TargetExtension);
    }

    private void OnScheduler(object sender, RoutedEventArgs e)
    {
        var w = new SchedulerWindow(_mgr.Settings) { Owner = this };
        if (w.ShowDialog() == true) _mgr.UpdateSettings(w.Result);
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow(_mgr.Settings) { Owner = this };
        if (w.ShowDialog() == true) _mgr.UpdateSettings(w.Result);
    }

    // ---- row actions ---------------------------------------------------------------

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        if (Selected is { Status: DownloadStatus.Completed } i) Shell.Open(i.FullPath);
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) => Shell.Reveal(Selected?.FullPath, _mgr.Settings.DownloadFolder);

    private void OnShowProgress(object sender, RoutedEventArgs e)
    {
        if (Selected is { } i) App.Current.ShowProgress(i);
    }

    private void OnRelocate(object sender, RoutedEventArgs e)
    {
        if (Selected is { } i) new RelocateWindow(i) { Owner = this }.ShowDialog();
    }

    private void OnCopyUrl(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } i) return;
        try { Clipboard.SetText(i.Url); } catch { /* clipboard busy */ }
    }

    private void OnRemoveFromList(object sender, RoutedEventArgs e)
    {
        foreach (var i in Selection) _mgr.Remove(i.Id, deleteFile: false);
    }

    private void OnDeleteFile(object sender, RoutedEventArgs e)
    {
        var sel = Selection;
        if (sel.Count == 0) return;
        string what = sel.Count == 1 ? $"\"{sel[0].FileName}\"" : $"{sel.Count} files";
        int r = DialogWindow.Show(this, $"Delete {what} from disk?",
            "The file is deleted permanently and removed from the list.",
            "", (Brush)FindResource("DangerBrush"),
            new DialogButton("Cancel"), new DialogButton("Delete", ButtonKind.Danger));
        if (r == 1)
            foreach (var i in sel) _mgr.Remove(i.Id, deleteFile: true);
    }

    private void OnRowMenuOpened(object sender, RoutedEventArgs e)
    {
        var sel = Selection;
        var one = sel.Count == 1 ? sel[0] : null;
        MiOpen.IsEnabled = one is { Status: DownloadStatus.Completed };
        MiFolder.IsEnabled = one != null;
        MiProgress.IsEnabled = one != null;
        MiResume.IsEnabled = sel.Any(i => i.Status != DownloadStatus.Completed && !_mgr.IsRunning(i.Id));
        MiPause.IsEnabled = sel.Any(i => _mgr.IsRunning(i.Id) || i.Status == DownloadStatus.Queued);
        MiMove.IsEnabled = one != null;
        MiCopy.IsEnabled = one != null;
        MiConvert.IsEnabled = one is { Status: DownloadStatus.Completed };
    }

    // ---- input -----------------------------------------------------------------------

    /// <summary>Hides lower-priority columns when the list is narrow so the rest stay readable.</summary>
    private void OnGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = e.NewSize.Width;
        // Name (min 220) + Size 150 + Progress 185 + Speed 90 = 645; + Time left 85; + Added 115.
        AddedColumn.Visibility = w < 855 ? Visibility.Collapsed : Visibility.Visible;
        EtaColumn.Visibility = w < 740 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is DownloadItem i)
            App.Current.ShowProgress(i);
    }

    private void OnGridRightDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row != null && !row.IsSelected)
        {
            DownloadsGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) OnDeleteFile(sender, e);
            else OnRemoveFromList(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Selected is { } i)
        {
            App.Current.ShowProgress(i);
            e.Handled = true;
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.N) { OnAddUrl(sender, e); e.Handled = true; }
        else if (e.Key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T)
            d = d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); } // keep running in the tray
        base.OnClosing(e);
    }
}
