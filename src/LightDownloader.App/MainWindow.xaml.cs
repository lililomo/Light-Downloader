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
    private const string AllKey = "all", UnfinishedKey = "unfinished", FinishedKey = "finished";

    // Sortable columns: header text → property.
    private static readonly Dictionary<string, string> SortKeys = new()
    {
        ["File Name"] = nameof(DownloadItem.FileName),
        ["Size"] = nameof(DownloadItem.TotalSize),
        ["Status"] = nameof(DownloadItem.Status),
        ["Transfer rate"] = nameof(DownloadItem.SpeedBytesPerSec),
        ["Last Try Date"] = nameof(DownloadItem.LastTryAt),
        ["Address"] = nameof(DownloadItem.Url)
    };

    private readonly DownloadManager _mgr = App.Current.Manager;
    private readonly ObservableCollection<DownloadItem> _items = new();
    private readonly ICollectionView _view;
    private readonly DispatcherTimer _timer;
    private string _filter = AllKey;
    private GridLength _treeWidth = new(190);

    public bool AllowClose;

    public MainWindow()
    {
        InitializeComponent();

        // Fit the work area (e.g. a 1080-px-wide portrait monitor) instead of overflowing it.
        var work = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, work.Width - 32));
        Height = Math.Max(MinHeight, Math.Min(Height, work.Height - 48));

        foreach (var i in _mgr.Items) _items.Add(i);
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = o => o is DownloadItem i && Matches(_filter, i);
        _view.SortDescriptions.Add(new SortDescription(nameof(DownloadItem.AddedAt), ListSortDirection.Descending));
        if (_view is ICollectionViewLiveShaping live && live.CanChangeLiveFiltering)
        {
            // Rows move between "Unfinished" and "Finished" as their status changes.
            live.LiveFilteringProperties.Add(nameof(DownloadItem.Status));
            live.LiveFilteringProperties.Add(nameof(DownloadItem.Category));
            live.IsLiveFiltering = true;
        }
        DownloadsList.ItemsSource = _view;

        BuildTree();

        _mgr.ItemAdded += i => Dispatcher.InvokeAsync(() => { if (!_items.Contains(i)) _items.Add(i); });
        _mgr.ItemRemoved += i => Dispatcher.InvokeAsync(() => _items.Remove(i));

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    // ---- categories ------------------------------------------------------------

    private void BuildTree()
    {
        var all = Node(AllKey, "All Downloads");
        foreach (var c in Categories.All) all.Items.Add(Node(c, c));
        all.IsExpanded = true;
        CategoryTree.Items.Add(all);
        CategoryTree.Items.Add(Node(UnfinishedKey, "Unfinished"));
        CategoryTree.Items.Add(Node(FinishedKey, "Finished"));
        all.IsSelected = true;
    }

    private static TreeViewItem Node(string key, string label)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        header.Children.Add(new Image { Source = ShellIcons.Folder(), Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0) });
        header.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var node = new TreeViewItem { Header = header, Tag = key };
        System.Windows.Automation.AutomationProperties.SetName(node, label);
        return node;
    }

    private static bool Matches(string key, DownloadItem i) => key switch
    {
        AllKey => true,
        UnfinishedKey => i.Status != DownloadStatus.Completed,
        FinishedKey => i.Status == DownloadStatus.Completed,
        _ => i.Category == key
    };

    private void OnCategoryChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (CategoryTree.SelectedItem is TreeViewItem { Tag: string key }) _filter = key;
        _view.Refresh();
    }

    private void OnToggleCategories(object sender, RoutedEventArgs e)
    {
        bool show = CategoriesPanel.Visibility != Visibility.Visible;
        if (!show) _treeWidth = TreeColumn.Width;
        CategoriesPanel.Visibility = Splitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TreeColumn.Width = show ? _treeWidth : new GridLength(0);
        TreeColumn.MinWidth = show ? 120 : 0;
        SplitColumn.Width = new GridLength(show ? 4 : 0);
        MvCategories.IsChecked = show;
    }

    private void OnToggleToolbar(object sender, RoutedEventArgs e)
        => ToolbarPanel.Visibility = MvToolbar.IsChecked ? Visibility.Visible : Visibility.Collapsed;

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

        CountText.Text = $"{_items.Count} download{(_items.Count == 1 ? "" : "s")}" +
                         (active > 0 ? $", {active} active" : "") + (queued > 0 ? $", {queued} queued" : "");
        SpeedText.Text = "Speed: " + (total > 0 ? Formatting.Speed(total) : "0 B/s");
        long limit = _mgr.Settings.SpeedLimitBytesPerSec;
        LimitText.Text = limit > 0 ? "Limit: " + Formatting.Speed(limit) : "No speed limit";

        var server = App.Current.Server;
        BridgeText.Text = !server.Running ? "Browser integration: offline"
            : _mgr.Settings.BrowserCapture ? "Browser integration: on" : "Browser integration: off";
        BridgeText.ToolTip = server.Running ? $"Listening on 127.0.0.1:{server.Port}" : null;
        var tools = _mgr.Tools;
        ToolsText.Text = tools.Ready ? "" : tools.IsBusy ? "Downloading video tools…" : "Video tools not installed yet";
    }

    // ---- selection helpers -------------------------------------------------------

    private List<DownloadItem> Selection => DownloadsList.SelectedItems.Cast<DownloadItem>().ToList();
    private DownloadItem? Selected => DownloadsList.SelectedItem as DownloadItem;

    private void Info(string message) => MessageBox.Show(this, message, "Light Downloader", MessageBoxButton.OK, MessageBoxImage.Information);

    // ---- commands ------------------------------------------------------------------

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

    private void OnStartAll(object sender, RoutedEventArgs e) => _mgr.StartAll();
    private void OnPauseAll(object sender, RoutedEventArgs e) => _mgr.PauseAll();

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        var sel = Selection;
        if (sel.Count == 0) return;
        string what = sel.Count == 1 ? $"\"{sel[0].FileName}\"" : $"these {sel.Count} downloads";
        int r = DialogWindow.Show(this, "Delete downloads",
            $"Remove {what} from the list?\n\nChoose \"Delete files\" to also delete what was downloaded from your disk.",
            MessageBoxImage.Question, new[] { "Remove from list", "Delete files", "Cancel" }, defaultButton: 0, cancelButton: 2);
        if (r is 0 or 1)
            foreach (var i in sel) _mgr.Remove(i.Id, deleteFile: r == 1);
    }

    private void OnClearCompleted(object sender, RoutedEventArgs e) => _mgr.RemoveCompleted();

    private void OnConvert(object sender, RoutedEventArgs e)
    {
        var i = Selected;
        if (i is not { Status: DownloadStatus.Completed })
        {
            Info("Select a finished download first: Convert works on a file that has finished downloading.");
            return;
        }
        if (!_mgr.Tools.HasFfmpeg)
        {
            Info("The video tools (ffmpeg) are still downloading in the background. Try again in a moment.");
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

    private void OnSettings(object sender, RoutedEventArgs e) => App.Current.ShowOptions(this);

    private void OnExit(object sender, RoutedEventArgs e) => App.Current.ExitApp();

    private void OnChromeExtension(object sender, RoutedEventArgs e) => Shell.OpenIn("chrome.exe", Shell.ChromeExtensionUrl);
    private void OnFirefoxExtension(object sender, RoutedEventArgs e) => Shell.OpenIn("firefox.exe", Shell.FirefoxAddonUrl);
    private void OnWebsite(object sender, RoutedEventArgs e) => Shell.Open(Shell.WebsiteUrl);
    private async void OnCheckUpdates(object sender, RoutedEventArgs e) => await App.Current.CheckForUpdatesAsync(manual: true, owner: this);

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        MessageBox.Show(this, $"Light Downloader {v}\n\nA lightweight download manager for Windows.\n{Shell.WebsiteUrl}",
            "About Light Downloader", MessageBoxButton.OK, MessageBoxImage.Information);
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
        string what = sel.Count == 1 ? $"\"{sel[0].FileName}\"" : $"these {sel.Count} files";
        if (MessageBox.Show(this, $"Delete {what} from your disk?\n\nThe file is deleted permanently and removed from the list.",
                "Delete file", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK)
            foreach (var i in sel) _mgr.Remove(i.Id, deleteFile: true);
    }

    private void OnRowMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var sel = Selection;
        if (sel.Count == 0) { e.Handled = true; return; }
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

    private void OnFileMenuOpened(object sender, RoutedEventArgs e)
    {
        var sel = Selection;
        var one = sel.Count == 1 ? sel[0] : null;
        MfOpen.IsEnabled = one is { Status: DownloadStatus.Completed };
        MfFolder.IsEnabled = MfMove.IsEnabled = MfCopy.IsEnabled = one != null;
        MfRemove.IsEnabled = MfDelete.IsEnabled = sel.Count > 0;
    }

    // ---- list input -----------------------------------------------------------------

    private ListSortDirection _sortDir = ListSortDirection.Descending;
    private string _sortKey = nameof(DownloadItem.AddedAt);

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column.Header: string header } || !SortKeys.TryGetValue(header, out var key))
            return;
        _sortDir = key == _sortKey && _sortDir == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        _sortKey = key;
        _view.SortDescriptions.Clear();
        _view.SortDescriptions.Add(new SortDescription(key, _sortDir));
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<ListViewItem>(e.OriginalSource as DependencyObject)?.Content is not DownloadItem i) return;
        if (i.Status == DownloadStatus.Completed && File.Exists(i.FullPath)) Shell.Open(i.FullPath);
        else App.Current.ShowProgress(i);
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
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
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { OnAddUrl(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A && !(e.OriginalSource is TextBox))
        {
            DownloadsList.SelectAll();
            e.Handled = true;
        }
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
