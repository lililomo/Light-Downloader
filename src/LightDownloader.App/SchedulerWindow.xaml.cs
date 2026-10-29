using System.Windows;
using LightDownloader.Core.Models;

namespace LightDownloader.App;

public partial class SchedulerWindow : Window
{
    public AppSettings Result { get; private set; }

    public SchedulerWindow(AppSettings s)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        Result = s;
        EnabledBox.IsChecked = s.SchedulerEnabled;
        StartBox.Text = s.StartTime ?? "";
        StopBox.Text = s.StopTime ?? "";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!TryParse(StartBox.Text, out var start) || !TryParse(StopBox.Text, out var stop))
        {
            ErrorText.Text = "Times must look like 01:00 or 23:30.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        var s = Result.Clone();
        s.SchedulerEnabled = EnabledBox.IsChecked == true;
        s.StartTime = start;
        s.StopTime = stop;
        Result = s;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Empty input is valid (action skipped); otherwise it must be a time of day.</summary>
    private static bool TryParse(string? text, out string? value)
    {
        value = null;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return true;
        if (TimeSpan.TryParse(text, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
        {
            value = t.ToString(@"hh\:mm");
            return true;
        }
        return false;
    }
}
