using System.Windows;
using System.Windows.Media;
using LightDownloader.Core.Models;

namespace LightDownloader.App;

/// <summary>IDM-style map of a download: one block per connection, filled as that connection receives data.</summary>
public sealed class SegmentBar : FrameworkElement
{
    private static readonly Brush Track = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Brush Fill = Frozen(Color.FromRgb(0x2E, 0x7B, 0xD6));
    private static readonly Brush Divider = Frozen(Color.FromRgb(0xA0, 0xA0, 0xA0));

    private (double Start, double Length, double Done)[] _parts = Array.Empty<(double, double, double)>();

    public void Update(IReadOnlyList<SegmentState> segments, long total)
    {
        if (total <= 0 || segments.Count == 0)
        {
            _parts = Array.Empty<(double, double, double)>();
        }
        else
        {
            var parts = new (double, double, double)[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                double len = Math.Max(0, s.Length);
                parts[i] = (s.Start / (double)total, len / total, len > 0 ? Math.Clamp(s.Downloaded / len, 0, 1) : 0);
            }
            _parts = parts;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRectangle(Track, null, new Rect(0, 0, w, h));
        for (int i = 0; i < _parts.Length; i++)
        {
            var (start, length, done) = _parts[i];
            double x = start * w;
            if (done > 0) dc.DrawRectangle(Fill, null, new Rect(x, 0, length * w * done, h));
            if (i > 0) dc.DrawRectangle(Divider, null, new Rect(Math.Round(x), 0, 1, h)); // block divider
        }
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
