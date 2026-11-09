using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace LightDownloader.App;

/// <summary>A message box with custom button captions. Returns the clicked button's index, or the cancel index if closed.</summary>
public partial class DialogWindow : Window
{
    private int _result = -1;

    private DialogWindow() => InitializeComponent();

    public static int Show(Window? owner, string title, string message, MessageBoxImage icon,
        string[] buttons, int defaultButton = 0, int cancelButton = -1)
    {
        var w = new DialogWindow { Title = title };
        if (owner is { IsVisible: true }) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        w.MessageText.Text = message;
        var sys = icon switch
        {
            MessageBoxImage.Warning => Drawing.SystemIcons.Warning,
            MessageBoxImage.Error => Drawing.SystemIcons.Error,
            MessageBoxImage.Question => Drawing.SystemIcons.Question,
            _ => Drawing.SystemIcons.Information
        };
        w.IconImage.Source = Imaging.CreateBitmapSourceFromHIcon(sys.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

        w._result = cancelButton;
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var b = new Button
            {
                Content = buttons[i],
                Style = (Style)w.FindResource("DlgButton"),
                IsDefault = i == defaultButton,
                IsCancel = i == cancelButton
            };
            b.Click += (_, _) => { w._result = index; w.Close(); };
            w.ButtonsPanel.Children.Add(b);
        }
        w.Loaded += (_, _) => (w.ButtonsPanel.Children[Math.Clamp(defaultButton, 0, buttons.Length - 1)] as Button)?.Focus();

        System.Media.SystemSounds.Question.Play();
        w.ShowDialog();
        return w._result;
    }
}
