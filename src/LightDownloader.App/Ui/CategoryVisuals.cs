using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LightDownloader.Core.Util;

namespace LightDownloader.App;

/// <summary>Glyph and colour for each file category (sidebar, list rows, dialogs).</summary>
public static class CategoryVisuals
{
    private static readonly Dictionary<string, (string Glyph, Color Color)> Map = new()
    {
        [Categories.Compressed] = ("", Color.FromRgb(0xF5, 0xA5, 0x24)),
        [Categories.Documents] = ("", Color.FromRgb(0x5B, 0x9B, 0xFF)),
        [Categories.Music] = ("", Color.FromRgb(0xF4, 0x72, 0xB6)),
        [Categories.Video] = ("", Color.FromRgb(0xA7, 0x8B, 0xFA)),
        [Categories.Programs] = ("", Color.FromRgb(0x34, 0xD3, 0x99)),
        [Categories.Pictures] = ("", Color.FromRgb(0x2D, 0xD4, 0xBF)),
        [Categories.Other] = ("", Color.FromRgb(0x94, 0xA3, 0xB8)),
    };

    private static readonly Dictionary<string, Brush> SolidCache = new();
    private static readonly Dictionary<string, Brush> TintCache = new();

    public static string GlyphFor(string? category) => Map[Key(category)].Glyph;

    public static Brush BrushFor(string? category)
    {
        var key = Key(category);
        if (!SolidCache.TryGetValue(key, out var b))
            SolidCache[key] = b = Frozen(Map[key].Color);
        return b;
    }

    /// <summary>Translucent background used behind the category glyph.</summary>
    public static Brush TintFor(string? category)
    {
        var key = Key(category);
        if (!TintCache.TryGetValue(key, out var b))
        {
            var c = Map[key].Color;
            TintCache[key] = b = Frozen(Color.FromArgb(0x2A, c.R, c.G, c.B));
        }
        return b;
    }

    private static string Key(string? category)
        => category != null && Map.ContainsKey(category) ? category : Categories.Other;

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>Converts a category name to its glyph, brush or tinted background (see <see cref="Part"/>).</summary>
public sealed class CategoryVisualConverter : IValueConverter
{
    public string Part { get; set; } = "Glyph";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var category = value as string;
        return Part switch
        {
            "Brush" => CategoryVisuals.BrushFor(category),
            "Tint" => CategoryVisuals.TintFor(category),
            _ => CategoryVisuals.GlyphFor(category)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
