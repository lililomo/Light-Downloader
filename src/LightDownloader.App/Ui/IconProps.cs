using System.Windows;
using System.Windows.Media;

namespace LightDownloader.App;

/// <summary>Attached glyph (a Segoe Fluent Icons code point) and its colour, read by button/menu templates.</summary>
public static class IconProps
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(string), typeof(IconProps), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.RegisterAttached(
        "GlyphBrush", typeof(Brush), typeof(IconProps), new FrameworkPropertyMetadata(DefaultBrush()));

    public static string GetGlyph(DependencyObject o) => (string)o.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject o, string value) => o.SetValue(GlyphProperty, value);

    public static Brush GetGlyphBrush(DependencyObject o) => (Brush)o.GetValue(GlyphBrushProperty);
    public static void SetGlyphBrush(DependencyObject o, Brush value) => o.SetValue(GlyphBrushProperty, value);

    private static Brush DefaultBrush()
    {
        var b = new SolidColorBrush(Color.FromRgb(0xA0, 0xA8, 0xB6));
        b.Freeze();
        return b;
    }
}
