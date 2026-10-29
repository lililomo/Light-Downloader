using System.Windows;
using System.Windows.Media;
using LightDownloader.Core.Models;

namespace LightDownloader.App;

/// <summary>IDM-style map of a download: one block per connection, filled as that connection receives data.</summary>
public sealed class SegmentBar : FrameworkElement
{
    private static readonly Brush Track = Frozen(Color.FromRgb(0x2A, 0x2F, 0x39));
    private static readonly Brush Gap = Frozen(Color.FromRgb(0x14, 0x16, 0x1B));

    private (double Start, double Length, double Done)[] _parts = Array.Empty<(double, double, double)>();
    private Brush _fill = Frozen(Color.FromRgb(0x4F, 0x7D, 0xFF));

    public void Update(IReadOnlyList<SegmentState> segments, long total, Brush fill)
    {
        _fill = fill;
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

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), 4, 4));
        dc.DrawRectangle(Track, null, new Rect(0, 0, w, h));
        for (int i = 0; i < _parts.Length; i++)
        {
            var (start, length, done) = _parts[i];
            double x = start * w;
            if (done > 0) dc.DrawRectangle(_fill, null, new Rect(x, 0, length * w * done, h));
            if (i > 0) dc.DrawRectangle(Gap, null, new Rect(x - 1, 0, 2, h)); // block divider
        }
        dc.Pop();
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
