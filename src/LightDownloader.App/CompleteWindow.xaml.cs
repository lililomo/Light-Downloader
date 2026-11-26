using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using LightDownloader.Core.Models;
using LightDownloader.Core.Util;

namespace LightDownloader.App;

/// <summary>IDM's "Download complete" window, shown in place of the progress window when a download finishes.</summary>
public partial class CompleteWindow : Window
{
    private readonly DownloadItem _item;

    public CompleteWindow(DownloadItem item)
    {
        InitializeComponent();
        _item = item;
        var path = item.FullPath;

        bool convert = item.Kind == DownloadKind.Convert;
        Title = Heading.Text = convert ? "Conversion complete" : "Download complete";
        AddressLabel.Text = convert ? "Converted from" : "Address";
        AddressBox.Text = item.Url;
        PathBox.Text = path;
        TypeIcon.Source = ShellIcons.ForFile(item.FileName, large: true);

        long size = File.Exists(path) ? new FileInfo(path).Length : item.TotalSize;
        SizeText.Text = size >= 0
            ? $"{(convert ? "Saved" : "Downloaded")} {Formatting.Bytes(size)} ({size.ToString("N0", CultureInfo.CurrentCulture)} bytes)"
            : "";
        OpenButton.IsEnabled = OpenWithButton.IsEnabled = File.Exists(path);

        // Long addresses and paths: show their end (the file name), not their start.
        Loaded += (_, _) =>
        {
            AddressBox.ScrollToHorizontalOffset(double.MaxValue);
            PathBox.ScrollToHorizontalOffset(double.MaxValue);
        };
        Closed += (_, _) => { if (DontShowBox.IsChecked == true) App.Current.TurnOffCompleteDialog(); };
    }

    public Guid ItemId => _item.Id;

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        Shell.Open(_item.FullPath);
        Close();
    }

    private void OnOpenWith(object sender, RoutedEventArgs e)
    {
        Shell.OpenWith(_item.FullPath, new WindowInteropHelper(this).Handle);
        Close();
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        Shell.Reveal(_item.FullPath, _item.SaveFolder);
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
