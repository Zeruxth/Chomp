using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using SkiaSharp;

namespace Chomp;

/// <summary>A decoded image as straight-alpha BGRA bytes: the one shape every step here works on.</summary>
public sealed class Pixels(int w, int h, byte[] data)
{
    public int W { get; } = w;
    public int H { get; } = h;
    public byte[] Data { get; } = data;
    public int Stride => W * 4;

    public bool HasAlpha()
    {
        for (var i = 3; i < Data.Length; i += 4) if (Data[i] != 255) return true;
        return false;
    }

    public BitmapSource ToBitmap()
    {
        var b = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, Data, Stride);
        b.Freeze();
        return b;
    }

    public SKBitmap ToSkia()
    {
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(Data, 0, bmp.GetPixels(), Data.Length);
        return bmp;
    }

    public static Pixels FromSkia(SKBitmap b)
    {
        // Skia converts colour type and alpha on the way out.
        var info = new SKImageInfo(b.Width, b.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var data = new byte[b.Width * b.Height * 4];
        var h = System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            using var pm = b.PeekPixels() ?? throw new ChompException("Can't read image");
            if (!pm.ReadPixels(info, h.AddrOfPinnedObject(), info.RowBytes)) throw new ChompException("Can't read image");
        }
        finally { h.Free(); }
        return new Pixels(b.Width, b.Height, data);
    }

    public static Pixels FromBitmap(BitmapSource src)
    {
        BitmapSource s = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var data = new byte[s.PixelWidth * s.PixelHeight * 4];
        s.CopyPixels(data, s.PixelWidth * 4, 0);
        return new Pixels(s.PixelWidth, s.PixelHeight, data);
    }

    /// <summary>On white, for formats without transparency.</summary>
    public Pixels Flatten()
    {
        var d = (byte[])Data.Clone();
        for (var i = 0; i < d.Length; i += 4)
        {
            var a = d[i + 3];
            if (a == 255) continue;
            for (var c = 0; c < 3; c++) d[i + c] = (byte)((d[i + c] * a + 255 * (255 - a) + 127) / 255);
            d[i + 3] = 255;
        }
        return new Pixels(W, H, d);
    }

    /// <summary>Long edge capped at maxPixel, with a good downscaler.</summary>
    public Pixels Fit(int maxPixel)
    {
        var full = Math.Max(W, H);
        if (maxPixel >= full) return this;
        var s = (double)maxPixel / full;
        var w = Math.Max(1, (int)Math.Round(W * s));
        var h = Math.Max(1, (int)Math.Round(H * s));
        using var b = ToSkia();
        using var r = b.Resize(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKCubicResampler.Mitchell))
                      ?? throw new ChompException("Resize failed");
        return FromSkia(r);
    }

    /// <summary>90° clockwise.</summary>
    public Pixels RotateRight()
    {
        var d = new byte[Data.Length];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
                Buffer.BlockCopy(Data, (y * W + x) * 4, d, (x * H + (H - 1 - y)) * 4, 4);
        return new Pixels(H, W, d);
    }
}

/// <summary>Images: Windows Imaging Component (WIC) to read and for JPG/PNG/TIFF/GIF/BMP/HEIC, libwebp for WebP, ffmpeg for AVIF.</summary>
public static class Images
{
    static readonly HashSet<string> Native = new() { "jpg", "png", "tiff", "gif", "bmp" };

    public static bool CanSave(string ext) => Native.Contains(ext) || ext == "webp" || (ext == "avif" && Bin.Av1Encoder.Value != null) || (ext == "heic" && HeifEncoder.Value);

    static readonly Guid HeifContainer = new("e1e62521-6787-405b-a339-500715b5763f");   // GUID_ContainerFormatHeif

    /// <summary>Windows writes HEIC only with Microsoft's HEIF and HEVC extensions installed. Try a 2×2 to find out.</summary>
    static readonly Lazy<bool> HeifEncoder = new(() =>
    {
        try
        {
            var enc = BitmapEncoder.Create(HeifContainer);
            enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr24, null, new byte[12], 6)));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.Length > 0;
        }
        catch { return false; }
    });

    const string HeifHelp = "Windows needs the HEIF Image Extensions and HEVC Video Extensions from the Microsoft Store to make HEIC";

    /// <summary>Same format if we can write it, otherwise PNG.</summary>
    public static string KeepTarget(string ext) => CanSave(ext) ? ext : "png";

    /// <summary>Format that actually gets smaller.</summary>
    public static string CompressTarget(string src)
    {
        var ext = Catalog.Ext(src);
        return ext switch
        {
            "jpg" => "jpg",
            "webp" => "webp",
            "avif" => CanSave("avif") ? "avif" : "jpg",
            "png" => Load(src).HasAlpha() ? "webp" : "jpg",
            _ => "jpg",
        };
    }

    static BitmapFrame Frame(string src)
    {
        using var fs = File.OpenRead(src);
        var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return dec.Frames[0];
    }

    static ushort Orientation(BitmapFrame f)
    {
        try { return f.Metadata is BitmapMetadata m && m.GetQuery("System.Photo.Orientation") is ushort o ? o : (ushort)1; }
        catch { return 1; }
    }

    public static (int W, int H) Dims(string src)
    {
        try
        {
            var f = Frame(src);
            return Orientation(f) >= 5 ? (f.PixelHeight, f.PixelWidth) : (f.PixelWidth, f.PixelHeight);
        }
        catch
        {
            using var codec = SKCodec.Create(src);
            return codec == null ? (0, 0) : (codec.Info.Width, codec.Info.Height);
        }
    }

    /// <summary>Decodes upright (EXIF orientation applied), optionally capped to a long edge.
    /// WIC reads whatever codecs Windows has (HEIC and AVIF need Microsoft's free extensions); Skia covers WebP without them.</summary>
    public static Pixels Load(string src, int? maxPixel = null)
    {
        Pixels p;
        try
        {
            var f = Frame(src);
            BitmapSource s = f;
            var t = Orientation(f) switch
            {
                2 => new Transform[] { new ScaleTransform(-1, 1) },
                3 => new Transform[] { new RotateTransform(180) },
                4 => new Transform[] { new ScaleTransform(1, -1) },
                5 => new Transform[] { new RotateTransform(90), new ScaleTransform(-1, 1) },
                6 => new Transform[] { new RotateTransform(90) },
                7 => new Transform[] { new RotateTransform(270), new ScaleTransform(-1, 1) },
                8 => new Transform[] { new RotateTransform(270) },
                _ => Array.Empty<Transform>(),
            };
            foreach (var x in t) s = new TransformedBitmap(s, x);
            p = Pixels.FromBitmap(s);
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            using var b = SKBitmap.Decode(src) ?? ViaFFmpeg(src);
            if (b == null)
            {
                var ext = Catalog.Ext(src);
                throw new ChompException(ext is "heic" or "avif"
                    ? $"Windows needs the free {(ext == "heic" ? "HEIF" : "AV1")} Image Extension from the Microsoft Store"
                    : "Can't read image");
            }
            p = Pixels.FromSkia(b);
        }
        return maxPixel is int m ? p.Fit(m) : p;
    }

    /// <summary>HEIC and AVIF without Microsoft's extensions: the bundled ffmpeg can still read them.</summary>
    static SKBitmap? ViaFFmpeg(string src)
    {
        if (Catalog.Ext(src) is not ("heic" or "avif") || Bin.FFmpeg is not string ff) return null;
        var tmp = Files.Temp("png");
        try
        {
            Shell.Run(ff, new List<string> { "-y", "-nostdin", "-loglevel", "error", "-i", src, "-frames:v", "1", tmp }).GetAwaiter().GetResult();
            return SKBitmap.Decode(tmp);
        }
        catch { return null; }
        finally { Files.TryDelete(tmp); }
    }

    /// <summary>Source metadata minus orientation, when the target keeps the same kind of metadata block.</summary>
    static BitmapMetadata? Metadata(string src, string ext)
    {
        if (Catalog.Ext(src) != ext || ext is not ("jpg" or "tiff")) return null;
        try
        {
            if (Frame(src).Metadata is not BitmapMetadata m) return null;
            var c = m.Clone();
            try { c.SetQuery("System.Photo.Orientation", (ushort)1); } catch { }
            return c;
        }
        catch { return null; }
    }

    public static void Write(Pixels img, string o, string ext, double quality, BitmapMetadata? meta = null)
    {
        if (ext == "webp") { WriteWebP(img, o, quality); return; }
        if (ext == "heic" && !HeifEncoder.Value) throw new ChompException(HeifHelp);
        BitmapEncoder enc = ext switch
        {
            "heic" => BitmapEncoder.Create(HeifContainer),
            "jpg" => new JpegBitmapEncoder { QualityLevel = Math.Clamp((int)Math.Round(quality * 100), 1, 100) },
            "png" => new PngBitmapEncoder(),
            "tiff" => new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw },
            "gif" => new GifBitmapEncoder(),
            "bmp" => new BmpBitmapEncoder(),
            _ => throw new ChompException($"Can't write {ext.ToUpperInvariant()}"),
        };
        var src = ext is "jpg" or "bmp" or "heic" && img.HasAlpha() ? img.Flatten() : img;
        BitmapSource bmp = src.ToBitmap();
        if (ext is "jpg" or "bmp" or "heic") bmp = new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0);
        try { enc.Frames.Add(BitmapFrame.Create(bmp, null, meta, null)); }
        catch { enc.Frames.Clear(); enc.Frames.Add(BitmapFrame.Create(bmp)); }   // metadata the encoder won't take
        try
        {
            using var fs = File.Create(o);
            enc.Save(fs);
        }
        catch
        {
            Files.TryDelete(o);
            throw;
        }
    }

    static void WriteWebP(Pixels img, string o, double quality)
    {
        using var b = img.ToSkia();
        using var data = b.Encode(SKEncodedImageFormat.Webp, Math.Clamp((int)Math.Round(quality * 100), 1, 100))
                         ?? throw new ChompException("Couldn't save WEBP");
        using var fs = File.Create(o);
        data.SaveTo(fs);
    }

    /// <summary>Writes natively, or via a temp PNG + ffmpeg for AVIF.</summary>
    public static async Task Save(Pixels img, string o, string ext, double quality, BitmapMetadata? meta)
    {
        if (ext != "avif") { Write(img, o, ext, quality, meta); return; }
        var ff = Bin.FFmpeg ?? throw ChompException.NeedsFFmpeg;
        var enc = Bin.Av1Encoder.Value ?? throw new ChompException("This ffmpeg can't make AVIF");
        var tmp = Files.Temp("png");
        try
        {
            Write(img, tmp, "png", 1);
            var crf = ((int)((1 - quality) * 50) + 12).ToString();
            var args = new List<string> { "-y", "-nostdin", "-loglevel", "error", "-i", tmp, "-c:v", enc, "-crf", crf, "-pix_fmt", "yuv420p" };
            args.AddRange(enc == "libaom-av1" ? new[] { "-still-picture", "1", "-cpu-used", "6" } : new[] { "-preset", "8" });
            args.Add(o);
            await Shell.Run(ff, args);
        }
        catch
        {
            Files.TryDelete(o);
            throw;
        }
        finally { Files.TryDelete(tmp); }
    }

    public static async Task<string> Convert(string src, string ext, string o, double quality, int? maxPixel = null, bool strip = false)
    {
        if (ext == "pdf") { ToPdf(new[] { src }, o); return o; }
        var img = Load(src, maxPixel);
        await Save(img, o, ext, quality, strip ? null : Metadata(src, ext));
        return o;
    }

    public static async Task<string> Rotate(string src, string ext, string o, double quality)
    {
        await Save(Load(src).RotateRight(), o, ext, quality, Metadata(src, ext));
        return o;
    }

    /// <summary>Still over the size limit? Lower the quality, then the size, until it fits.</summary>
    public static async Task Squeeze(string o, string ext, long limit)
    {
        if (Files.Size(o) <= limit || ext is not ("jpg" or "webp" or "avif" or "heic")) return;
        var full = Load(o);
        double q = 0.6, scale = 1;
        for (var i = 0; i < 8; i++)
        {
            var img = scale < 1 ? full.Fit(Math.Max(1, (int)(Math.Max(full.W, full.H) * scale))) : full;
            var tmp = Files.Temp(ext);
            await Save(img, tmp, ext, q, null);
            var fits = Files.Size(tmp) <= limit;
            File.Move(tmp, o, overwrite: true);
            if (fits) return;
            if (q > 0.45) q -= 0.1; else scale *= 0.75;
        }
    }

    /// <summary>One page per image, each page the image's own size.</summary>
    public static void ToPdf(IReadOnlyList<string> files, string o)
    {
        using var doc = new PdfSharp.Pdf.PdfDocument();
        foreach (var f in files)
        {
            Pixels img;
            try { img = Load(f); } catch { continue; }
            var alpha = img.HasAlpha();
            var tmp = Files.Temp(alpha ? "png" : "jpg");
            try
            {
                Write(img, tmp, alpha ? "png" : "jpg", 0.92);
                using var x = XImage.FromFile(tmp);
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(img.W);
                page.Height = XUnit.FromPoint(img.H);
                using var g = XGraphics.FromPdfPage(page);
                g.DrawImage(x, 0, 0, img.W, img.H);
            }
            finally { Files.TryDelete(tmp); }
        }
        if (doc.PageCount == 0) throw new ChompException("PDF failed");
        doc.Save(o);
    }
}
