using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LightDownloader.App;

public enum ButtonKind { Normal, Primary, Danger }

public readonly record struct DialogButton(string Text, ButtonKind Kind = ButtonKind.Normal);

/// <summary>Themed replacement for MessageBox. Returns the index of the clicked button, or -1 if closed.</summary>
public partial class DialogWindow : Window
{
    private int _result = -1;

    private DialogWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);
    }

    public static int Show(Window? owner, string title, string message, string glyph, Brush glyphBrush,
        params DialogButton[] buttons)
    {
        var w = new DialogWindow { Title = title };
        if (owner is { IsVisible: true }) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        w.TitleText.Text = title;
        w.MessageText.Text = message;
        w.GlyphText.Text = glyph;
        w.GlyphText.Foreground = glyphBrush;
        if (glyphBrush is SolidColorBrush sb)
            w.GlyphBadge.Background = new SolidColorBrush(Color.FromArgb(0x2A, sb.Color.R, sb.Color.G, sb.Color.B));

        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var b = new Button
            {
                Content = buttons[i].Text,
                MinWidth = 96,
                Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0),
                IsCancel = i == 0,
                IsDefault = i == buttons.Length - 1
            };
            if (buttons[i].Kind == ButtonKind.Primary) b.Style = (Style)w.FindResource("PrimaryButton");
            else if (buttons[i].Kind == ButtonKind.Danger) b.Style = (Style)w.FindResource("DangerButton");
            b.Click += (_, _) => { w._result = index; w.Close(); };
            w.ButtonsPanel.Children.Add(b);
        }

        w.ShowDialog();
        return w._result;
    }
}
