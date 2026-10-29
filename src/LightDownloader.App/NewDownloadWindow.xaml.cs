using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LightDownloader.Core.Http;
using LightDownloader.Core.Models;
using LightDownloader.Core.Queue;
using LightDownloader.Core.Util;
using Microsoft.Win32;

namespace LightDownloader.App;

/// <summary>
/// "New download" dialog (IDM's Download File Info): pick the file name, category and folder,
/// optionally remember the folder for that category, then start now or later.
/// </summary>
public partial class NewDownloadWindow : Window
{
    private readonly DownloadManager _mgr = App.Current.Manager;
    private readonly DownloadRequest _req;
    private readonly bool _video;
    private readonly DispatcherTimer _probeDelay;
    private CancellationTokenSource? _probeCts;
    private long _probedSize = -1;

    // Track what the user changed by hand so probe results never overwrite it.
    private bool _programmatic, _nameEdited, _categoryEdited, _folderEdited;

    public NewDownloadWindow(DownloadRequest? request, bool video)
    {
        InitializeComponent();
        WindowTheme.Apply(this);

        _req = request ?? new DownloadRequest();
        _video = video || string.Equals(_req.Kind, "video", StringComparison.OrdinalIgnoreCase);
        _probeDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _probeDelay.Tick += async (_, _) => { _probeDelay.Stop(); await ProbeAsync(); };

        CategoryBox.ItemsSource = Categories.All;

        _programmatic = true;
        UrlBox.Text = !string.IsNullOrWhiteSpace(_req.Url) ? _req.Url : ClipboardUrl();
        if (_video)
        {
            Title = HeaderTitle.Text = "New video download";
            HeaderInfo.Text = "Video and audio are fetched with yt-dlp and merged into one file.";
            NameBox.Text = "Named after the video's title";
            NameBox.IsEnabled = false;
            CategoryBox.SelectedItem = Categories.Video;
            CategoryBox.IsEnabled = false;
        }
        else
        {
            NameBox.Text = !string.IsNullOrWhiteSpace(_req.FileName)
                ? HttpProbe.Sanitize(_req.FileName!)
                : GuessName(UrlBox.Text);
            CategoryBox.SelectedItem = Categories.FromFileName(NameBox.Text);
        }
        FolderBox.Text = !string.IsNullOrWhiteSpace(_req.SaveFolder) ? _req.SaveFolder! : _mgr.FolderFor(CurrentCategory);
        _programmatic = false;
        // A name supplied by the browser (Content-Disposition / download attribute) is authoritative.
        _nameEdited = !string.IsNullOrWhiteSpace(_req.FileName);
        UpdateVisuals();

        Loaded += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(UrlBox.Text)) UrlBox.Focus();
            else StartButton.Focus();
            await ProbeAsync();
        };
        Closed += (_, _) => _probeCts?.Cancel();
    }

    private string CurrentCategory => CategoryBox.SelectedItem as string ?? Categories.Other;

    // ---- probing ---------------------------------------------------------------

    private async Task ProbeAsync()
    {
        if (_video) return;
        var url = UrlBox.Text.Trim();
        if (!IsHttp(url))
        {
            HeaderInfo.Text = "Enter an http:// or https:// address.";
            return;
        }

        _probeCts?.Cancel();
        var cts = _probeCts = new CancellationTokenSource();
        HeaderInfo.Text = "Getting file info…";

        var probe = await _mgr.ProbeAsync(new DownloadRequest
        {
            Url = url,
            Referrer = _req.Referrer,
            UserAgent = _req.UserAgent,
            Cookies = _req.Cookies,
            Headers = _req.Headers
        }, cts.Token);
        if (cts.IsCancellationRequested || !IsLoaded) return;

        if (probe == null)
        {
            HeaderInfo.Text = "Couldn't reach the server — you can still start the download.";
            return;
        }

        _programmatic = true;
        if (!_nameEdited && !string.IsNullOrWhiteSpace(probe.FileName) && probe.FileName != "download")
            NameBox.Text = probe.FileName;
        if (!_categoryEdited) CategoryBox.SelectedItem = Categories.FromFileName(NameBox.Text);
        if (!_folderEdited) FolderBox.Text = _mgr.FolderFor(CurrentCategory);
        _programmatic = false;

        _probedSize = probe.TotalSize;
        HeaderInfo.Text = (probe.TotalSize > 0 ? Formatting.Bytes(probe.TotalSize) : "Unknown size") + "  ·  " +
                          (probe.SupportsRanges ? "Resumable, multi-connection" : "Single connection, no resume");
        UpdateVisuals();
    }

    // ---- field changes ---------------------------------------------------------

    private void OnUrlChanged(object sender, TextChangedEventArgs e)
    {
        if (_programmatic || _video) return;
        if (!_nameEdited)
        {
            _programmatic = true;
            NameBox.Text = GuessName(UrlBox.Text);
            if (!_categoryEdited) CategoryBox.SelectedItem = Categories.FromFileName(NameBox.Text);
            if (!_folderEdited) FolderBox.Text = _mgr.FolderFor(CurrentCategory);
            _programmatic = false;
        }
        _probeDelay.Stop();
        _probeDelay.Start();
        UpdateVisuals();
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        if (!_programmatic && !_video)
        {
            _nameEdited = true;
            if (!_categoryEdited)
            {
                _programmatic = true;
                CategoryBox.SelectedItem = Categories.FromFileName(NameBox.Text);
                if (!_folderEdited) FolderBox.Text = _mgr.FolderFor(CurrentCategory);
                _programmatic = false;
            }
        }
        UpdateVisuals();
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_programmatic)
        {
            _categoryEdited = true;
            if (!_folderEdited)
            {
                _programmatic = true;
                FolderBox.Text = _mgr.FolderFor(CurrentCategory);
                _programmatic = false;
            }
        }
        UpdateVisuals();
    }

    private void OnFolderChanged(object sender, TextChangedEventArgs e)
    {
        if (!_programmatic) _folderEdited = true;
        UpdateVisuals();
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose where to save" };
        try { if (Directory.Exists(FolderBox.Text)) dlg.InitialDirectory = FolderBox.Text; } catch { }
        if (dlg.ShowDialog(this) == true)
        {
            FolderBox.Text = dlg.FolderName;  // marks the folder as user-chosen
            RememberBox.IsChecked = true;     // picking a folder by hand usually means "keep using it"
        }
    }

    private void UpdateVisuals()
    {
        if (TypeBadge == null) return; // during InitializeComponent
        var category = CurrentCategory;
        TypeBadge.Background = CategoryVisuals.TintFor(category);
        TypeGlyph.Text = CategoryVisuals.GlyphFor(category);
        TypeGlyph.Foreground = CategoryVisuals.BrushFor(category);
        RememberBox.Content = $"Remember this folder for {category} files";

        string? warning = null;
        if (!_video)
        {
            try
            {
                var name = NameBox.Text.Trim();
                if (name.Length > 0 && File.Exists(Path.Combine(FolderBox.Text.Trim(), HttpProbe.Sanitize(name))))
                    warning = $"A file named \"{name}\" already exists here — the new one will get a number added.";
            }
            catch { /* invalid path while typing */ }
        }
        ShowWarning(warning);
    }

    private void ShowWarning(string? text)
    {
        WarnText.Text = text ?? "";
        WarnText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- actions -----------------------------------------------------------------

    private void OnStart(object sender, RoutedEventArgs e) => Submit(start: true);
    private void OnLater(object sender, RoutedEventArgs e) => Submit(start: false);
    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void Submit(bool start)
    {
        var url = UrlBox.Text.Trim();
        if (!IsHttp(url)) { ShowWarning("Enter a valid http:// or https:// address."); UrlBox.Focus(); return; }

        var folder = FolderBox.Text.Trim();
        if (folder.Length == 0) { ShowWarning("Choose a folder to save into."); return; }
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) { ShowWarning("Can't use this folder: " + ex.Message); return; }

        if (_video && start && !_mgr.Tools.HasYtDlp)
        {
            ShowWarning("The video tools are still downloading in the background — try again in a moment, or choose Download later.");
            return;
        }

        if (RememberBox.IsChecked == true) _mgr.RememberFolder(CurrentCategory, folder);

        var item = _mgr.Add(new DownloadRequest
        {
            Url = url,
            Kind = _video ? "video" : null,
            FileName = _video ? null : NameBox.Text.Trim(),
            SaveFolder = folder,
            Referrer = _req.Referrer,
            UserAgent = _req.UserAgent,
            Cookies = _req.Cookies,
            Headers = _req.Headers,
            FileSize = _probedSize > 0 ? _probedSize : _req.FileSize
        }, autoStart: start);

        if (start && _mgr.Settings.ShowProgressWindow) App.Current.ShowProgress(item);
        Close();
    }

    // ---- helpers ---------------------------------------------------------------

    private static bool IsHttp(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    private static string GuessName(string url)
    {
        try
        {
            if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u))
            {
                var name = Path.GetFileName(WebUtility.UrlDecode(u.AbsolutePath));
                if (!string.IsNullOrWhiteSpace(name)) return HttpProbe.Sanitize(name);
            }
        }
        catch { /* ignore */ }
        return "download";
    }

    private static string ClipboardUrl()
    {
        try
        {
            var text = Clipboard.GetText().Trim();
            return IsHttp(text) ? text : "";
        }
        catch { return ""; }
    }
}
