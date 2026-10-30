using System;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Chomp;

/// <summary>Background removal. The Mac asks Vision; Windows has nothing built in, so this runs a small salient-object
/// model (Silueta, a slimmed U²-Net, Apache 2.0, from the rembg project) on the CPU. Release builds ship it in models\.</summary>
public static class Cutout
{
    const int N = 320;   // the model's input size
    static readonly float[] Mean = { 0.485f, 0.456f, 0.406f }, Std = { 0.229f, 0.224f, 0.225f };

    static string ModelPath => Path.Combine(AppContext.BaseDirectory, "models", "cutout.onnx");
    public static bool Available => File.Exists(ModelPath);

    static readonly Lazy<InferenceSession> Session = new(() => new InferenceSession(ModelPath));

    public static string Run(string src, string o)
    {
        if (!Available) throw new ChompException("The cut-out model is missing from this copy of Chomp");
        var img = Images.Load(src);

        // The model sees a 320×320 squash of the picture, scaled so the brightest value is 1.
        using var full = img.ToSkia();
        using var small = full.Resize(new SKImageInfo(N, N, SKColorType.Bgra8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKCubicResampler.Mitchell))
                          ?? throw new ChompException("Resize failed");
        var px = Pixels.FromSkia(small).Data;
        float max = 1;
        for (var i = 0; i < px.Length; i += 4) max = Math.Max(max, Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])));
        var input = new DenseTensor<float>(new[] { 1, 3, N, N });
        for (var y = 0; y < N; y++)
            for (var x = 0; x < N; x++)
            {
                var i = (y * N + x) * 4;
                // BGRA in, RGB planes out.
                for (var c = 0; c < 3; c++) input[0, c, y, x] = (px[i + 2 - c] / max - Mean[c]) / Std[c];
            }

        var s = Session.Value;
        using var results = s.Run(new[] { NamedOnnxValue.CreateFromTensor(s.InputMetadata.Keys.First(), input) });
        var pred = results.First().AsEnumerable<float>().ToArray();   // the first output is the finest mask

        // Stretch to 0…1, then up to the picture's own size as an 8-bit mask.
        float lo = pred.Min(), hi = pred.Max(), span = Math.Max(1e-6f, hi - lo);
        using var mask = new SKBitmap(new SKImageInfo(N, N, SKColorType.Gray8, SKAlphaType.Opaque));
        var m = new byte[N * N];
        for (var i = 0; i < m.Length; i++) m[i] = (byte)Math.Clamp((int)Math.Round((pred[i] - lo) / span * 255), 0, 255);
        System.Runtime.InteropServices.Marshal.Copy(m, 0, mask.GetPixels(), m.Length);
        using var big = mask.Resize(new SKImageInfo(img.W, img.H, SKColorType.Gray8, SKAlphaType.Opaque), new SKSamplingOptions(SKCubicResampler.Mitchell))
                        ?? throw new ChompException("Resize failed");
        var a = new byte[img.W * img.H];
        System.Runtime.InteropServices.Marshal.Copy(big.GetPixels(), a, 0, a.Length);

        var outPx = (byte[])img.Data.Clone();
        var any = false;
        for (var i = 0; i < a.Length; i++)
        {
            var alpha = outPx[i * 4 + 3] * a[i] / 255;
            outPx[i * 4 + 3] = (byte)alpha;
            any |= alpha > 16;
        }
        if (!any) throw new ChompException("Couldn't find anything to cut out");
        Images.Write(new Pixels(img.W, img.H, outPx), o, "png", 1);
        return o;
    }
}
