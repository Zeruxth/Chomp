using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using static Chomp.Loc;

namespace Chomp;

/// <summary>Colours every skin shares, so text and outlines always read the same. Same values as PixelArt.swift.</summary>
public static class Pal
{
    public const uint Ink = 0x140A08;
    public const uint Mouth = 0x24101C;
    public const uint Tooth = 0xF6F1E6;
    public const uint Tongue = 0xEA4F86;
    public const uint TongueDark = 0xA3285A;
    public const uint Disc = 0x1D1420;
    public const uint Segment = 0x2F2231;
    public const uint SegmentRim = 0x3D2D3F;
    public const uint Hole = 0x120C14;
    public const uint Panel = 0x241A26;
    public const uint Muted = 0xB3A6B8;

    public static Color C(uint rgb, byte a = 255) => Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    static readonly Dictionary<(uint, byte), SolidColorBrush> brushes = new();

    public static SolidColorBrush B(uint rgb, byte a = 255)
    {
        if (brushes.TryGetValue((rgb, a), out var b)) return b;
        b = new SolidColorBrush(C(rgb, a));
        b.Freeze();
        return brushes[(rgb, a)] = b;
    }
}

/// <summary>A chest skin recolours the wood, trim and eyes of Chomp and the accent everywhere.</summary>
public sealed class Skin
{
    public string Id { get; }
    public string Title => char.ToUpperInvariant(Id[0]) + Id[1..];
    public string Note { get; }
    readonly uint[] hex;

    Skin(string id, string note, params uint[] hex) { Id = id; Note = L(note, RoyalAt); this.hex = hex; }

    public const int RoyalAt = 100;

    // woodDark, wood, woodLight, woodHighlight, woodShadow, gold, goldDark, eye, accent, accentDark
    public static readonly Skin[] All =
    {
        new("oak", "Default", 0x5E3820, 0x7F512D, 0xA06A3A, 0xC48A50, 0x43281A, 0xFFD45C, 0xC48D2A, 0x8FF6FF, 0xFFD45C, 0xC48D2A),
        new("frost", "Icy blue", 0x2B4A6B, 0x3E6A94, 0x5A8FBF, 0x8FC0E8, 0x1F334D, 0xE8F6FF, 0x9CC4DE, 0xFFD45C, 0x8FD8FF, 0x4F9CC8),
        new("slime", "Swamp green", 0x2B5A2A, 0x3F7A3A, 0x5FA34F, 0x8CCB6A, 0x1E3F1E, 0xD9F27A, 0x9BB83A, 0xFF7AD9, 0xB6F05A, 0x7FB02E),
        new("void", "Dungeon purple", 0x352050, 0x4B2F6B, 0x6A4A94, 0x9273C0, 0x24163A, 0xC9A6FF, 0x8A6AC8, 0x8FF6FF, 0xB894FF, 0x7E5CC4),
        new("bone", "Greyscale", 0x4A4A50, 0x6B6B70, 0x8E8E95, 0xB8B8C0, 0x333338, 0xF2F2F2, 0xA8A8B0, 0xFF5A5A, 0xEDEDED, 0xA8A8B0),
        new("royal", "{0} chomps", 0x8F6118, 0xC48D2A, 0xE0AE45, 0xFFD45C, 0x6B4510, 0xFFF3B0, 0xE8C25A, 0xEA4F86, 0xFFE27A, 0xC9A23A),
    };

    public uint WoodDark => hex[0];
    public uint Wood => hex[1];
    public uint WoodLight => hex[2];
    public uint WoodHighlight => hex[3];
    public uint WoodShadow => hex[4];
    public uint Gold => hex[5];
    public uint GoldDark => hex[6];
    public uint Eye => hex[7];
    public uint Accent => hex[8];
    public uint AccentDark => hex[9];

    public bool Locked => Id == "royal" && Prefs.Current.Chomps < RoyalAt;

    public static Skin Current
    {
        get
        {
            foreach (var s in All) if (s.Id == Prefs.Current.Skin && !s.Locked) return s;
            return All[0];
        }
    }
}

/// <summary>A little RGBA canvas for pixel art, shown scaled up with nearest-neighbour so pixels stay square.</summary>
public sealed class PixelCanvas(int w, int h)
{
    public int W { get; } = w;
    public int H { get; } = h;
    readonly byte[] px = new byte[w * h * 4];

    public void Fill(int x, int y, int w, int h, uint rgb, byte a = 255)
    {
        for (var yy = System.Math.Max(0, y); yy < System.Math.Min(H, y + h); yy++)
            for (var xx = System.Math.Max(0, x); xx < System.Math.Min(W, x + w); xx++)
                Set(xx, yy, rgb, a);
    }

    public void Set(int x, int y, uint rgb, byte a = 255)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        var k = (y * W + x) * 4;
        px[k] = (byte)rgb; px[k + 1] = (byte)(rgb >> 8); px[k + 2] = (byte)(rgb >> 16); px[k + 3] = a;
    }

    public BitmapSource ToBitmap()
    {
        var b = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, px, W * 4);
        b.Freeze();
        return b;
    }
}
