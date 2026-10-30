using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

using static Chomp.Loc;

namespace Chomp;

public static class Program
{
    static readonly string Pipe = $"Chomp-{Environment.UserName}";

    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--run") return RunOp(args[1], args.Skip(2).ToList());

        var files = args.Where(a => File.Exists(a) || Directory.Exists(a)).Select(Path.GetFullPath).ToList();

        // One Chomp at a time: a second launch (Send to, a double-click) hands its files to the first and leaves.
        using var mutex = new Mutex(true, $@"Local\{Pipe}", out var first);
        if (!first)
        {
            Forward(files);
            return 0;
        }

        var app = new ChompApp();
        app.Startup += (_, _) => app.Start(files);
        return app.Run();
    }

    static void Forward(List<string> files)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", Pipe, PipeDirection.Out);
            c.Connect(2000);
            using var w = new StreamWriter(c, Encoding.UTF8);
            foreach (var f in files) w.WriteLine(f);
        }
        catch { /* the first Chomp is on its way out; nothing to do */ }
    }

    public static void Listen(Action<List<string>> onFiles)
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var s = new NamedPipeServerStream(Pipe, PipeDirection.In, 1);
                    s.WaitForConnection();
                    using var r = new StreamReader(s, Encoding.UTF8);
                    var files = new List<string>();
                    while (r.ReadLine() is { } line) if (line.Length > 0) files.Add(line);
                    onFiles(files);
                }
                catch (IOException) { }
            }
        }) { IsBackground = true, Name = "Chomp pipe" };
        t.Start();
    }

    /// <summary>`Chomp.exe --run &lt;op id&gt; &lt;files&gt;` runs one wheel choice from a terminal and prints what happened,
    /// e.g. `Chomp.exe --run to.webp photo.jpg`. For testing conversions without dragging.</summary>
    static int RunOp(string id, List<string> files)
    {
        AttachConsole(-1);
        // The same id (to.gif, to.jpg…) exists for several kinds of file: take the one for these files.
        var kind = files.Count > 0 ? Catalog.KindOf(files[0]) : Kind.Other;
        var matches = Catalog.All().Where(o => o.Id == id).ToList();
        var op = matches.FirstOrDefault(o => o.Kinds.Contains(kind)) ?? matches.FirstOrDefault();
        if (op == null) { Console.WriteLine($"No such op: {id}"); return 2; }
        var r = Task.Run(() => Engine.Run(op, files.Select(Path.GetFullPath).ToList())).GetAwaiter().GetResult();
        Console.WriteLine("outputs: " + string.Join(", ", r.Outputs));
        Console.WriteLine("failures: " + string.Join(", ", r.Failures));
        if (r.Note != null) Console.WriteLine("note: " + r.Note);
        Console.WriteLine($"bytes: {r.BytesIn} → {r.BytesOut} skipped: {r.Skipped}");
        return r.Failures.Count == 0 ? 0 : 1;
    }
}

public sealed class ChompApp : Application
{
    WinForms.NotifyIcon? tray;
    readonly DragWatcher watcher = new();

    public ChompApp() => ShutdownMode = ShutdownMode.OnExplicitShutdown;

    public void Start(List<string> files)
    {
        var icon = GetResourceStream(new Uri("pack://application:,,,/Assets/Chomp.ico"))!.Stream;
        tray = new WinForms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(icon, WinForms.SystemInformation.SmallIconSize),
            Text = "Chomp",
            Visible = true,
            ContextMenuStrip = new WinForms.ContextMenuStrip(),
        };
        tray.ContextMenuStrip.Items.Add(L("Open Chomp"), null, (_, _) => MenuWindow.Shared.Toggle());
        tray.ContextMenuStrip.Items.Add(L("Quit"), null, (_, _) => Shutdown());
        tray.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) MenuWindow.Shared.Toggle(); };
        Exit += (_, _) => { if (tray != null) tray.Visible = false; };

        WheelWindow.Shared.OnRun = Run;
        watcher.OnTrigger = m => WheelWindow.Shared.Trigger(m);
        watcher.Start();
        Updates.Start();
        FolderWatcher.Shared.Restart();
        ClipboardShrinker.Shared.Start();
        Program.Listen(f => Dispatcher.InvokeAsync(() => OpenWheel(f)));

        var p = Prefs.Current;
        if (!p.Onboarded)
        {
            p.Onboarded = true;
            p.Save();
            Dispatcher.InvokeAsync(() => Toast.Shared.Show(Toast.Phase.Done, L("Chomp is awake"), L("Drag any file, then tap Shift, Ctrl or Alt")), DispatcherPriority.ApplicationIdle);
            if (!Integration.InSendTo) try { Integration.InSendTo = true; } catch { }
        }
        if (files.Count > 0) OpenWheel(files);
    }

    /// <summary>Send to → Chomp, or files passed on the command line.</summary>
    void OpenWheel(List<string> files)
    {
        if (files.Count > 0) WheelWindow.Shared.Open(files, Mode.Convert, interactive: true);
        else MenuWindow.Shared.Toggle();
    }

    async void Run(Op op, IReadOnlyList<string> files, int job)
    {
        var isConvert = op.Id.StartsWith("to.");
        var what = files.Count == 1 ? Path.GetFileName(files[0]) : Counted(files.Count, "item", "items");
        var title = isConvert ? L("Converting to {0}…", op.Title) : $"{op.Title}…";
        // The wheel shows progress in its ring; the bottom toast is only for when the wheel has gone.
        if (!WheelWindow.Shared.JobStarted(job, title, what) && !op.Id.StartsWith("act."))
            Toast.Shared.Show(Toast.Phase.Working, title, what);
        RunResult r;
        try { r = await Engine.Run(op, files); }
        catch (Exception e)
        {
            r = new RunResult();
            r.Failures.Add(e.Message);
        }
        Finish(op, r, job);
    }

    void Finish(Op op, RunResult r, int job)
    {
        if (r.Cancelled && r.Outputs.Count == 0 && r.Failures.Count == 0) return;
        var isConvert = op.Id.StartsWith("to.");
        var n = r.Outputs.Count;
        var parts = new List<string>();
        if (r.Note != null) parts.Add(r.Note);
        if (r.Note == null && r.BytesIn > 0 && r.BytesOut > 0)
        {
            var diff = r.BytesOut - r.BytesIn;
            var f = Files.Bytes(Math.Abs(diff));
            if (diff <= 0) parts.Add(L("{0} saved ({1}%)", f, (int)Math.Round(-diff * 100.0 / r.BytesIn)));
            else parts.Add($"+{f}");
        }
        if (r.Skipped > 0) parts.Add(L("{0} already {1}", r.Skipped, op.Title));
        if (r.Failures.Count > 0) parts.Add(r.Failures.Count == 1 ? r.Failures[0] : L("{0} failed", r.Failures.Count));

        var ok = n > 0 || (r.Failures.Count == 0 && (r.Note != null || r.Skipped > 0));
        string title;
        if (!ok) title = L("Couldn't chew that");
        else if (n == 0 && r.Skipped > 0 && r.Note == null) title = L("Already {0}", op.Title);
        else if (op.Id.StartsWith("act.") || op.Id == "doc.count") title = op.Title;
        else if (isConvert) title = L(n == 1 ? "{0} file → {1}" : "{0} files → {1}", n, op.Title);
        else title = L(n == 1 ? "{0} · {1} file" : "{0} · {1} files", op.Title, n);
        var detail = string.Join(" · ", parts);

        if (!WheelWindow.Shared.JobFinished(job, ok, title, detail, r.Outputs))
            Toast.Shared.Show(ok ? Toast.Phase.Done : Toast.Phase.Failed, title, detail, r.Outputs);
        if (ok && r.Outputs.Count > 0) History.Add(title, detail, r.Outputs.ToList());
        if (ok) { Prefs.Current.Chomps++; Prefs.Current.Save(); }
        if (Prefs.Current.Sounds) Sound.Play(ok);
    }
}

/// <summary>A soft pop when it worked, a low buzz when it didn't. Made on the fly, so there are no sound files to ship.</summary>
static class Sound
{
    static readonly Lazy<byte[]> Pop = new(() => Tone(660, 1320, 0.07, 0.35));
    static readonly Lazy<byte[]> Buzz = new(() => Tone(150, 120, 0.18, 0.25));

    public static void Play(bool ok)
    {
        try
        {
            var p = new SoundPlayer(new MemoryStream(ok ? Pop.Value : Buzz.Value));
            p.Play();
        }
        catch { }
    }

    /// <summary>A sine sweep from f0 to f1 Hz with a quick fade, as a 16-bit mono WAV.</summary>
    static byte[] Tone(double f0, double f1, double secs, double vol)
    {
        const int rate = 44100;
        var n = (int)(rate * secs);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / n;
            phase += 2 * Math.PI * (f0 + (f1 - f0) * t) / rate;
            var env = Math.Min(1, i / (rate * 0.004)) * (1 - t) * (1 - t);
            w.Write((short)(Math.Sin(phase) * env * vol * short.MaxValue));
        }
        w.Flush();
        return ms.ToArray();
    }
}
