using System.Windows;
using LightDownloader.Core.Models;
using LightDownloader.Core.Queue;
using LightDownloader.Core.Server;
using Drawing = System.Drawing;
using WF = System.Windows.Forms;

namespace LightDownloader.App;

public partial class App : Application
{
    public DownloadManager Manager = null!;
    public BridgeServer Server = null!;

    private WF.NotifyIcon _tray = null!;
    private MainWindow _main = null!;
    private static Mutex? _mutex;

    public static new App Current => (App)Application.Current;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _mutex = new Mutex(true, "LightDownloader_SingleInstance_8F2A", out bool isNew);
        if (!isNew) { Shutdown(); return; }

        Manager = new DownloadManager();
        Server = new BridgeServer(Manager);
        try { Server.Start(Manager.Settings.ServerPort); } catch { /* bridge optional */ }

        // Fetch yt-dlp + ffmpeg in the background (not bundled) so video/convert work later.
        Manager.Tools.EnsureInBackground();

        _main = new MainWindow();
        SetupTray();

        Server.DownloadCaptured += item =>
            Dispatcher.Invoke(() => _tray.ShowBalloonTip(3000, "Download started", item.FileName, WF.ToolTipIcon.Info));

        Manager.ItemChanged += item =>
        {
            if (item.Status == DownloadStatus.Completed)
                Dispatcher.Invoke(() => _tray.ShowBalloonTip(3000, "Download complete", item.FileName, WF.ToolTipIcon.Info));
        };

        if (!Manager.Settings.StartMinimized) _main.Show();
    }

    private void SetupTray()
    {
        _tray = new WF.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Light Downloader"
        };
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowMain());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMain();
    }

    public void ShowMain()
    {
        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    public void ExitApp()
    {
        try { _tray.Visible = false; } catch { }
        _main.AllowClose = true;
        try { Server.Dispose(); } catch { }
        try { Manager.Dispose(); } catch { }
        Shutdown();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        try { _tray.Visible = false; } catch { }
    }
}
