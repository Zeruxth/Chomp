using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using static Chomp.Loc;

namespace Chomp;

// MARK: - Errors

public sealed class ChompException(string message) : Exception(L(message))
{
    public static ChompException NeedsFFmpeg => new("This copy of Chomp has no ffmpeg: download it again from the site");
}

// MARK: - Preferences

public enum SaveTo { Beside, Downloads, Custom }

/// <summary>Settings, kept as JSON in %APPDATA%\Chomp\settings.json.</summary>
public sealed class Prefs
{
    public double Quality { get; set; } = 0.85;
    public SaveTo SaveTo { get; set; } = SaveTo.Beside;
    public string CustomFolder { get; set; } = "";
    public string Skin { get; set; } = "oak";
    public bool PixelText { get; set; } = true;
    public bool Sounds { get; set; } = true;
    public int Chomps { get; set; }
    public bool Onboarded { get; set; }

    /// <summary>What the "fit" slices aim under, in MB: 10 is Discord's limit, 25 email's.</summary>
    public int SizeLimit { get; set; } = 10;
    public static readonly int[] SizeLimits = { 10, 25, 50 };
    public bool CheckUpdates { get; set; } = true;
    public bool ShrinkClipboard { get; set; }
    public string WatchFolder { get; set; } = "";
    public string WatchRule { get; set; } = "heicToJpg";
    /// <summary>Wheel slices someone switched off, as "kind:op id" (actions use "any:").</summary>
    public List<string> HiddenSlices { get; set; } = new();

    public int Limit => SizeLimits.Contains(SizeLimit) ? SizeLimit : 10;

    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chomp");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Prefs Current { get; } = Load();

    /// <summary>Fires after any change, so open windows can repaint in the new skin.</summary>
    public static event Action? Changed;

    static Prefs Load()
    {
        try { return JsonSerializer.Deserialize<Prefs>(File.ReadAllText(FilePath)) ?? new Prefs(); }
        catch { return new Prefs(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* settings are a nicety; never crash over them */ }
        Changed?.Invoke();
    }
}

// MARK: - Binaries

public static class Bin
{
    /// <summary>ffmpeg next to Chomp.exe (release builds ship one), else whatever is on PATH.</summary>
    public static readonly string? FFmpeg = Find();

    static string? Find()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
        if (File.Exists(bundled)) return bundled;
        bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(bundled)) return bundled;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                var p = Path.Combine(dir.Trim('"'), "ffmpeg.exe");
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    /// <summary>Which AV1 encoder this ffmpeg was built with, if any.</summary>
    public static readonly Lazy<string?> Av1Encoder = new(() =>
    {
        if (FFmpeg == null) return null;
        try
        {
            var psi = new ProcessStartInfo(FFmpeg, "-hide_banner -encoders")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            p.ErrorDataReceived += (_, _) => { };
            p.BeginErrorReadLine();
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return new[] { "libsvtav1", "libaom-av1" }.FirstOrDefault(e => o.Contains($" {e} "));
        }
        catch { return null; }
    });
}

// MARK: - Shell

public static class Shell
{
    /// <summary>Runs a program without a console window. Throws with its last stderr line on failure.</summary>
    public static async Task<string> Run(string exe, IEnumerable<string> args, string? cwd = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardError = true, RedirectStandardOutput = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (cwd != null) psi.WorkingDirectory = cwd;
        using var p = Process.Start(psi) ?? throw new ChompException($"Can't start {Path.GetFileName(exe)}");
        p.StandardInput.Close();
        var err = p.StandardError.ReadToEndAsync();
        var _ = p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        var e = await err;
        if (p.ExitCode == 0) return e;
        var line = e.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        throw new ChompException(line ?? $"Exit {p.ExitCode}");
    }
}

// MARK: - Files

public static class Files
{
    public static string Downloads =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public static string OutputDir(string src)
    {
        var prefs = Prefs.Current;
        switch (prefs.SaveTo)
        {
            case SaveTo.Beside:
                var dir = Path.GetDirectoryName(Path.GetFullPath(src));
                if (dir != null) return dir;
                break;
            case SaveTo.Custom:
                // A folder that's gone (deleted, drive unplugged) falls back to Downloads\Chomp.
                if (prefs.CustomFolder.Length > 0 && Directory.Exists(prefs.CustomFolder)) return prefs.CustomFolder;
                break;
        }
        var d = Path.Combine(Downloads, "Chomp");
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>"photo.heic" + ext "jpg" + suffix "small" → "photo (small).jpg", never overwriting.</summary>
    public static string Dest(string src, string ext, string? suffix = null)
    {
        var b = Path.GetFileNameWithoutExtension(src) + (suffix != null ? $" ({suffix})" : "");
        return Unique(Path.Combine(OutputDir(src), b + "." + ext));
    }

    public static string DestNamed(string name, string near, string ext) =>
        Unique(Path.Combine(OutputDir(near), name + "." + ext));

    public static string Unique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? "";
        var ext = Path.GetExtension(path);
        var b = Path.GetFileNameWithoutExtension(path);
        for (var i = 2; ; i++)
        {
            var c = Path.Combine(dir, $"{b} {i}{ext}");
            if (!File.Exists(c) && !Directory.Exists(c)) return c;
        }
    }

    public static long Size(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    public static string Temp(string ext) => Path.Combine(Path.GetTempPath(), $"chomp-{Guid.NewGuid():N}.{ext}");

    public static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    /// <summary>"1.8 MB", like Finder and Explorer.</summary>
    public static string Bytes(long n)
    {
        string[] u = { "bytes", "KB", "MB", "GB", "TB" };
        double v = n;
        var i = 0;
        while (v >= 1000 && i < u.Length - 1) { v /= 1000; i++; }
        return i == 0 ? $"{n} bytes" : $"{v:0.#} {u[i]}";
    }
}

// MARK: - History

public sealed record HistoryItem(string Title, string Detail, IReadOnlyList<string> Outputs);

public static class History
{
    public static readonly ObservableCollection<HistoryItem> Items = new();

    public static void Add(string title, string detail, IReadOnlyList<string> outputs)
    {
        Items.Insert(0, new HistoryItem(title, detail, outputs));
        while (Items.Count > 12) Items.RemoveAt(Items.Count - 1);
    }

    public static void Remove(IReadOnlyList<string> outputs)
    {
        foreach (var i in Items.Where(i => i.Outputs.SequenceEqual(outputs)).ToList()) Items.Remove(i);
    }
}
