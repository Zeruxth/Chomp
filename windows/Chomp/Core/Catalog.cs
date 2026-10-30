using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Chomp;

public enum Kind { Image, Video, Audio, Pdf, Document, Folder, Other }

public enum Mode { Convert, Tools, Actions }

public static class KindExt
{
    public static string Noun(this Kind k) => k switch
    {
        Kind.Image => "image",
        Kind.Video => "video",
        Kind.Audio => "audio file",
        Kind.Pdf => "PDF",
        Kind.Document => "document",
        Kind.Folder => "folder",
        _ => "file",
    };

    public static string Title(this Mode m) => m switch { Mode.Convert => "Convert", Mode.Tools => "Tools", _ => "Actions" };
    public static Mode Next(this Mode m) => (Mode)(((int)m + 1) % 3);
}

/// <summary>One slice of the wheel. Ids match the Mac app so `--run` works the same.</summary>
public sealed record Op(string Id, string Title, string Detail, string? Icon, IReadOnlySet<Kind> Kinds, bool Batch = false);

/// <summary>Which options show for which file type. Mirrors Catalog.swift, minus what Windows can't do yet.</summary>
public static class Catalog
{
    // MARK: File types

    public static string Norm(string ext)
    {
        var e = ext.TrimStart('.').ToLowerInvariant();
        return e switch
        {
            "jpeg" or "jpe" or "jfif" => "jpg",
            "tif" => "tiff",
            "heif" => "heic",
            "aif" => "aiff",
            "htm" => "html",
            "markdown" => "md",
            "m4v" => "mp4",
            _ => e,
        };
    }

    public static string Ext(string path) => Norm(Path.GetExtension(path));

    public static Kind KindOf(string path)
    {
        if (Directory.Exists(path)) return Kind.Folder;
        return Ext(path) switch
        {
            "jpg" or "png" or "heic" or "webp" or "avif" or "tiff" or "gif" or "bmp" => Kind.Image,
            "mp4" or "mov" or "mkv" or "webm" or "avi" or "wmv" or "flv" or "mpg" or "mpeg" or "3gp" => Kind.Video,
            "mp3" or "m4a" or "wav" or "flac" or "aiff" or "ogg" or "opus" or "aac" or "wma" or "caf" => Kind.Audio,
            "pdf" => Kind.Pdf,
            "doc" or "docx" or "rtf" or "txt" or "md" or "html" or "odt" => Kind.Document,
            _ => Kind.Other,
        };
    }

    /// <summary>Folders become the files inside them (recursive, hidden files skipped).</summary>
    public static List<string> Expand(IEnumerable<string> items, int limit = 10_000)
    {
        var o = new List<string>();
        foreach (var u in items)
        {
            if (KindOf(u) != Kind.Folder) { o.Add(u); continue; }
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            foreach (var f in Directory.EnumerateFiles(u, "*", opts))
            {
                o.Add(f);
                if (o.Count >= limit) return o;
            }
        }
        return o;
    }

    /// <summary>The most common file kind in a drop, and how many files that drop really is.</summary>
    public static (Kind Kind, int Count) Summary(IEnumerable<string> items)
    {
        var files = Expand(items, 1500);
        var counts = files.Select(KindOf).Where(k => k != Kind.Other).GroupBy(k => k).OrderByDescending(g => g.Count()).FirstOrDefault();
        return (counts?.Key ?? Kind.Other, files.Count);
    }

    // MARK: Operations

    static readonly HashSet<Kind> Everything = new() { Kind.Image, Kind.Video, Kind.Audio, Kind.Pdf, Kind.Document, Kind.Folder, Kind.Other };

    static Op O(string id, string t, string d, string? icon, IReadOnlySet<Kind> k, bool batch = false) => new(id, t, d, icon, k, batch);
    static HashSet<Kind> K(params Kind[] k) => new(k);

    public static List<Op> Ops(Mode mode, Kind kind) => mode switch
    {
        Mode.Convert => Convert(kind),
        Mode.Tools => Tools(kind),
        _ => Actions(),
    };

    public static IEnumerable<Op> All() =>
        new[] { Mode.Convert, Mode.Tools, Mode.Actions }
            .SelectMany(m => new[] { Kind.Image, Kind.Video, Kind.Audio, Kind.Pdf, Kind.Document }.SelectMany(k => Ops(m, k)));

    static List<Op> Convert(Kind kind)
    {
        switch (kind)
        {
            case Kind.Image:
            {
                var k = K(Kind.Image);
                return new()
                {
                    O("to.jpg", "JPG", "Universal", null, k),
                    O("to.png", "PNG", "Lossless", null, k),
                    O("to.webp", "WebP", "For web", null, k),
                    O("to.avif", "AVIF", "Smallest", null, k),
                    O("to.pdf", "PDF", "Document", null, k),
                    O("to.tiff", "TIFF", "Print", null, k),
                    O("to.gif", "GIF", "Old school", null, k),
                    O("to.bmp", "BMP", "Bitmap", null, k),
                };
            }
            case Kind.Video:
            {
                var k = K(Kind.Video);
                return new()
                {
                    O("to.mp4", "MP4", "H.264", null, k),
                    O("to.mov", "MOV", "QuickTime", null, k),
                    O("to.gif", "GIF", "Loop", null, k),
                    O("to.webm", "WebM", "VP9 · web", null, k),
                    O("to.mp3", "MP3", "Audio only", null, k),
                    O("to.m4a", "M4A", "AAC audio", null, k),
                    O("to.mkv", "MKV", "Matroska", null, k),
                };
            }
            case Kind.Audio:
            {
                var k = K(Kind.Audio);
                return new()
                {
                    O("to.mp3", "MP3", "Plays anywhere", null, k),
                    O("to.m4a", "M4A", "AAC", null, k),
                    O("to.wav", "WAV", "Uncompressed", null, k),
                    O("to.flac", "FLAC", "Lossless", null, k),
                    O("to.aiff", "AIFF", "Pro audio", null, k),
                    O("to.ogg", "OGG", "Vorbis", null, k),
                    O("to.opus", "Opus", "Voice · web", null, k),
                };
            }
            case Kind.Pdf:
            {
                var k = K(Kind.Pdf);
                return new()
                {
                    O("to.png", "PNG", "Every page", null, k),
                    O("to.jpg", "JPG", "Every page", null, k),
                    O("to.txt", "TXT", "Plain text", null, k),
                };
            }
            case Kind.Document:
                // Layout-preserving conversions (PDF, DOCX, RTF…) come in a later phase.
                return new() { O("to.txt", "TXT", "Plain", null, K(Kind.Document)) };
            default:
                return new();
        }
    }

    static List<Op> Tools(Kind kind)
    {
        switch (kind)
        {
            case Kind.Image:
            {
                var k = K(Kind.Image);
                return new()
                {
                    O("img.compress", "Compress", "Smaller file", "compress", k),
                    O("img.1080", "1080", "Web / Insta", "1080", k),
                    O("img.half", "Half", "50% size", "half", k),
                    O("img.strip", "Clean", "No EXIF / GPS", "clean", k),
                    O("img.rotate", "Rotate", "90° right", "rotate", k),
                    O("img.pdf", "Combine", "One PDF", "combine", k, batch: true),
                };
            }
            case Kind.Video:
            {
                var k = K(Kind.Video);
                return new()
                {
                    O("vid.compress", "Compress", "Smaller MP4", "compress", k),
                    O("vid.10mb", "10 MB", "Send-able", "send", k),
                    O("vid.half", "Half", "50% size", "half", k),
                    O("vid.frame", "Frame", "Best still", "frame", k),
                    O("vid.mute", "Mute", "No audio", "mute", k),
                    O("vid.x2", "2×", "Speed up", "speed", k),
                };
            }
            case Kind.Audio:
            {
                var k = K(Kind.Audio);
                return new()
                {
                    O("aud.compress", "Compress", "128k MP3", "compress", k),
                    O("aud.loud", "Loudness", "−14 LUFS", "wave", k),
                    O("aud.trim", "Trim", "Silent ends", "trim", k),
                    O("aud.mono", "Mono", "One channel", "mono", k),
                };
            }
            case Kind.Pdf:
            {
                var k = K(Kind.Pdf);
                return new()
                {
                    O("pdf.compress", "Compress", "Smaller file", "compress", k),
                    O("pdf.merge", "Merge", "One PDF", "combine", k, batch: true),
                    O("pdf.split", "Split", "Page files", "split", k),
                    O("pdf.rotate", "Rotate", "90° right", "rotate", k),
                };
            }
            case Kind.Document:
                return new()
                {
                    O("doc.count", "Count", "Words", "count", K(Kind.Document, Kind.Pdf)),
                    O("doc.clean", "Clean", "Plain text", "text", K(Kind.Document)),
                };
            default:
                return new();
        }
    }

    static List<Op> Actions() => new()
    {
        O("act.copy", "Copy", "To clipboard", "copy", Everything),
        O("act.path", "Path", "Copy path", "path", Everything),
        O("act.zip", "Zip", "Archive", "zip", Everything),
        O("act.terminal", "Terminal", "Open here", "terminal", Everything),
        O("act.desktop", "Desktop", "Copy there", "desktop", Everything),
    };
}
