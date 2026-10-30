using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Chomp;

/// <summary>Video and audio, all through ffmpeg (bundled with release builds).</summary>
public static class Media
{
    public const string Even = "scale=trunc(iw/2)*2:trunc(ih/2)*2";

    public static string[] H264(int crf, string vf = Even, string audio = "192k") =>
        ["-vf", vf, "-c:v", "libx264", "-preset", "veryfast", "-crf", crf.ToString(),
         "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", audio];

    public static string AudioTarget(string ext) =>
        new[] { "mp3", "m4a", "wav", "flac", "aiff", "ogg", "opus" }.Contains(ext) ? ext : "m4a";

    public static string[] AudioCodec(string ext) => ext switch
    {
        "mp3" => ["-c:a", "libmp3lame", "-q:a", "2"],
        "m4a" => ["-c:a", "aac", "-b:a", "256k"],
        "wav" => ["-c:a", "pcm_s16le"],
        "flac" => ["-c:a", "flac"],
        "aiff" => ["-c:a", "pcm_s16be"],
        "ogg" => ["-c:a", "libvorbis", "-q:a", "6"],
        "opus" => ["-c:a", "libopus", "-b:a", "128k"],
        _ => [],
    };

    public static async Task<string> FF(string src, string ext, string? suffix, string[] args)
    {
        var ff = Bin.FFmpeg ?? throw ChompException.NeedsFFmpeg;
        var o = Files.Dest(src, ext, suffix);
        try
        {
            await Shell.Run(ff, ["-y", "-nostdin", "-loglevel", "error", "-i", src, .. args, o]);
        }
        catch
        {
            Files.TryDelete(o);
            throw;
        }
        return o;
    }

    public static Task<string> Convert(string src, string t)
    {
        string[] a = t switch
        {
            "mp4" => [.. H264(20), "-movflags", "+faststart"],
            "mov" or "mkv" => H264(20),
            "webm" => ["-vf", Even, "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1",
                       "-deadline", "good", "-cpu-used", "4", "-c:a", "libopus", "-b:a", "128k"],
            "gif" => ["-vf", "fps=15,scale='min(720,iw)':-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a",
                      "-loop", "0"],
            _ => ["-vn", .. AudioCodec(t)],
        };
        return FF(src, t, null, a);
    }

    /// <summary>Seconds, read from ffmpeg's own banner (no ffprobe needed).</summary>
    public static async Task<double> Duration(string src)
    {
        var ff = Bin.FFmpeg ?? throw ChompException.NeedsFFmpeg;
        var psi = new ProcessStartInfo(ff) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-i", src }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        var m = Regex.Match(err, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
        if (!m.Success) return 0;
        return int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60
               + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Fits a video under a size budget with a computed bitrate.</summary>
    public static async Task<string> Under(string src, double mb)
    {
        var dur = await Duration(src);
        if (!(dur > 0) || double.IsInfinity(dur)) throw new ChompException("Can't read duration");
        var totalK = mb * 8000 * 0.92 / dur;
        var videoK = (int)(totalK - 96);
        if (videoK < 80) throw new ChompException($"Too long to fit {(int)mb} MB");
        var vf = videoK < 1500 ? "scale=w='trunc(min(1280,iw)/2)*2':h=-2" : Even;
        return await FF(src, "mp4", $"{(int)mb}MB",
        [
            // Good quality by default, with the budget as a ceiling: small videos stay small.
            "-vf", vf, "-c:v", "libx264", "-preset", "veryfast",
            "-crf", "23", "-maxrate", $"{videoK}k", "-bufsize", $"{videoK * 2}k",
            "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "96k", "-movflags", "+faststart",
        ]);
    }

    /// <summary>Keeps start…end and crops to the chosen shape, as an MP4.</summary>
    public static Task<string> Edit(string src, EditPlan p)
    {
        var vf = Even;
        if (p.Ratio is double r)
        {
            // The largest box of that shape that fits, slid along the spare room by Offset.
            var rs = r.ToString(CultureInfo.InvariantCulture);
            var o = p.Offset.ToString("0.000", CultureInfo.InvariantCulture);
            vf = $"crop=trunc(min(iw\\,ih*{rs})/2)*2:trunc(min(ih\\,iw/{rs})/2)*2:(iw-ow)*{o}:(ih-oh)*{o}";
        }
        var start = p.Start.ToString("0.000", CultureInfo.InvariantCulture);
        var span = Math.Max(0.1, p.End - p.Start).ToString("0.000", CultureInfo.InvariantCulture);
        return FF(src, "mp4", "edited", ["-ss", start, "-t", span, .. H264(20, vf: vf), "-movflags", "+faststart"]);
    }
}

/// <summary>What the video editor hands back: the part to keep, and the shape to crop to.</summary>
public sealed record EditPlan(double Start, double End, double? Ratio, double Offset = 0.5)
{
    public static readonly (string Title, double? Ratio)[] Shapes = { ("Original", null), ("1:1", 1), ("9:16", 9.0 / 16), ("16:9", 16.0 / 9) };
}
