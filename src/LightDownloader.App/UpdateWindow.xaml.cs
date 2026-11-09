using System.Diagnostics;
using System.Windows;
using LightDownloader.Core.Update;
using LightDownloader.Core.Util;

namespace LightDownloader.App;

/// <summary>"A new version is available" prompt: downloads the installer, then runs it silently and exits.</summary>
public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _info;
    private CancellationTokenSource? _cts;
    private bool _closing;

    public UpdateWindow(UpdateInfo info, Version current)
    {
        InitializeComponent();
        _info = info;
        CurrentText.Text = current.ToString(3);
        NewText.Text = info.Version;
        SizeText.Text = info.Size > 0 ? Formatting.Bytes(info.Size) : "—";
        NotesBox.Text = string.IsNullOrWhiteSpace(info.Notes) ? "Improvements and fixes." : info.Notes.Trim();
        ActiveNote.Visibility = App.Current.Manager.Items.Any(i => i.IsActive)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnUpdate(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = SkipButton.IsEnabled = false;
        LaterButton.Content = "Cancel";
        Progress.Visibility = StatusText.Visibility = Visibility.Visible;
        Progress.Value = 0;
        StatusText.ClearValue(ForegroundProperty);
        StatusText.Text = "Downloading the update…";
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                Progress.Value = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
                StatusText.Text = p.Total > 0
                    ? $"Downloading the update… {Formatting.Bytes(p.Done)} of {Formatting.Bytes(p.Total)}"
                    : $"Downloading the update… {Formatting.Bytes(p.Done)}";
            });
            var setup = await App.Current.Updater.DownloadAsync(_info, progress, _cts.Token);

            StatusText.Text = "Installing the update…";
            // /UPDATE=1 makes setup start the app again when it's done.
            Process.Start(new ProcessStartInfo(setup, "/SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1") { UseShellExecute = true });
            App.Current.ExitApp();
        }
        catch (OperationCanceledException)
        {
            if (!_closing) Close();
        }
        catch (Exception ex)
        {
            Progress.Visibility = Visibility.Collapsed;
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("ErrorTextBrush");
            StatusText.Text = "The update couldn't be installed: " + ex.Message;
            UpdateButton.Content = "Try again";
            UpdateButton.IsEnabled = SkipButton.IsEnabled = true;
            LaterButton.Content = "Close";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnLater(object sender, RoutedEventArgs e)
    {
        if (_cts != null) _cts.Cancel(); // the download stops; the window closes when it does
        else Close();
    }

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        App.Current.SkipVersion(_info.Version);
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _cts?.Cancel();
        base.OnClosing(e);
    }
}
