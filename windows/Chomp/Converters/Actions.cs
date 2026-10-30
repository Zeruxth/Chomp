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

namespace Chomp;

/// <summary>Plain text out of documents. Layout-keeping conversions come in a later phase.</summary>
public static class Docs
{
    public static string Text(string src)
    {
        switch (Catalog.Ext(src))
        {
            case "txt":
            case "md":
                return File.ReadAllText(src);
            case "html":
            {
                var s = File.ReadAllText(src);
                s = Regex.Replace(s, @"<(script|style)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"<(br|/p|/div|/h\d|/li|/tr)[^>]*>", "\n", RegexOptions.IgnoreCase);
                s = Regex.Replace(s, "<[^>]+>", "");
                return Regex.Replace(WebUtility.HtmlDecode(s), @"\n{3,}", "\n\n").Trim();
            }
            case "docx":
                return FromZipXml(src, "word/document.xml", "http://schemas.openxmlformats.org/wordprocessingml/2006/main", "p", "t", "tab");
            case "odt":
                return FromZipXml(src, "content.xml", "urn:oasis:names:tc:opendocument:xmlns:text:1.0", "p", null, "tab");
            case "pdf":
                return Pdfs.Text(src);
            default:
                throw new ChompException($"{Catalog.Ext(src).ToUpperInvariant()} isn't readable on Windows yet");
        }
    }

    /// <summary>Word and OpenDocument files are zips of XML: one line per paragraph.</summary>
    static string FromZipXml(string src, string entry, string ns, string para, string? run, string tab)
    {
        using var z = ZipFile.OpenRead(src);
        var e = z.GetEntry(entry) ?? throw new ChompException("Can't read document");
        using var s = e.Open();
        XNamespace n = ns;
        var sb = new StringBuilder();
        foreach (var p in XDocument.Load(s).Descendants(n + para))
        {
            foreach (var node in p.DescendantNodes())
            {
                if (node is XElement el && el.Name == n + tab) sb.Append('\t');
                else if (node is XText t && (run == null || t.Parent?.Name == n + run)) sb.Append(t.Value);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static string ToText(string src, string o)
    {
        File.WriteAllText(o, Text(src));
        return o;
    }

    public static RunResult Count(IEnumerable<string> files)
    {
        long words = 0, chars = 0;
        foreach (var f in files)
        {
            string s;
            try { s = Text(f); } catch { continue; }
            words += s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            chars += s.Length;
        }
        return new RunResult { Note = $"{words:N0} words · {chars:N0} characters" };
    }
}

public static class Actions
{
    public static async Task<RunResult> Run(Op op, IReadOnlyList<string> items)
    {
        var r = new RunResult();
        if (items.Count == 0) return r;
        var first = items[0];
        var n = items.Count;
        var things = n == 1 ? Path.GetFileName(first) : $"{n} items";
        switch (op.Id)
        {
            case "act.copy":
                OnUi(() =>
                {
                    var list = new StringCollection();
                    list.AddRange(items.ToArray());
                    Clipboard.SetFileDropList(list);
                });
                r.Note = $"Copied {things}";
                break;
            case "act.path":
                OnUi(() => Clipboard.SetText(string.Join(Environment.NewLine, items)));
                r.Note = n == 1 ? first : $"Copied {n} paths";
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
                r.Note = $"Terminal · {Path.GetFileName(dir.TrimEnd('\\'))}";
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
                r.Note = $"Copied {things} to Desktop";
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
