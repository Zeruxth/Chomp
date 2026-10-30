using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Chomp;

/// <summary>Words in a picture, read with Windows' own text recognition (the languages your PC is set up for).</summary>
public static class Ocr
{
    public static string Text(string src)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? throw new ChompException("Windows has no text-recognition language installed (Settings → Language)");
        var img = Images.Load(src, (int)OcrEngine.MaxImageDimension).Flatten();
        using var bmp = SoftwareBitmap.CreateCopyFromBuffer(img.Data.AsBuffer(), BitmapPixelFormat.Bgra8, img.W, img.H, BitmapAlphaMode.Premultiplied);
        var result = engine.RecognizeAsync(bmp).AsTask().GetAwaiter().GetResult();
        var lines = result.Lines.Select(l => l.Text).Where(t => t.Trim().Length > 0).ToList();
        if (lines.Count == 0) throw new ChompException("No text found");
        return string.Join(Environment.NewLine, lines);
    }
}
