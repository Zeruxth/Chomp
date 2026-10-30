using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Chomp;

// MARK: - Watched folder

/// <summary>What happens to new files in the watched folder.</summary>
public sealed record WatchRule(string Id, string Title, string OpId, Kind Kind, bool HeicOnly = false)
{
    public static readonly WatchRule[] All =
    {
        new("heicToJpg", "HEIC photos → JPG", "to.jpg", Kind.Image, HeicOnly: true),
        new("compressImages", "Compress images", "img.compress", Kind.Image),
        new("imagesToWebp", "Images → WebP", "to.webp", Kind.Image),
        new("compressVideos", "Compress videos", "vid.compress", Kind.Video),
        new("fitVideos", "Videos → under the size limit", "vid.fit", Kind.Video),
    };

    public static WatchRule Current => All.FirstOrDefault(r => r.Id == Prefs.Current.WatchRule) ?? All[0];

    public bool Applies(string f) => HeicOnly ? Catalog.Ext(f) == "heic" : Catalog.KindOf(f) == Kind;

    public Op? Op => new[] { Mode.Convert, Mode.Tools }.SelectMany(m => Catalog.AllOps(m, Kind)).FirstOrDefault(o => o.Id == OpId);
}

/// <summary>Watches one folder and runs a rule on files that land in it: phone photos copied in, new screenshots…
/// Files Chomp made itself are left alone, so a rule never feeds on its own output.</summary>
public sealed class FolderWatcher
{
    public static readonly FolderWatcher Shared = new();

    FileSystemWatcher? fsw;
    readonly HashSet<string> made = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> waiting = new(StringComparer.OrdinalIgnoreCase);

    public void Restart()
    {
        fsw?.Dispose();
        fsw = null;
        var dir = Prefs.Current.WatchFolder;
        if (dir.Length == 0 || !Directory.Exists(dir)) return;
        fsw = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
        fsw.Created += (_, e) => Seen(e.FullPath);
        fsw.Renamed += (_, e) => Seen(e.FullPath);
        fsw.EnableRaisingEvents = true;
    }

    void Seen(string path) => Application.Current?.Dispatcher.InvokeAsync(() =>
    {
        if (made.Contains(path) || !waiting.Add(path)) return;
        Settle(path, -1);
    });

    /// <summary>Waits until the file stops growing (a copy or download finishing), then runs the rule on it.</summary>
    void Settle(string f, long last)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (!File.Exists(f)) { waiting.Remove(f); return; }
            var size = Files.Size(f);
            if (size != last || size == 0 || Locked(f)) { Settle(f, size); return; }
            waiting.Remove(f);
            Run(f);
        };
        t.Start();
    }

    static bool Locked(string f)
    {
        try { using var s = File.Open(f, FileMode.Open, FileAccess.Read, FileShare.None); return false; }
        catch (IOException) { return true; }
        catch { return false; }
    }

    async void Run(string f)
    {
        var rule = WatchRule.Current;
        if (made.Contains(f) || !rule.Applies(f) || rule.Op is not Op op) return;
        var r = await Engine.Run(op, new[] { f });
        foreach (var o in r.Outputs) made.Add(o);
        if (r.Outputs.Count == 0 && r.Failures.Count == 0) return;
        var ok = r.Failures.Count == 0;
        Toast.Shared.Show(ok ? Toast.Phase.Done : Toast.Phase.Failed, ok ? $"Watched folder · {rule.Title}" : "Couldn't chew that",
            ok ? Path.GetFileName(f) : r.Failures[0], r.Outputs);
        if (ok) History.Add(rule.Title, Path.GetFileName(f), r.Outputs.ToList());
    }
}

// MARK: - Clipboard

/// <summary>"Copy big, paste small": a big screenshot copied to the clipboard is swapped for a JPG of it, both as a file
/// (what chat apps upload) and as the picture. Off unless switched on in More settings.</summary>
public sealed class ClipboardShrinker
{
    public static readonly ClipboardShrinker Shared = new();
    const string Mark = "ChompShrunk";
    const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr hwnd);

    HwndSource? sink;
    bool busy;

    public void Start()
    {
        if (sink != null) return;
        // A message-only window: it never shows, it just hears about clipboard changes.
        sink = new HwndSource(new HwndSourceParameters("Chomp clipboard") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        sink.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == WM_CLIPBOARDUPDATE && !busy) Application.Current.Dispatcher.InvokeAsync(Changed, DispatcherPriority.Background);
            return IntPtr.Zero;
        });
        AddClipboardFormatListener(sink.Handle);
    }

    void Changed()
    {
        if (!Prefs.Current.ShrinkClipboard) return;
        try
        {
            var data = Clipboard.GetDataObject();
            if (data == null || data.GetDataPresent(Mark) || data.GetDataPresent(DataFormats.FileDrop) || !Clipboard.ContainsImage()) return;

            // Snipping Tool and most apps also put a PNG there: that's what a paste would upload, so it's the size to beat.
            BitmapSource? img = null;
            long before = 0;
            if (data.GetData("PNG") is MemoryStream png)
            {
                before = png.Length;
                img = BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            img ??= Clipboard.GetImage();
            if (img == null) return;
            var px = Pixels.FromBitmap(img);
            if (px.HasAlpha()) return;   // a cut-out or a window with its shadow: JPG would lose that
            if (before == 0)
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(img));
                using var ms = new MemoryStream();
                enc.Save(ms);
                before = ms.Length;
            }
            if (before < 400_000) return;

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chomp", "Clipboard");
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.EnumerateFiles(dir)) Files.TryDelete(old);   // only the latest one is ever needed
            var o = Path.Combine(dir, $"Screenshot {DateTime.Now:yyyy-MM-dd} at {DateTime.Now:HH.mm.ss}.jpg");
            Images.Write(px, o, "jpg", 0.82);
            var after = Files.Size(o);
            if (after >= before * 7 / 10) { Files.TryDelete(o); return; }

            var swap = new DataObject();
            swap.SetFileDropList(new StringCollection { o });
            swap.SetImage(BitmapFrame.Create(new Uri(o), BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
            swap.SetData(Mark, "1");
            busy = true;
            try { Clipboard.SetDataObject(swap, true); }
            finally { Application.Current.Dispatcher.InvokeAsync(() => busy = false, DispatcherPriority.Background); }
            Toast.Shared.Show(Toast.Phase.Done, "Shrunk the copied picture", $"{Files.Bytes(before)} → {Files.Bytes(after)}");
        }
        catch { /* the clipboard is shared with every app; never crash over it */ }
    }
}

// MARK: - Updates

/// <summary>Asks GitHub once a day whether there's a newer Chomp. Sends nothing but the request itself.</summary>
public static class Updates
{
    public sealed record Release(string Version, string Page);
    public static Release? Available { get; private set; }
    public static event Action? Changed;

    static DispatcherTimer? timer;

    public static void Start()
    {
        _ = Check();
        timer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        timer.Tick += (_, _) => _ = Check();
        timer.Start();
    }

    public static async Task Check()
    {
        if (!Prefs.Current.CheckUpdates) { Set(null); return; }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Chomp-Windows");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var doc = JsonDocument.Parse(await http.GetStringAsync("https://api.github.com/repos/Zeruxth/Chomp/releases/latest"));
            var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
            var page = doc.RootElement.GetProperty("html_url").GetString() ?? "";
            var mine = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0";
            Set(Newer(tag, mine) ? new Release(tag, page) : null);
        }
        catch { /* offline, or GitHub said no: try again tomorrow */ }
    }

    static void Set(Release? r) => Application.Current?.Dispatcher.InvokeAsync(() =>
    {
        Available = r;
        Changed?.Invoke();
    });

    /// <summary>"0.10.0" is newer than "0.9.2".</summary>
    public static bool Newer(string a, string b)
    {
        int[] P(string s) => s.Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
        var (x, y) = (P(a), P(b));
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
            if (p != q) return p > q;
        }
        return false;
    }
}
