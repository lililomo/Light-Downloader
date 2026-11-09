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
/// IDM's "Download File Info" dialog: check the address, category and where to save (optionally
/// remembering the folder for that category), pick a video quality, then start now or later.
/// </summary>
public partial class NewDownloadWindow : Window
{
    public sealed record Quality(string Id, string Label);

    private static readonly Quality[] Qualities =
    {
        new("best", "Best available"),
        new("2160", "2160p (4K)"),
        new("1440", "1440p"),
        new("1080", "1080p"),
        new("720", "720p"),
        new("480", "480p"),
        new("360", "360p"),
        new("m4a", "Audio only (M4A)"),
        new("mp3", "Audio only (MP3)")
    };

    private readonly DownloadManager _mgr = App.Current.Manager;
    private readonly DownloadRequest _req;
    private readonly bool _video;
    private readonly DispatcherTimer _probeDelay;
    private CancellationTokenSource? _probeCts;
    private long _probedSize = -1;
    private string _folder = "", _name = "";

    // Track what the user changed by hand so probe results never overwrite it.
    private bool _programmatic, _nameEdited, _categoryEdited, _folderEdited;

    public NewDownloadWindow(DownloadRequest? request, bool video)
    {
        InitializeComponent();

        _req = request ?? new DownloadRequest();
        _video = video || string.Equals(_req.Kind, "video", StringComparison.OrdinalIgnoreCase);
        _probeDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _probeDelay.Tick += async (_, _) => { _probeDelay.Stop(); await ProbeAsync(); };

        CategoryBox.ItemsSource = Categories.All;

        _programmatic = true;
        UrlBox.Text = !string.IsNullOrWhiteSpace(_req.Url) ? _req.Url : ClipboardUrl();
        if (_video)
        {
            Title = "Download Video";
            PathLabel.Text = "Save to";
            CategoryBox.SelectedItem = Categories.Video;
            CategoryBox.IsEnabled = false;
            QualityBox.ItemsSource = QualityList(_req.Format);
            QualityBox.SelectedValue = string.IsNullOrWhiteSpace(_req.Format) ? "best" : _req.Format;
            InfoText.Text = string.IsNullOrWhiteSpace(_req.Title) ? "Named after the video title" : _req.Title;
            InfoText.ToolTip = InfoText.Text;
        }
        else
        {
            QualityLabel.Visibility = QualityBox.Visibility = Visibility.Collapsed;
            _name = !string.IsNullOrWhiteSpace(_req.FileName) ? HttpProbe.Sanitize(_req.FileName!) : GuessName(UrlBox.Text);
            CategoryBox.SelectedItem = Categories.FromFileName(_name);
        }
        _folder = !string.IsNullOrWhiteSpace(_req.SaveFolder) ? _req.SaveFolder! : _mgr.FolderFor(CurrentCategory);
        ShowPath();
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

    private static List<Quality> QualityList(string? preset)
    {
        var list = Qualities.ToList();
        // A height picked in the browser that isn't in the standard list (e.g. 240p).
        if (int.TryParse(preset, out var h) && list.All(q => q.Id != preset))
            list.Insert(list.FindIndex(q => int.TryParse(q.Id, out var x) && x < h) is var i and >= 0 ? i : list.Count - 2,
                new Quality(preset!, $"{h}p"));
        return list;
    }

    private void ShowPath()
    {
        bool was = _programmatic;
        _programmatic = true;
        PathBox.Text = _video ? _folder : Path.Combine(_folder, _name);
        _programmatic = was;
    }

    // ---- probing ---------------------------------------------------------------

    private async Task ProbeAsync()
    {
        if (_video) return;
        var url = UrlBox.Text.Trim();
        if (!IsHttp(url))
        {
            InfoText.Text = "Enter an http:// or https:// address.";
            return;
        }

        _probeCts?.Cancel();
        var cts = _probeCts = new CancellationTokenSource();
        InfoText.Text = "Getting file information…";

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
            InfoText.Text = _req.FileSize > 0 ? $"File size: {Formatting.Bytes(_req.FileSize)}" : "File size: unknown";
            return;
        }

        _programmatic = true;
        if (!_nameEdited && !string.IsNullOrWhiteSpace(probe.FileName) && probe.FileName != "download")
            _name = probe.FileName;
        if (!_categoryEdited) CategoryBox.SelectedItem = Categories.FromFileName(_name);
        if (!_folderEdited) _folder = _mgr.FolderFor(CurrentCategory);
        ShowPath();
        _programmatic = false;

        _probedSize = probe.TotalSize;
        InfoText.Text = "File size: " + (probe.TotalSize > 0 ? Formatting.Bytes(probe.TotalSize) : "unknown") +
                        (probe.SupportsRanges ? "" : "\nThis server can't resume the download.");
        UpdateVisuals();
    }

    // ---- field changes ---------------------------------------------------------

    private void OnUrlChanged(object sender, TextChangedEventArgs e)
    {
        if (_programmatic || _video) return;
        if (!_nameEdited)
        {
            _programmatic = true;
            _name = GuessName(UrlBox.Text);
            if (!_categoryEdited) CategoryBox.SelectedItem = Categories.FromFileName(_name);
            if (!_folderEdited) _folder = _mgr.FolderFor(CurrentCategory);
            ShowPath();
            _programmatic = false;
        }
        _probeDelay.Stop();
        _probeDelay.Start();
        UpdateVisuals();
    }

    private void OnPathChanged(object sender, TextChangedEventArgs e)
    {
        if (_programmatic) return;
        var text = PathBox.Text.Trim();
        if (_video)
        {
            _folder = text;
            _folderEdited = true;
        }
        else
        {
            string folder, name;
            try { folder = Path.GetDirectoryName(text) ?? ""; name = Path.GetFileName(text); }
            catch { return; } // invalid while typing
            if (!string.Equals(folder, _folder, StringComparison.OrdinalIgnoreCase)) _folderEdited = true;
            if (name != _name)
            {
                _nameEdited = true;
                if (!_categoryEdited)
                {
                    _programmatic = true;
                    CategoryBox.SelectedItem = Categories.FromFileName(name);
                    _programmatic = false;
                }
            }
            _folder = folder;
            _name = name;
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
                _folder = _mgr.FolderFor(CurrentCategory);
                ShowPath();
            }
        }
        UpdateVisuals();
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (_video)
        {
            var dlg = new OpenFolderDialog { Title = "Choose where to save the video" };
            try { if (Directory.Exists(_folder)) dlg.InitialDirectory = _folder; } catch { }
            if (dlg.ShowDialog(this) != true) return;
            _folder = dlg.FolderName;
        }
        else
        {
            var dlg = new SaveFileDialog { Title = "Save As", FileName = _name, Filter = "All files (*.*)|*.*", OverwritePrompt = false };
            try { if (Directory.Exists(_folder)) dlg.InitialDirectory = _folder; } catch { }
            if (dlg.ShowDialog(this) != true) return;
            _folder = Path.GetDirectoryName(dlg.FileName) ?? _folder;
            _name = Path.GetFileName(dlg.FileName);
            _nameEdited = true;
        }
        _folderEdited = true;
        ShowPath();
        RememberBox.IsChecked = true; // picking a folder by hand usually means "keep using it"
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (TypeIcon == null) return; // during InitializeComponent
        var category = CurrentCategory;
        TypeIcon.Source = ShellIcons.ForFile(_video ? "video.mp4" : _name, large: true);
        RememberBox.Content = $"Remember this path for \"{category}\" category";

        string? warning = null;
        if (!_video)
        {
            try
            {
                if (_name.Length > 0 && File.Exists(Path.Combine(_folder, HttpProbe.Sanitize(_name))))
                    warning = $"\"{_name}\" already exists in this folder; the new file will get a number added.";
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

        var folder = _folder.Trim();
        if (folder.Length == 0 || (!_video && _name.Trim().Length == 0)) { ShowWarning("Choose where to save the file."); PathBox.Focus(); return; }
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) { ShowWarning("Can't use this folder: " + ex.Message); return; }

        if (_video && start && !_mgr.Tools.HasYtDlp)
        {
            ShowWarning("The video tools are still downloading in the background. Try again in a moment, or choose Download Later.");
            return;
        }

        if (RememberBox.IsChecked == true) _mgr.RememberFolder(CurrentCategory, folder);

        var item = _mgr.Add(new DownloadRequest
        {
            Url = url,
            Kind = _video ? "video" : null,
            Format = _video ? QualityBox.SelectedValue as string : null,
            Title = _video ? _req.Title : null,
            FileName = _video ? null : _name.Trim(),
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
