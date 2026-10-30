using System;
using System.Collections.Generic;
using System.Linq;

namespace Chomp;

/// <summary>What Chomp is doing right now.</summary>
public enum Mood { Idle, PeekLeft, Peek, PeekRight, Closed, Chomp, Working, Happy }

/// <summary>Pixel-art treasure-chest mimic, 32×33 grid. A port of Mimic.swift, frame for frame.</summary>
public static class Mimic
{
    public enum Eyes { None, Glow, Happy }

    /// <summary>A loose pixel: crumbs and sparkles. Colour 0 means the skin's gold.</summary>
    public readonly record struct Bit(int X, int Y, int W, int H, uint Rgb);

    public sealed record Frame
    {
        public int Open { get; init; }
        public Eyes Eyes { get; init; }
        public int Look { get; init; }
        public bool Tongue { get; init; }
        public int Dx { get; init; }
        public Bit[] Bits { get; init; } = Array.Empty<Bit>();
        /// <summary>A snack being eaten: its top-left y, and whether it has gone into the mouth (drawn under the lid).</summary>
        public (int Index, int Y, bool Inside)? Food { get; init; }
    }

    const uint Gold = 0;

    // MARK: Animations

    public const double Tick = 0.11;

    static readonly Frame[] Idle = Enumerable.Range(0, 18).Select(i => new Frame
    {
        Open = new[] { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0 }[i],
        Eyes = Eyes.Glow,
        Dx = new[] { 0, 0, 0, 1, 1, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }[i],
    }).ToArray();

    /// <summary>Seconds per frame of the bite, and how long the whole bite takes.</summary>
    public const double ChompTick = 0.08;
    public static double ChompLength => (ChompSteps.Length + 3) * ChompTick;

    /// <summary>Mouth opening and snack height for each step: Chomp opens up, the snack drops in, the lid snaps shut.</summary>
    static readonly (int Open, int Y, bool Inside, bool Tongue)[] ChompSteps =
    {
        (0, -2, false, false), (2, 0, false, false), (5, 2, false, false), (8, 4, false, true),
        (8, 7, false, true), (8, 11, true, false), (5, 12, true, false), (2, 13, true, false),
    };

    public static List<Frame> ChompFrames(int food)
    {
        var k = Food.Colors.TryGetValue(Food.All[food % Food.All.Length].Crumb, out var crumb) ? crumb : 0xFFFFFFu;
        var frames = ChompSteps.Select(s => new Frame
        {
            Open = s.Open, Eyes = s.Open > 0 ? Eyes.Glow : Eyes.None, Tongue = s.Tongue, Food = (food, s.Y, s.Inside),
        }).ToList();
        // Crumbs in the snack's colour burst out both sides as the lid slams.
        Bit[] Crumbs(params (int X, int Y, int S)[] spots) => spots.Select(p => new Bit(p.X, p.Y, p.S, p.S, k)).ToArray();
        frames.Add(new Frame { Dx = 1, Bits = [.. Crumbs((2, 17, 2), (28, 16, 2), (5, 14, 1), (26, 13, 1)), new Bit(3, 12, 1, 1, Gold)] });
        frames.Add(new Frame { Dx = -1, Bits = [.. Crumbs((1, 13, 2), (29, 12, 2), (3, 10, 1), (27, 9, 1)), new Bit(30, 8, 1, 1, Gold)] });
        frames.Add(new Frame { Bits = Crumbs((0, 9, 2), (30, 8, 2), (2, 6, 1), (29, 5, 1)) });
        return frames;
    }

    static readonly Frame[] Working = new[] { 0, 2, 4, 4, 2, 0, 0, 0 }
        .Select(o => new Frame { Open = o, Eyes = Eyes.Glow, Dx = o == 0 ? 0 : (o == 4 ? 1 : -1) }).ToArray();

    static readonly Frame[] Happy = Enumerable.Range(0, 8).Select(i => new Frame
    {
        Open = i == 4 || i == 5 ? 6 : 5,
        Eyes = Eyes.Happy,
        Tongue = true,
        Bits = i % 4 < 2
            ? new[] { new Bit(2, 6, 1, 1, Gold), new Bit(1, 7, 3, 1, Gold), new Bit(2, 8, 1, 1, Gold), new Bit(28, 2, 1, 1, Gold) }
            : new[] { new Bit(29, 5, 1, 1, Gold), new Bit(28, 6, 3, 1, Gold), new Bit(29, 7, 1, 1, Gold), new Bit(3, 3, 1, 1, Gold) },
    }).ToArray();

    /// <summary>Lid cracked with eyes showing: used for skin thumbnails.</summary>
    public static readonly Frame Portrait = new() { Open = 2, Eyes = Eyes.Glow };

    /// <summary>t is seconds on a steady clock; since is when the bite started.</summary>
    public static Frame At(Mood mood, double t, double since, int food)
    {
        var n = (int)(t / Tick);
        switch (mood)
        {
            case Mood.Idle: return Idle[n % Idle.Length];
            case Mood.Closed: return new Frame();
            case Mood.PeekLeft: case Mood.Peek: case Mood.PeekRight:
                return new Frame { Open = n % 30 == 0 ? 1 : 2, Eyes = Eyes.Glow, Look = mood - Mood.Peek };
            case Mood.Chomp:
            {
                var frames = ChompFrames(food);
                var i = Math.Max(0, (int)((t - since) / ChompTick));
                return i < frames.Count ? frames[i] : new Frame();
            }
            case Mood.Working: return Working[n % Working.Length];
            default: return Happy[n % Happy.Length];
        }
    }

    // MARK: Drawing

    public static PixelCanvas Draw(Frame f, Skin k)
    {
        var c = new PixelCanvas(32, 33);
        void Px(int x, int y, int w, int h, uint col) => c.Fill(x, y, w, h, col);

        var open = Math.Clamp(f.Open, 0, 9);
        int bx = 4 + f.Dx, bw = 24, by = 19, bh = 11;

        c.Fill(6, 30, 20, 2, 0x000000, 89);   // shadow

        // Body
        Px(bx, by, bw, bh, Pal.Ink);
        Px(bx + 1, by + 1, bw - 2, bh - 2, k.Wood);
        for (var x = bx + 5; x < bx + bw - 2; x += 5) Px(x, by + 1, 1, bh - 2, k.WoodDark);
        Px(bx + 1, by + bh - 2, bw - 2, 1, k.WoodShadow);
        Px(bx + 1, by + 1, 2, bh - 2, k.GoldDark); Px(bx + bw - 3, by + 1, 2, bh - 2, k.GoldDark);
        Px(bx + 1, by + 1, 2, 1, k.Gold); Px(bx + bw - 3, by + 1, 2, 1, k.Gold);

        void Snack((int Index, int Y, bool Inside) sn)
        {
            var sprite = Food.All[sn.Index % Food.All.Length];
            for (var row = 0; row < sprite.Rows.Length; row++)
            {
                var y = sn.Y + row;
                // Once inside, only what's in the open mouth shows; the lid hides the rest.
                if (sn.Inside && (y < by - open || y >= by)) continue;
                for (var col = 0; col < sprite.Rows[row].Length; col++)
                    if (Food.Colors.TryGetValue(sprite.Rows[row][col], out var fc)) Px(bx + 8 + col, y, 1, 1, fc);
            }
        }

        // Mouth
        if (open > 0)
        {
            Px(bx, by - open, bw, open, Pal.Ink);
            Px(bx + 1, by - open, bw - 2, open, Pal.Mouth);
        }
        if (f.Food is { } fin && fin.Inside) Snack(fin);
        if (open >= 3)
        {
            for (var x = bx + 3; x < bx + bw - 4; x += 3)
            {
                Px(x, by - 1, 2, 1, Pal.Tooth);
                if (open >= 6) Px(x, by - 2, 1, 1, Pal.Tooth);
            }
        }

        // Lid
        var ly = by - open - 9;
        Px(bx + 1, ly, bw - 2, 1, Pal.Ink);
        Px(bx, ly + 1, bw, 8, Pal.Ink);
        Px(bx + 1, ly + 1, bw - 2, 7, k.WoodLight);
        Px(bx + 2, ly + 1, bw - 4, 1, k.WoodHighlight);
        for (var x = bx + 5; x < bx + bw - 2; x += 5) Px(x, ly + 2, 1, 4, k.Wood);
        Px(bx + 1, ly + 6, bw - 2, 2, k.GoldDark); Px(bx + 1, ly + 6, bw - 2, 1, k.Gold);
        Px(bx + 1, ly + 1, 2, 5, k.GoldDark); Px(bx + bw - 3, ly + 1, 2, 5, k.GoldDark);
        Px(bx + 1, ly + 1, 2, 1, k.Gold); Px(bx + bw - 3, ly + 1, 2, 1, k.Gold);
        if (open >= 3)
        {
            for (var x = bx + 2; x < bx + bw - 3; x += 3)
            {
                Px(x, ly + 9, 2, 1, Pal.Tooth);
                if (open >= 6) Px(x + 1, ly + 10, 1, 1, Pal.Tooth);
            }
        }

        // Lock
        Px(bx + 10, by, 4, 4, Pal.Ink);
        Px(bx + 10, by, 4, 3, k.Gold);
        Px(bx + 11, by + 1, 2, 1, k.GoldDark);
        Px(bx + 11, by + 2, 2, 1, Pal.Ink);

        // Eyes
        if (f.Eyes != Eyes.None && open >= 1)
        {
            var h = open >= 3 ? 2 : 1;
            var ey = open >= 3 ? by - open + Math.Max(1, open / 2 - 1) : by - 1;
            if (f.Eyes == Eyes.Happy && open >= 3)
            {
                foreach (var (ex, ey2) in new[] { (6, 1), (7, 0), (8, 1), (16, 1), (17, 0), (18, 1) }) Px(bx + ex, ey + ey2, 1, 1, k.Eye);
            }
            else
            {
                Px(bx + 6 + f.Look, ey, 2, h, k.Eye);
                Px(bx + 16 + f.Look, ey, 2, h, k.Eye);
            }
        }

        // Tongue
        if (f.Tongue && open >= 4)
        {
            Px(bx + 15, by - 1, 4, 7, Pal.Ink);
            Px(bx + 16, by - 1, 2, 6, Pal.Tongue);
            Px(bx + 16, by + 4, 2, 1, Pal.TongueDark);
        }

        if (f.Food is { } fout && !fout.Inside) Snack(fout);

        foreach (var b in f.Bits) Px(b.X, b.Y, b.W, b.H, b.Rgb == Gold ? k.Gold : b.Rgb);
        return c;
    }
}
