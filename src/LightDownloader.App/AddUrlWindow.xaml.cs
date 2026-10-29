using System.Windows;

namespace LightDownloader.App;

public partial class AddUrlWindow : Window
{
    public string Url => UrlBox.Text;
    public string FileName => NameBox.Text;

    public AddUrlWindow()
    {
        InitializeComponent();
        try
        {
            var clip = Clipboard.GetText();
            if (!string.IsNullOrWhiteSpace(clip) &&
                (clip.StartsWith("http://") || clip.StartsWith("https://")))
                UrlBox.Text = clip.Trim();
        }
        catch { /* clipboard may be empty/locked */ }

        Loaded += (_, _) => { UrlBox.Focus(); UrlBox.SelectAll(); };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UrlBox.Text)) return;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
