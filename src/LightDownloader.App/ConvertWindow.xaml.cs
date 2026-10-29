using System.Windows;

namespace LightDownloader.App;

public partial class ConvertWindow : Window
{
    public string TargetExtension { get; private set; } = ".mp4";

    public ConvertWindow(string sourceName)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        SourceText.Text = sourceName;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        TargetExtension = FormatBox.SelectedIndex switch
        {
            1 => ".mkv",
            2 => ".mp3",
            3 => ".m4a",
            4 => ".wav",
            _ => ".mp4"
        };
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
