using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace IconGen;

/// <summary>
/// Draws the Light Downloader brand mark (gradient squircle, download arrow into a tray,
/// and a "light" sparkle) as vector art, and exports every icon and store asset from it.
/// </summary>
internal static class Program
{
    private static readonly Color Indigo = Rgb("#5B5CF6");
    private static readonly Color Blue = Rgb("#3B82F6");
    private static readonly Color Cyan = Rgb("#06C4E2");

    [STAThread]
    private static int Main(string[] args)
    {
        var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        string P(params string[] parts) => Path.Combine(new[] { root }.Concat(parts).ToArray());

        // Windows app icon (.ico): small sizes as 32-bit DIBs, large sizes as PNG frames.
        var icoSizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var frames = icoSizes.Select(s => (s, s <= 48 ? Dib(RenderIcon(s)) : Png(RenderIcon(s)))).ToList();
        WriteIco(P("src", "LightDownloader.App", "Assets", "app.ico"), frames);
        Save(RenderIcon(128), P("src", "LightDownloader.App", "Assets", "logo-128.png"));

        // Browser extension icons.
        foreach (var browser in new[] { "chrome", "firefox" })
            foreach (var s in new[] { 16, 32, 48, 96, 128 })
                Save(RenderIcon(s), P("extension", browser, "icons", $"icon{s}.png"));

        // Store submission assets.
        foreach (var s in new[] { 16, 32, 48, 64, 96, 128, 256, 512 })
            Save(RenderIcon(s), P("store", "icons", $"icon-{s}.png"));
        Save(RenderIcon(128, padded: true), P("store", "icons", "chrome-store-icon-128.png"));
        Save(RenderPromo(440, 280), P("store", "icons", "promo-small-440x280.png"));
        Save(RenderPromo(1400, 560), P("store", "icons", "promo-marquee-1400x560.png"));

        Console.WriteLine("Icon assets written under " + root);
        return 0;
    }

    // ---- drawing -------------------------------------------------------------

    /// <summary>Builds the mark in a 256-unit design space scaled to <paramref name="size"/> px.</summary>
    private static Visual BuildIcon(double size, bool padded)
    {
        // Chrome's store icon wants the artwork at 96/128 with transparent padding.
        double artPx = padded ? size * 0.75 : size;
        double offset = padded ? size * 0.125 : 0;
        double k = artPx / 256.0;

        var root = new ContainerVisual();

        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(offset, offset));
            dc.PushTransform(new ScaleTransform(k, k));
            var rect = new Rect(12, 12, 232, 232);
            var fill = new LinearGradientBrush(
                new GradientStopCollection { new(Indigo, 0), new(Blue, 0.48), new(Cyan, 1) },
                new Point(0, 0), new Point(1, 1));
            dc.DrawRoundedRectangle(fill, null, rect, 58, 58);
            var gloss = new LinearGradientBrush(
                new GradientStopCollection { new(Color.FromArgb(72, 255, 255, 255), 0), new(Color.FromArgb(0, 255, 255, 255), 0.55) },
                new Point(0, 0), new Point(0, 1));
            dc.DrawRoundedRectangle(gloss, null, rect, 58, 58);
            if (artPx >= 64)
                dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)), 2),
                    new Rect(13, 13, 230, 230), 57, 57);
            dc.Pop(); dc.Pop();
        }
        root.Children.Add(bg);

        var glyph = new DrawingVisual();
        using (var dc = glyph.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(offset, offset));
            dc.PushTransform(new ScaleTransform(k, k));

            // Thicker strokes at small sizes so the mark stays legible in the taskbar/toolbar.
            double t = artPx <= 20 ? 32 : artPx <= 32 ? 28 : 24;
            var pen = new Pen(Brushes.White, t)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            dc.DrawLine(pen, new Point(128, 50), new Point(128, 144));
            dc.DrawGeometry(null, pen, Geometry.Parse("M88,106 L128,146 L168,106"));
            dc.DrawGeometry(null, pen, Geometry.Parse("M62,154 L62,176 Q62,198 84,198 L172,198 Q194,198 194,176 L194,154"));

            if (artPx >= 48)
            {
                var spark = new SolidColorBrush(Rgb("#FFF6D5"));
                dc.DrawGeometry(spark, null, Sparkle(new Point(192, 60), 26, 6.5));
                dc.DrawEllipse(spark, null, new Point(215, 101), 5.5, 5.5);
            }
            dc.Pop(); dc.Pop();
        }
        if (artPx >= 48)
            glyph.Effect = new DropShadowEffect
            {
                Color = Rgb("#0B1650"),
                BlurRadius = Math.Max(2, artPx * 0.05),
                ShadowDepth = Math.Max(1, artPx * 0.016),
                Direction = 270,
                Opacity = 0.38
            };
        root.Children.Add(glyph);
        return root;
    }

    /// <summary>Four-point star with concave sides.</summary>
    private static Geometry Sparkle(Point c, double outer, double inner)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (int i = 0; i < 4; i++)
            {
                double a = Math.PI / 2 * i - Math.PI / 2;
                var tip = new Point(c.X + outer * Math.Cos(a), c.Y + outer * Math.Sin(a));
                double an = a + Math.PI / 2;
                var next = new Point(c.X + outer * Math.Cos(an), c.Y + outer * Math.Sin(an));
                var ctrl = new Point(c.X + inner * Math.Cos(a + Math.PI / 4), c.Y + inner * Math.Sin(a + Math.PI / 4));
                if (i == 0) ctx.BeginFigure(tip, true, true);
                ctx.QuadraticBezierTo(ctrl, next, true, true);
            }
        }
        g.Freeze();
        return g;
    }

    private static BitmapSource RenderIcon(int size, bool padded = false)
        => Render(BuildIcon(size, padded), size, size);

    private static BitmapSource RenderPromo(int w, int h)
    {
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            var bg = new LinearGradientBrush(
                new GradientStopCollection { new(Rgb("#0B1020"), 0), new(Rgb("#1A2160"), 0.55), new(Rgb("#0B4A5E"), 1) },
                new Point(0, 0), new Point(1, 1));
            dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));

            // Soft brand glows.
            var glow = new RadialGradientBrush(Color.FromArgb(90, 59, 130, 246), Color.FromArgb(0, 59, 130, 246));
            dc.DrawEllipse(glow, null, new Point(w * 0.18, h * 0.5), h * 0.75, h * 0.75);
            var glow2 = new RadialGradientBrush(Color.FromArgb(70, 6, 196, 226), Color.FromArgb(0, 6, 196, 226));
            dc.DrawEllipse(glow2, null, new Point(w * 0.92, h * 0.1), h * 0.6, h * 0.6);

            double u = h / 280.0; // layout unit
            int iconPx = (int)(108 * u);
            double ix = 34 * u, iy = (h - iconPx) / 2.0;
            dc.DrawImage(RenderIcon(iconPx), new Rect(ix, iy, iconPx, iconPx));

            double tx = ix + iconPx + 22 * u;
            double maxW = w - tx - 26 * u;
            DrawText(dc, "Light Downloader", tx, h / 2.0 - 50 * u, 30 * u, maxW, FontWeights.Bold, Brushes.White);
            DrawText(dc, "Fast, multi-connection downloads", tx + 1 * u, h / 2.0 - 6 * u, 15 * u, maxW,
                FontWeights.Normal, new SolidColorBrush(Rgb("#CBD5E1")));
            DrawText(dc, "Chrome & Firefox integration  ·  Free", tx + 1 * u, h / 2.0 + 20 * u, 13 * u, maxW,
                FontWeights.SemiBold, new SolidColorBrush(Rgb("#67E8F9")));
        }
        return Render(v, w, h);
    }

    /// <summary>Draws a single line, shrinking the font until it fits <paramref name="maxWidth"/>.</summary>
    private static void DrawText(DrawingContext dc, string text, double x, double y, double px, double maxWidth,
        FontWeight weight, Brush brush)
    {
        var tf = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal);
        FormattedText ft;
        do
        {
            ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, px, brush, 1.0);
            px *= 0.96;
        } while (ft.Width > maxWidth && px > 6);
        dc.DrawText(ft, new Point(x, y));
    }

    private static BitmapSource Render(Visual v, int w, int h)
    {
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(v);
        rtb.Freeze();
        return rtb;
    }

    // ---- encoding ------------------------------------------------------------

    private static void Save(BitmapSource bmp, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Png(bmp));
    }

    private static byte[] Png(BitmapSource bmp)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>32-bit BGRA DIB icon frame (bottom-up XOR bitmap + 1-bit AND mask).</summary>
    private static byte[] Dib(BitmapSource bmp)
    {
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        var straight = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        var px = new byte[w * h * 4];
        straight.CopyPixels(px, w * 4, 0);

        int maskStride = (w + 31) / 32 * 4;
        var mask = new byte[maskStride * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[(y * w + x) * 4 + 3] == 0)
                    mask[(h - 1 - y) * maskStride + x / 8] |= (byte)(0x80 >> (x % 8));

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(40); bw.Write(w); bw.Write(h * 2);
        bw.Write((short)1); bw.Write((short)32);
        bw.Write(0); bw.Write(w * h * 4 + mask.Length);
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
        for (int y = h - 1; y >= 0; y--) bw.Write(px, y * w * 4, w * 4);
        bw.Write(mask);
        bw.Flush();
        return ms.ToArray();
    }

    private static void WriteIco(string path, List<(int size, byte[] data)> frames)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((short)0); bw.Write((short)1); bw.Write((short)frames.Count);
        int offset = 6 + 16 * frames.Count;
        foreach (var (s, data) in frames)
        {
            byte dim = (byte)(s >= 256 ? 0 : s);
            bw.Write(dim); bw.Write(dim); bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((short)1); bw.Write((short)32);
            bw.Write(data.Length); bw.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in frames) bw.Write(data);
    }

    private static Color Rgb(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
