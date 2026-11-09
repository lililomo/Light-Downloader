using System.IO;
using System.Reflection;
using System.Windows;
using LightDownloader.Core.Models;
using LightDownloader.Core.Queue;
using LightDownloader.Core.Server;
using Drawing = System.Drawing;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace LightDownloader.App;

public partial class App : Application
{
    public DownloadManager Manager = null!;
    public BridgeServer Server = null!;

    private WF.NotifyIcon _tray = null!;
    private MainWindow _main = null!;
    private readonly Dictionary<Guid, ProgressWindow> _progress = new();
    private readonly HashSet<Guid> _announcedComplete = new();
    private static Mutex? _mutex;
    private static EventWaitHandle? _exitSignal;

    public static new App Current => (App)Application.Current;

#if DEBUG
    // Debug builds keep their own identity and data so they run beside an installed copy.
    private const string Instance = "LightDownloader_Dev";
#else
    private const string Instance = "LightDownloader";
#endif
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _mutex = new Mutex(true, Instance + "_SingleInstance_8F2A", out bool isNew);
        if (!isNew) { Shutdown(); return; }

        // The installer and uninstaller signal this to close a running copy cleanly (it lives in the tray).
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Instance + "_Exit_8F2A");
        ThreadPool.RegisterWaitForSingleObject(_exitSignal,
            (_, _) => Dispatcher.InvokeAsync(ExitApp), null, Timeout.Infinite, executeOnlyOnce: true);

        Manager = new DownloadManager(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Instance));
        Server = new BridgeServer(Manager)
        {
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0"
        };
        try { Server.Start(Manager.Settings.ServerPort); } catch { /* bridge optional */ }
        ApplyRunAtStartup(Manager.Settings.RunAtStartup);

        // Fetch yt-dlp + ffmpeg in the background (not bundled) so video/convert work later.
        Manager.Tools.EnsureInBackground();

        foreach (var i in Manager.Items)
            if (i.Status == DownloadStatus.Completed) _announcedComplete.Add(i.Id);

        _main = new MainWindow();
        SetupTray();

        // "Ask where to save" on: a captured download opens the New-download dialog first.
        Server.DownloadRequested += req => Dispatcher.InvokeAsync(() => ShowNewDownload(req));
        // "Ask where to save" off: it starts straight away.
        Server.DownloadCaptured += item => Dispatcher.InvokeAsync(() =>
        {
            _tray.ShowBalloonTip(3000, "Download started", item.FileName, WF.ToolTipIcon.Info);
            if (Manager.Settings.ShowProgressWindow) ShowProgress(item);
        });
        Manager.ItemChanged += item =>
        {
            if (item.Status != DownloadStatus.Completed) return;
            Dispatcher.InvokeAsync(() =>
            {
                if (_announcedComplete.Add(item.Id))
                    _tray.ShowBalloonTip(3000, "Download complete", item.FileName, WF.ToolTipIcon.Info);
            });
        };
        Manager.ItemRemoved += item => Dispatcher.InvokeAsync(() =>
        {
            if (_progress.TryGetValue(item.Id, out var w)) w.Close();
        });

        // Windows starts us with --tray (see ApplyRunAtStartup): stay in the notification area.
        bool tray = e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        if (!tray && !Manager.Settings.StartMinimized) _main.Show();
    }

    /// <summary>Adds or removes the "start with Windows" entry (per user; no admin rights needed).</summary>
    private static void ApplyRunAtStartup(bool enabled)
    {
#if !DEBUG
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && Environment.ProcessPath is { } exe) key.SetValue("LightDownloader", $"\"{exe}\" --tray");
            else key.DeleteValue("LightDownloader", throwOnMissingValue: false);
        }
        catch { /* policy may block it; the option just has no effect */ }
#endif
    }

    /// <summary>Opens the Options dialog and applies what the user saved.</summary>
    public void ShowOptions(Window? owner)
    {
        var w = new SettingsWindow(Manager.Settings, Server.SeenBrowsers);
        if (owner is { IsVisible: true }) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (w.ShowDialog() != true) return;
        Manager.UpdateSettings(w.Result);
        ApplyRunAtStartup(w.Result.RunAtStartup);
    }

    /// <summary>Opens the New-download dialog (from the toolbar, the tray, or a browser capture).</summary>
    public void ShowNewDownload(DownloadRequest? request, bool video = false, Window? owner = null)
    {
        var w = new NewDownloadWindow(request, video);
        if (owner is { IsVisible: true })
        {
            w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        w.Show();
        // Captures arrive while the app sits in the tray: bring the dialog to the front.
        w.Topmost = true;
        w.Activate();
        w.Topmost = false;
    }

    /// <summary>Shows (or focuses) the progress window of a download.</summary>
    public void ShowProgress(DownloadItem item)
    {
        if (_progress.TryGetValue(item.Id, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var w = new ProgressWindow(item);
        _progress[item.Id] = w;
        w.Closed += (_, _) => _progress.Remove(item.Id);
        w.Show();
    }

    private void SetupTray()
    {
        Drawing.Icon icon;
        try
        {
            var res = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            icon = new Drawing.Icon(res.Stream, WF.SystemInformation.SmallIconSize);
        }
        catch { icon = Drawing.SystemIcons.Application; }

        _tray = new WF.NotifyIcon { Icon = icon, Visible = true, Text = "Light Downloader" };
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("Open Light Downloader", null, (_, _) => ShowMain());
        menu.Items.Add("Add URL…", null, (_, _) => ShowNewDownload(null));
        menu.Items.Add("Options…", null, (_, _) => ShowOptions(_main.IsVisible ? _main : null));
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMain();
    }

    public void ShowMain()
    {
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    public void ExitApp()
    {
        try { _tray.Visible = false; } catch { }
        _main.AllowClose = true;
        foreach (var w in _progress.Values.ToList()) w.Close();
        try { Server.Dispose(); } catch { }
        try { Manager.Dispose(); } catch { }
        Shutdown();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        try { _tray.Visible = false; } catch { }
    }
}
