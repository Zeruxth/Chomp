using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using WinPdf = Windows.Data.Pdf;
using WinStorage = Windows.Storage;
using WinStreams = Windows.Storage.Streams;

namespace Chomp;

/// <summary>PDFs: Windows' own renderer for pictures, PDFsharp for pages, PdfPig for text.</summary>
public static class Pdfs
{
    static PdfDocument Open(string path, PdfDocumentOpenMode mode)
    {
        try { return PdfReader.Open(path, mode); }
        catch (PdfReaderException e) when (e.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new ChompException("PDF is password-locked");
        }
        catch (Exception e) when (e is not ChompException)
        {
            throw new ChompException("Can't open PDF");
        }
    }

    public static async Task<List<string>> Convert(string src, string t)
    {
        switch (t)
        {
            case "png":
            case "jpg":
            {
                var file = await WinStorage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(src));
                var doc = await WinPdf.PdfDocument.LoadFromFileAsync(file);
                var outs = new List<string>();
                for (uint i = 0; i < doc.PageCount; i++)
                {
                    using var page = doc.GetPage(i);
                    var img = await Render(page);
                    var o = Files.Dest(src, t, doc.PageCount > 1 ? $"p{i + 1:00}" : null);
                    Images.Write(img, o, t, 0.92);
                    outs.Add(o);
                }
                return outs;
            }
            case "txt":
            {
                var o = Files.Dest(src, "txt");
                await File.WriteAllTextAsync(o, Text(src));
                return new() { o };
            }
            default:
                throw new ChompException($"Can't make {t.ToUpperInvariant()} from a PDF");
        }
    }

    /// <summary>One page at 220 dpi on white.</summary>
    static async Task<Pixels> Render(WinPdf.PdfPage page, double dpi = 220)
    {
        // Page size comes in device-independent pixels (96 per inch).
        var opts = new WinPdf.PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * dpi / 96)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * dpi / 96)),
        };
        using var stream = new WinStreams.InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, opts);
        var bytes = new byte[stream.Size];
        using (var reader = new WinStreams.DataReader(stream.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
        }
        var tmp = Files.Temp("png");
        try
        {
            await File.WriteAllBytesAsync(tmp, bytes);
            return Images.Load(tmp).Flatten();
        }
        finally { Files.TryDelete(tmp); }
    }

    public static string Text(string src)
    {
        try
        {
            using var d = UglyToad.PdfPig.PdfDocument.Open(src);
            return string.Join("\n\n", d.GetPages().Select(p => ContentOrderTextExtractor.GetText(p)));
        }
        catch (Exception e) when (e.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new ChompException("PDF is password-locked");
        }
    }

    /// <summary>Recompresses every stream. Doesn't downsample pictures yet, so scans shrink less than on a Mac.</summary>
    public static string Compress(string src)
    {
        using var doc = Open(src, PdfDocumentOpenMode.Modify);
        doc.Options.CompressContentStreams = true;
        doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
        doc.Options.NoCompression = false;
        doc.Options.EnableCcittCompressionForBilevelImages = true;
        var o = Files.Dest(src, "pdf", "small");
        doc.Save(o);
        if (Files.Size(o) >= Files.Size(src))
        {
            Files.TryDelete(o);
            throw new ChompException("Already as small as I can make it");
        }
        return o;
    }

    public static void Merge(IReadOnlyList<string> files, string o)
    {
        using var doc = new PdfDocument();
        foreach (var f in files)
        {
            using var d = Open(f, PdfDocumentOpenMode.Import);
            foreach (var p in d.Pages) doc.AddPage(p);
        }
        if (doc.PageCount == 0) throw new ChompException("Merge failed");
        doc.Save(o);
    }

    public static List<string> Split(string src)
    {
        using var d = Open(src, PdfDocumentOpenMode.Import);
        var outs = new List<string>();
        for (var i = 0; i < d.PageCount; i++)
        {
            using var one = new PdfDocument();
            one.AddPage(d.Pages[i]);
            var o = Files.Dest(src, "pdf", $"p{i + 1:00}");
            one.Save(o);
            outs.Add(o);
        }
        return outs;
    }

    public static string Rotate(string src)
    {
        using var doc = Open(src, PdfDocumentOpenMode.Modify);
        foreach (var p in doc.Pages) p.Rotate = (p.Rotate + 90) % 360;
        var o = Files.Dest(src, "pdf", "rotated");
        doc.Save(o);
        return o;
    }
}
