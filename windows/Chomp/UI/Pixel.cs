using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Chomp;

/// <summary>Pixel fonts for short names, Segoe UI for everything people need to read closely.</summary>
public static class Fonts
{
    static readonly Uri Base = new("pack://application:,,,/");
    public static readonly FontFamily Pixelify = new(Base, "./Fonts/#Pixelify Sans");
    public static readonly FontFamily Silkscreen = new(Base, "./Fonts/#Silkscreen");
    public static readonly FontFamily Body = new("Segoe UI");
    static readonly FontFamily Rounded = new("Segoe UI Variable Display, Segoe UI");

    /// <summary>Slice names, titles, headers.</summary>
    public static TextBlock Display(string text, double size, uint rgb = Pal.Tooth, bool bold = false)
    {
        var pixel = Prefs.Current.PixelText;
        var t = Text(text, pixel ? size : size * 0.9, rgb, pixel ? Pixelify : Rounded);
        t.FontWeight = bold || !pixel ? FontWeights.SemiBold : FontWeights.Medium;
        // Pixelify joins "fi" into one glyph, so "file" reads as "Ale".
        Typography.SetStandardLigatures(t, false);
        return t;
    }

    /// <summary>Tiny all-caps labels: keycaps, section headers.</summary>
    public static TextBlock Label(string text, double size, uint rgb = Pal.Tooth)
    {
        var pixel = Prefs.Current.PixelText;
        var t = Text(pixel ? text : text.ToUpperInvariant(), size, rgb, pixel ? Silkscreen : Rounded);
        if (!pixel) t.FontWeight = FontWeights.Bold;
        return t;
    }

    /// <summary>File names, sizes, paths, errors.</summary>
    public static TextBlock BodyText(string text, double size, uint rgb = Pal.Muted) => Text(text, size, rgb, Body);

    static TextBlock Text(string text, double size, uint rgb, FontFamily f) => new()
    {
        Text = text,
        FontSize = size,
        FontFamily = f,
        Foreground = Pal.B(rgb),
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextAlignment = TextAlignment.Center,
    };
}

public static class PixelUI
{
    /// <summary>Pixel art scaled up with hard edges.</summary>
    public static Image Art(BitmapSource src, double scale)
    {
        var i = new Image { Source = src, Width = src.PixelWidth * scale, Height = src.PixelHeight * scale, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(i, BitmapScalingMode.NearestNeighbor);
        return i;
    }

    public static void Set(Image i, BitmapSource src, double scale)
    {
        i.Source = src;
        i.Width = src.PixelWidth * scale;
        i.Height = src.PixelHeight * scale;
    }

    /// <summary>A rectangle with its corner pixels knocked out.</summary>
    public static Geometry Notched(double w, double h, double px)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(px, 0), true, true);
            c.PolyLineTo(new[]
            {
                new Point(w - px, 0), new Point(w - px, px), new Point(w, px), new Point(w, h - px), new Point(w - px, h - px),
                new Point(w - px, h), new Point(px, h), new Point(px, h - px), new Point(0, h - px), new Point(0, px), new Point(px, px),
            }, true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Ink outline with notched corners, a solid fill and a hard drop shadow. Like PixelBox in PixelArt.swift.</summary>
    public static Grid Box(UIElement content, uint fill, double px = 2, bool shade = false, bool shadow = true, double opacity = 1)
    {
        var g = new Grid { SnapsToDevicePixels = true, Opacity = opacity };
        var shadowPath = new Path { Fill = Pal.B(0x000000, 115), Stretch = Stretch.None, RenderTransform = new TranslateTransform(0, px), Visibility = shadow ? Visibility.Visible : Visibility.Collapsed };
        var outline = new Path { Fill = Pal.B(Pal.Ink), Stretch = Stretch.None };
        var inner = new Border { Background = Pal.B(fill), Margin = new Thickness(px) };
        g.Children.Add(shadowPath);
        g.Children.Add(outline);
        g.Children.Add(inner);
        if (shade)
            g.Children.Add(new Border { Background = Pal.B(0x000000, 77), Height = px, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(px) });
        g.Children.Add(content);
        g.SizeChanged += (_, e) =>
        {
            var n = Notched(e.NewSize.Width, e.NewSize.Height, px);
            shadowPath.Data = n;
            outline.Data = n;
        };
        return g;
    }

    /// <summary>An arcade-style key: pressed ones light up in the skin's accent.</summary>
    public static Grid Keycap(string text, bool on, Skin skin, double w, double h, double opacity = 1)
    {
        var label = Fonts.Label(text, 10, on ? Pal.Ink : Pal.Tooth);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var b = Box(label, on ? skin.Accent : Pal.Segment, shade: true, opacity: opacity);
        b.Width = w;
        b.Height = h;
        return b;
    }
}
