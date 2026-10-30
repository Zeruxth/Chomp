using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;

using static Chomp.Loc;

namespace Chomp;

public static class Actions
{
    public static async Task<RunResult> Run(Op op, IReadOnlyList<string> items)
    {
        var r = new RunResult();
        if (items.Count == 0) return r;
        var first = items[0];
        var n = items.Count;
        var things = n == 1 ? Path.GetFileName(first) : Counted(n, "item", "items");
        switch (op.Id)
        {
            case "act.share":
                try
                {
                    OnUi(() => Share.Show(items));
                    r.Note = L("Sharing {0}", things);
                }
                catch (Exception e) { r.Failures.Add(L("Sharing isn't available here: {0}", e.Message)); }
                break;
            case "act.copy":
                OnUi(() =>
                {
                    var list = new StringCollection();
                    list.AddRange(items.ToArray());
                    Clipboard.SetFileDropList(list);
                });
                r.Note = L("Copied {0}", things);
                break;
            case "act.path":
                OnUi(() => Clipboard.SetText(string.Join(Environment.NewLine, items)));
                r.Note = n == 1 ? first : L("Copied {0} paths", n);
                break;
            case "act.zip":
                try
                {
                    var o = await Task.Run(() => Zip(items));
                    r.Outputs.Add(o);
                    r.Note = Path.GetFileName(o);
                }
                catch (Exception e) { r.Failures.Add(e.Message); }
                break;
            case "act.terminal":
            {
                var dir = Directory.Exists(first) ? first : Path.GetDirectoryName(first)!;
                Terminal(dir);
                r.Note = L("Terminal · {0}", Path.GetFileName(dir.TrimEnd('\\')));
                break;
            }
            case "act.desktop":
            {
                var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                foreach (var u in items)
                {
                    var d = Files.Unique(Path.Combine(desk, Path.GetFileName(u.TrimEnd('\\'))));
                    try
                    {
                        await Task.Run(() => Copy(u, d));
                        r.Outputs.Add(d);
                    }
                    catch (Exception e) { r.Failures.Add($"{Path.GetFileName(u)}: {e.Message}"); }
                }
                r.Note = L("Copied {0} to Desktop", things);
                break;
            }
        }
        return r;
    }

    static void OnUi(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) a(); else d.Invoke(a);
    }

    /// <summary>Windows Terminal if it's there, else the classic Command Prompt.</summary>
    static void Terminal(string dir)
    {
        try
        {
            var wt = new ProcessStartInfo("wt.exe") { UseShellExecute = true };
            wt.ArgumentList.Add("-d");
            wt.ArgumentList.Add(dir);
            Process.Start(wt);
        }
        catch
        {
            Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = true, WorkingDirectory = dir });
        }
    }

    static void Copy(string from, string to)
    {
        if (File.Exists(from)) { File.Copy(from, to); return; }
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(from)) Copy(d, Path.Combine(to, Path.GetFileName(d)));
    }

    public static string Zip(IReadOnlyList<string> items)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(items[0].TrimEnd('\\')))!;
        var b = items.Count == 1 ? Path.GetFileNameWithoutExtension(items[0].TrimEnd('\\')) : "Archive";
        var o = Files.Unique(Path.Combine(parent, b + ".zip"));
        try
        {
            using var z = ZipFile.Open(o, ZipArchiveMode.Create);
            foreach (var item in items)
            {
                var full = Path.GetFullPath(item.TrimEnd('\\'));
                var root = Path.GetDirectoryName(full)!;
                if (File.Exists(full))
                {
                    z.CreateEntryFromFile(full, Path.GetFileName(full), CompressionLevel.Optimal);
                    continue;
                }
                foreach (var f in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                    z.CreateEntryFromFile(f, Path.GetRelativePath(root, f).Replace('\\', '/'), CompressionLevel.Optimal);
            }
        }
        catch
        {
            Files.TryDelete(o);
            throw;
        }
        return o;
    }
}
