using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace Chomp;

/// <summary>Paints the wheel disc pixel by pixel on a 220×220 grid, shown at 2×. A port of WheelArt in PixelArt.swift.
/// Each pixel works out which slice it belongs to from its angle, so arcs come out stepped.</summary>
public static class WheelArt
{
    public const int Grid = 220;
    public const int Scale = 2;
    const double Center = 110, Outer = 102, Inner = 40, DiscR = 108;
    public const double OuterR = Outer, InnerR = Inner;

    /// <summary>After a drop the slices give way to a plain ring: a spinner while working, then a solid rim.
    /// Spin ≥ 0 is the spinner's step; otherwise Full is the rim colour.</summary>
    public readonly record struct Ring(int Spin, uint Full)
    {
        public static Ring Spinning(int step) => new(step, 0);
        public static Ring Solid(uint rgb) => new(-1, rgb);
    }

    public const int SpinSteps = 24;

    static readonly Dictionary<string, BitmapSource> cache = new();

    static uint? Spinner(Ring ring, double dx, double dy, double tail, double head, uint accent, uint accentDark)
    {
        if (ring.Spin < 0) return ring.Full;
        var a = Math.Atan2(dx, -dy);
        if (a < 0) a += 2 * Math.PI;
        var at = 2 * Math.PI * ring.Spin / SpinSteps;
        var behind = (at - a + 4 * Math.PI) % (2 * Math.PI);
        return behind < tail ? (behind < head ? accent : accentDark) : null;
    }

    public static BitmapSource Image(int n, int? hover, Skin skin, Ring? ring = null)
    {
        var key = $"{n}-{hover ?? -9}-{skin.Id}-{ring}";
        if (cache.TryGetValue(key, out var hit)) return hit;
        if (cache.Count > 64) cache.Clear();

        uint accent = skin.Accent, accentDark = skin.AccentDark;
        var c = new PixelCanvas(Grid, Grid);
        var w = n > 0 ? 2 * Math.PI / n : 0;

        for (var y = 0; y < Grid; y++)
        for (var x = 0; x < Grid; x++)
        {
            double dx = x + 0.5 - Center, dy = y + 0.5 - Center;
            var r = Math.Sqrt(dx * dx + dy * dy);
            uint? col = null;
            if (r <= DiscR) col = r > DiscR - 2 ? Pal.Ink : Pal.Disc;
            if (r < Inner) col = r > Inner - 2 ? Pal.Ink : (hover == -1 ? Pal.Mouth : Pal.Hole);
            if (ring is { } rg && r >= Inner + 5 && r <= Outer - 5)
            {
                // Band for the text, with a 4-pixel rim on its outer edge.
                col = r > Outer - 6 ? Pal.SegmentRim : Pal.Segment;
                if (r > Outer - 10) col = Spinner(rg, dx, dy, Math.PI / 3, Math.PI / 9, accent, accentDark) ?? col;
            }
            else if (n > 0 && r >= Inner + 5)
            {
                var a = Math.Atan2(dx, -dy);
                if (a < 0) a += 2 * Math.PI;
                var t = (a + w / 2) % (2 * Math.PI);
                var i = (int)(t / w) % n;
                var off = Math.Min(t % w, w - t % w);
                var hot = hover == i;
                var o = hot ? Outer : Outer - 5;
                if (r <= o && off * r > 2)
                {
                    if (hot) col = (r > o - 1 || r < Inner + 6 || off * r < 3) ? accentDark : accent;
                    else col = r > o - 1 ? Pal.SegmentRim : Pal.Segment;
                }
            }
            if (col is uint v) c.Set(x, y, v);
        }
        return cache[key] = c.ToBitmap();
    }

    // MARK: Compact disc (after a drop)

    /// <summary>The small disc shown while a job runs and when it's done: a big hole for Chomp and a thin ring.</summary>
    public const int CompactGrid = 130;
    const double CompactHole = 41, CompactBand = 58, CompactDisc = 64;

    public static BitmapSource Compact(Ring ring, Skin skin)
    {
        var key = $"compact-{skin.Id}-{ring}";
        if (cache.TryGetValue(key, out var hit)) return hit;
        var g = CompactGrid;
        var c0 = g / 2.0;
        var c = new PixelCanvas(g, g);
        for (var y = 0; y < g; y++)
        for (var x = 0; x < g; x++)
        {
            double dx = x + 0.5 - c0, dy = y + 0.5 - c0;
            var r = Math.Sqrt(dx * dx + dy * dy);
            uint? col = null;
            if (r <= CompactDisc) col = r > CompactDisc - 2 ? Pal.Ink : Pal.Disc;
            if (r > CompactHole + 2 && r <= CompactBand)
                col = Spinner(ring, dx, dy, Math.PI / 2, Math.PI / 6, skin.Accent, skin.AccentDark) ?? Pal.Segment;
            if (r <= CompactHole + 2) col = r > CompactHole ? Pal.Ink : Pal.Hole;
            if (col is uint v) c.Set(x, y, v);
        }
        return cache[key] = c.ToBitmap();
    }
}
