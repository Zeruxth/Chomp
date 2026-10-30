using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chomp;

public sealed class RunResult
{
    public List<string> Outputs { get; } = new();
    public long BytesIn { get; set; }
    public long BytesOut { get; set; }
    public List<string> Failures { get; } = new();
    public int Skipped { get; set; }
    public string? Note { get; set; }
}

/// <summary>Runs one wheel choice over a drop (files and/or folders).</summary>
public static class Engine
{
    public static async Task<RunResult> Run(Op op, IReadOnlyList<string> items)
    {
        if (op.Id.StartsWith("act.")) return await Actions.Run(op, items);

        var files = Catalog.Expand(items).Where(f => op.Kinds.Contains(Catalog.KindOf(f))).ToList();
        var r = new RunResult();
        if (files.Count == 0)
        {
            r.Failures.Add("No matching files");
            return r;
        }

        if (op.Id == "doc.count") return Docs.Count(files);

        if (op.Batch)
        {
            try
            {
                var o = await Task.Run(() => Batch(op, files));
                r.Outputs.Add(o);
                r.BytesIn = files.Sum(Files.Size);
                r.BytesOut = Files.Size(o);
                r.Note = $"{files.Count} → 1 PDF";
            }
            catch (Exception e) { r.Failures.Add(e.Message); }
            return r;
        }

        if (op.Id.StartsWith("to."))
        {
            var target = op.Id[3..];
            var before = files.Count;
            files = files.Where(f => Catalog.Ext(f) != target).ToList();
            r.Skipped = before - files.Count;
        }

        var limit = Math.Max(2, Math.Min(4, Environment.ProcessorCount / 2));
        using var gate = new SemaphoreSlim(limit);
        var sync = new object();
        await Task.WhenAll(files.Select(async f =>
        {
            await gate.WaitAsync();
            try
            {
                var outs = await Task.Run(() => Perform(op, f));
                lock (sync)
                {
                    r.Outputs.AddRange(outs);
                    r.BytesIn += Files.Size(f);
                    r.BytesOut += outs.Sum(Files.Size);
                }
            }
            catch (Exception e)
            {
                lock (sync) r.Failures.Add($"{System.IO.Path.GetFileName(f)}: {e.Message}");
            }
            finally { gate.Release(); }
        }));
        return r;
    }

    static string Batch(Op op, List<string> files)
    {
        var sorted = files.OrderBy(f => System.IO.Path.GetFileName(f), NaturalOrder.Instance).ToList();
        var o = Files.DestNamed("Combined", sorted[0], "pdf");
        switch (op.Id)
        {
            case "img.pdf": Images.ToPdf(sorted, o); break;
            case "pdf.merge": Pdfs.Merge(sorted, o); break;
            case "doc.merge": Docs.Combine(sorted, o); break;
            default: throw new ChompException("Unknown operation");
        }
        return o;
    }

    static async Task<List<string>> Perform(Op op, string src)
    {
        var kind = Catalog.KindOf(src);
        var ext = Catalog.Ext(src);
        var q = Prefs.Current.Quality;

        if (op.Id.StartsWith("to."))
        {
            var t = op.Id[3..];
            return kind switch
            {
                Kind.Image => new() { await Images.Convert(src, t, Files.Dest(src, t), q) },
                Kind.Video or Kind.Audio => new() { await Media.Convert(src, t) },
                Kind.Pdf when t is "docx" or "rtf" => new() { Docs.Convert(src, t, Files.Dest(src, t)) },
                Kind.Pdf => await Pdfs.Convert(src, t),
                Kind.Document => new() { Docs.Convert(src, t, Files.Dest(src, t)) },
                _ => throw new ChompException("Unsupported file"),
            };
        }

        switch (op.Id)
        {
            // Images
            case "img.compress":
            {
                var t = Images.CompressTarget(src);
                return new() { await Images.Convert(src, t, Files.Dest(src, t, "small"), Math.Min(q, 0.7)) };
            }
            case "img.1080":
            {
                var t = Images.KeepTarget(ext);
                return new() { await Images.Convert(src, t, Files.Dest(src, t, "1080"), q, maxPixel: 1080) };
            }
            case "img.half":
            {
                var t = Images.KeepTarget(ext);
                var (w, h) = Images.Dims(src);
                return new() { await Images.Convert(src, t, Files.Dest(src, t, "half"), q, maxPixel: Math.Max(1, Math.Max(w, h) / 2)) };
            }
            case "img.strip":
            {
                var t = Images.KeepTarget(ext);
                return new() { await Images.Convert(src, t, Files.Dest(src, t, "clean"), Math.Max(q, 0.9), strip: true) };
            }
            case "img.cutout":
                return new() { Cutout.Run(src, Files.Dest(src, "png", "cutout")) };
            case "img.rotate":
            {
                var t = Images.KeepTarget(ext);
                return new() { await Images.Rotate(src, t, Files.Dest(src, t, "rotated"), Math.Max(q, 0.9)) };
            }

            // Video
            case "vid.compress":
                return new() { await Media.FF(src, "mp4", "small", [.. Media.H264(28, audio: "128k"), "-movflags", "+faststart"]) };
            case "vid.10mb":
                return new() { await Media.Under(src, 10) };
            case "vid.half":
                return new() { await Media.FF(src, "mp4", "half", Media.H264(23, vf: "scale=trunc(iw/4)*2:trunc(ih/4)*2")) };
            case "vid.frame":
                return new() { await Media.FF(src, "png", "frame", ["-vf", "thumbnail=120", "-frames:v", "1"]) };
            case "vid.mute":
                return new() { await Media.FF(src, ext, "muted", ["-an", "-c:v", "copy"]) };
            case "vid.x2":
                return new() { await Media.FF(src, "mp4", "2x", [.. Media.H264(22, vf: "setpts=0.5*PTS," + Media.Even), "-af", "atempo=2.0"]) };

            // Audio
            case "aud.compress":
                return new() { await Media.FF(src, "mp3", "small", ["-vn", "-c:a", "libmp3lame", "-b:a", "128k"]) };
            case "aud.loud":
            {
                var t = Media.AudioTarget(ext);
                return new() { await Media.FF(src, t, "loud", ["-vn", "-af", "loudnorm=I=-14:TP=-1:LRA=11", .. Media.AudioCodec(t)]) };
            }
            case "aud.trim":
            {
                var t = Media.AudioTarget(ext);
                const string f = "silenceremove=start_periods=1:start_threshold=-50dB,areverse,silenceremove=start_periods=1:start_threshold=-50dB,areverse";
                return new() { await Media.FF(src, t, "trimmed", ["-vn", "-af", f, .. Media.AudioCodec(t)]) };
            }
            case "aud.mono":
            {
                var t = Media.AudioTarget(ext);
                return new() { await Media.FF(src, t, "mono", ["-vn", "-ac", "1", .. Media.AudioCodec(t)]) };
            }

            // PDF
            case "pdf.compress": return new() { Pdfs.Compress(src) };
            case "pdf.split": return Pdfs.Split(src);
            case "pdf.rotate": return new() { Pdfs.Rotate(src) };

            // Documents
            case "doc.clean": return new() { Docs.ToText(src, Files.Dest(src, "txt", "clean")) };

            default: throw new ChompException("Unknown operation");
        }
    }
}

/// <summary>"page 2" before "page 10", like Explorer sorts.</summary>
public sealed class NaturalOrder : IComparer<string>
{
    public static readonly NaturalOrder Instance = new();

    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int StrCmpLogicalW(string a, string b);

    public int Compare(string? a, string? b) => StrCmpLogicalW(a ?? "", b ?? "");
}
