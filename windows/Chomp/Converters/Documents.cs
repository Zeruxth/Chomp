using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

using static Chomp.Loc;

namespace Chomp;

// MARK: - The shape every document goes through

public enum BlockKind { Para, Heading, Bullet, Number, Quote }

/// <summary>A stretch of text in one style. "\n" inside is a line break, "\t" a tab.</summary>
public sealed record Run(string Text, bool Bold = false, bool Italic = false);

public sealed class Block(BlockKind kind, int level = 0)
{
    public BlockKind Kind { get; } = kind;
    public int Level { get; } = level;   // heading 1–6
    public List<Run> Runs { get; } = new();
    public string Plain => string.Concat(Runs.Select(r => r.Text));
    public bool Empty => string.IsNullOrWhiteSpace(Plain);
}

/// <summary>Documents: read any of TXT, MD, HTML, DOCX, ODT, RTF and PDF into headings, paragraphs, lists, bold and italic,
/// and write that back out as TXT, MD, HTML, RTF, DOCX, ODT or PDF. Tables and pictures don't make the trip.
/// The Mac does this with textutil; this is a small stand-in for it.</summary>
public static class Docs
{
    public static readonly string[] Targets = { "pdf", "docx", "rtf", "txt", "html", "odt", "md" };

    public static List<Block> Read(string src) => Catalog.Ext(src) switch
    {
        "txt" => DocReaders.Txt(File.ReadAllText(src)),
        "md" => DocReaders.Markdown(File.ReadAllText(src)),
        "html" => DocReaders.Html(File.ReadAllText(src)),
        "docx" => DocReaders.Docx(src),
        "odt" => DocReaders.Odt(src),
        "rtf" => DocReaders.Rtf(File.ReadAllBytes(src)),
        "pdf" => DocReaders.Txt(Pdfs.Text(src), joinLines: true),
        "doc" => throw new ChompException("Old Word files (.doc) can't be read here: save it as DOCX in Word first"),
        var e => throw new ChompException($"{e.ToUpperInvariant()} isn't readable on Windows yet"),
    };

    public static string Text(string src) => DocWriters.Txt(Read(src));

    public static string Convert(string src, string ext, string o)
    {
        var blocks = Read(src).Where(b => !b.Empty).ToList();
        if (blocks.Count == 0) throw new ChompException("No text in there");
        var title = Path.GetFileNameWithoutExtension(src);
        try
        {
            switch (ext)
            {
                case "txt": File.WriteAllText(o, DocWriters.Txt(blocks)); break;
                case "md": File.WriteAllText(o, DocWriters.Markdown(blocks)); break;
                case "html": File.WriteAllText(o, DocWriters.Html(blocks, title)); break;
                case "rtf": File.WriteAllText(o, DocWriters.Rtf(blocks), Encoding.ASCII); break;
                case "docx": DocWriters.Docx(blocks, o); break;
                case "odt": DocWriters.Odt(blocks, o); break;
                case "pdf": DocWriters.Pdf(blocks, title, o); break;
                default: throw new ChompException($"Can't write {ext.ToUpperInvariant()}");
            }
        }
        catch
        {
            Files.TryDelete(o);
            throw;
        }
        return o;
    }

    /// <summary>Plain text, without any formatting marks.</summary>
    public static string ToText(string src, string o)
    {
        File.WriteAllText(o, Text(src));
        return o;
    }

    /// <summary>Documents and PDFs into one PDF, in name order.</summary>
    public static void Combine(IReadOnlyList<string> files, string o)
    {
        var temps = new List<string>();
        try
        {
            var pdfs = new List<string>();
            foreach (var f in files)
            {
                if (Catalog.Ext(f) == "pdf") { pdfs.Add(f); continue; }
                var t = Files.Temp("pdf");
                temps.Add(t);
                try { Convert(f, "pdf", t); pdfs.Add(t); } catch (ChompException) { }
            }
            if (pdfs.Count == 0) throw new ChompException("No text in there");
            Pdfs.Merge(pdfs, o);
        }
        finally { foreach (var t in temps) Files.TryDelete(t); }
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
        return new RunResult { Note = L("{0} words · {1} characters", words.ToString("N0"), chars.ToString("N0")) };
    }
}

// MARK: - Reading

static class DocReaders
{
    static Block Para(string text, BlockKind k = BlockKind.Para, int level = 0)
    {
        var b = new Block(k, level);
        b.Runs.Add(new Run(text));
        return b;
    }

    /// <summary>Paragraphs are separated by blank lines. PDF text wraps mid-sentence, so its lines join back up.</summary>
    public static List<Block> Txt(string s, bool joinLines = false) =>
        Regex.Split(s.Replace("\r\n", "\n").Replace('\r', '\n'), @"\n[ \t]*\n")
            .Select(p => p.Trim('\n'))
            .Where(p => p.Trim().Length > 0)
            .Select(p => Para(joinLines ? Regex.Replace(p, @"\s*\n\s*", " ") : p))
            .ToList();

    // MARK: Markdown

    public static List<Block> Markdown(string s)
    {
        var o = new List<Block>();
        Block? cur = null;
        var buf = new StringBuilder();
        void Flush()
        {
            if (cur != null && buf.Length > 0) { cur.Runs.AddRange(Inline(buf.ToString())); o.Add(cur); }
            cur = null;
            buf.Clear();
        }
        var fence = false;
        foreach (var raw in s.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```")) { Flush(); fence = !fence; continue; }
            if (fence) { o.Add(Para(line)); continue; }
            if (line.Trim().Length == 0) { Flush(); continue; }
            Match m;
            if ((m = Regex.Match(line, @"^\s{0,3}(#{1,6})\s+(.*?)\s*#*$")).Success)
            {
                Flush();
                cur = new Block(BlockKind.Heading, m.Groups[1].Length);
                buf.Append(m.Groups[2].Value);
                Flush();
            }
            else if ((m = Regex.Match(line, @"^\s*[-*+]\s+(.*)$")).Success && !Regex.IsMatch(line, @"^\s*([-*_])(\s*\1){2,}\s*$"))
            {
                Flush();
                cur = new Block(BlockKind.Bullet);
                buf.Append(m.Groups[1].Value);
            }
            else if ((m = Regex.Match(line, @"^\s*\d+[.)]\s+(.*)$")).Success)
            {
                Flush();
                cur = new Block(BlockKind.Number);
                buf.Append(m.Groups[1].Value);
            }
            else if ((m = Regex.Match(line, @"^\s*>\s?(.*)$")).Success)
            {
                if (cur?.Kind != BlockKind.Quote) { Flush(); cur = new Block(BlockKind.Quote); }
                else buf.Append(' ');
                buf.Append(m.Groups[1].Value);
            }
            else if (Regex.IsMatch(line, @"^\s*([-*_])(\s*\1){2,}\s*$"))
            {
                Flush();   // a horizontal rule
            }
            else
            {
                if (cur == null) cur = new Block(BlockKind.Para);
                else buf.Append(line.EndsWith("  ") ? "\n" : " ");
                buf.Append(line.Trim());
            }
        }
        Flush();
        return o;
    }

    static readonly Regex InlineRx = new(@"(\*\*\*|___)(.+?)\1|(\*\*|__)(.+?)\3|(?<![\w*])([*_])(?!\s)(.+?)(?<!\s)\5(?![\w*])|`([^`]+)`|!?\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);

    /// <summary>**bold**, *italic*, `code`, [links](…).</summary>
    static IEnumerable<Run> Inline(string s)
    {
        var at = 0;
        foreach (Match m in InlineRx.Matches(s))
        {
            if (m.Index > at) yield return new Run(Unescape(s[at..m.Index]));
            if (m.Groups[2].Success) yield return new Run(Unescape(m.Groups[2].Value), true, true);
            else if (m.Groups[4].Success) yield return new Run(Unescape(m.Groups[4].Value), Bold: true);
            else if (m.Groups[6].Success) yield return new Run(Unescape(m.Groups[6].Value), Italic: true);
            else if (m.Groups[7].Success) yield return new Run(m.Groups[7].Value);
            else yield return new Run(Unescape(m.Groups[8].Value));
            at = m.Index + m.Length;
        }
        if (at < s.Length) yield return new Run(Unescape(s[at..]));
    }

    static string Unescape(string s) => Regex.Replace(s, @"\\([\\`*_{}\[\]()#+\-.!])", "$1");

    // MARK: HTML

    static readonly Regex Tag = new(@"<!--.*?-->|<(/?)([a-zA-Z][a-zA-Z0-9]*)[^>]*?(/?)>", RegexOptions.Singleline | RegexOptions.Compiled);

    public static List<Block> Html(string s)
    {
        s = Regex.Replace(s, @"<(script|style|head|template|noscript)\b[^>]*>.*?</\1\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var o = new List<Block>();
        var lists = new Stack<bool>();   // true = numbered
        int bold = 0, italic = 0, quote = 0;
        Block? cur = null;
        void Flush()
        {
            if (cur != null && !cur.Empty)
            {
                // Collapse what HTML would have collapsed, at the block's edges too.
                var runs = cur.Runs.ToList();
                cur.Runs.Clear();
                var trimmed = runs.Select(r => r with { Text = r.Text.Replace("\u00a0", " ") }).ToList();
                if (trimmed.Count > 0)
                {
                    trimmed[0] = trimmed[0] with { Text = trimmed[0].Text.TrimStart(' ') };
                    trimmed[^1] = trimmed[^1] with { Text = trimmed[^1].Text.TrimEnd(' ') };
                }
                cur.Runs.AddRange(trimmed.Where(r => r.Text.Length > 0));
                o.Add(cur);
            }
            cur = null;
        }
        void Start(BlockKind k, int level = 0) { Flush(); cur = new Block(k, level); }
        void Text(string t)
        {
            t = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(t, @"\s+", " ")), "[ \t]+", " ");
            if (t.Length == 0) return;
            cur ??= new Block(quote > 0 ? BlockKind.Quote : BlockKind.Para);
            if (cur.Runs.Count > 0 && cur.Runs[^1].Text.EndsWith(' ') && t.StartsWith(' ')) t = t[1..];
            cur.Runs.Add(new Run(t, bold > 0, italic > 0));
        }

        var at = 0;
        foreach (Match m in Tag.Matches(s))
        {
            if (m.Index > at) Text(s[at..m.Index]);
            at = m.Index + m.Length;
            if (!m.Groups[2].Success) continue;   // a comment
            var close = m.Groups[1].Value == "/";
            var name = m.Groups[2].Value.ToLowerInvariant();
            switch (name)
            {
                case "b" or "strong": bold = Math.Max(0, bold + (close ? -1 : 1)); break;
                case "i" or "em" or "cite": italic = Math.Max(0, italic + (close ? -1 : 1)); break;
                case "br":
                    cur ??= new Block(quote > 0 ? BlockKind.Quote : BlockKind.Para);
                    cur.Runs.Add(new Run("\n"));
                    break;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    if (close) Flush(); else Start(BlockKind.Heading, name[1] - '0');
                    break;
                case "ul" or "ol":
                    Flush();
                    if (close) { if (lists.Count > 0) lists.Pop(); }
                    else lists.Push(name == "ol");
                    break;
                case "li":
                    if (close) Flush();
                    else Start(lists.Count > 0 && lists.Peek() ? BlockKind.Number : BlockKind.Bullet);
                    break;
                case "blockquote":
                    Flush();
                    quote = Math.Max(0, quote + (close ? -1 : 1));
                    break;
                case "p" or "div" or "section" or "article" or "header" or "footer" or "main" or "tr" or "table" or "pre" or "dd" or "dt" or "figcaption" or "hr":
                    // A <p> inside a list item is still that item.
                    if (!(cur?.Kind is BlockKind.Bullet or BlockKind.Number && name is "p" or "div")) Flush();
                    break;
                case "td" or "th":
                    if (close && cur != null) cur.Runs.Add(new Run("\t"));
                    break;
            }
        }
        if (at < s.Length) Text(s[at..]);
        Flush();
        return o;
    }

    // MARK: Word

    static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    static bool On(XElement? e) => e != null && (string?)e.Attribute(W + "val") is null or "1" or "true" or "on";

    public static List<Block> Docx(string src)
    {
        using var z = ZipFile.OpenRead(src);
        var doc = Load(z, "word/document.xml") ?? throw new ChompException("Can't read document");
        var styles = Load(z, "word/styles.xml");
        var numbering = Load(z, "word/numbering.xml");

        // Paragraph style id → its name ("heading 1", "Quote"…), and list number → whether it counts.
        var names = new Dictionary<string, string>();
        foreach (var e in styles?.Descendants(W + "style") ?? Enumerable.Empty<XElement>())
            if ((string?)e.Attribute(W + "type") == "paragraph")
                names.TryAdd((string?)e.Attribute(W + "styleId") ?? "", ((string?)e.Element(W + "name")?.Attribute(W + "val") ?? "").ToLowerInvariant());
        var numbered = new Dictionary<string, bool>();
        if (numbering != null)
        {
            var abstracts = new Dictionary<string, bool>();
            foreach (var a in numbering.Descendants(W + "abstractNum"))
                abstracts.TryAdd((string?)a.Attribute(W + "abstractNumId") ?? "",
                    (string?)a.Descendants(W + "numFmt").FirstOrDefault()?.Attribute(W + "val") is string f && f != "bullet" && f != "none");
            foreach (var n in numbering.Descendants(W + "num"))
            {
                var id = (string?)n.Attribute(W + "numId") ?? "";
                var abs = (string?)n.Element(W + "abstractNumId")?.Attribute(W + "val") ?? "";
                numbered[id] = abstracts.TryGetValue(abs, out var isNum) && isNum;
            }
        }

        var o = new List<Block>();
        foreach (var p in doc.Descendants(W + "p"))
        {
            var pPr = p.Element(W + "pPr");
            var styleId = (string?)pPr?.Element(W + "pStyle")?.Attribute(W + "val") ?? "";
            var style = names.TryGetValue(styleId, out var nm) ? nm : styleId.ToLowerInvariant();
            var numId = (string?)pPr?.Element(W + "numPr")?.Element(W + "numId")?.Attribute(W + "val");
            Block b;
            var hm = Regex.Match(style, @"^heading\s*(\d)$");
            if (hm.Success) b = new Block(BlockKind.Heading, Math.Clamp(int.Parse(hm.Groups[1].Value), 1, 6));
            else if (style is "title") b = new Block(BlockKind.Heading, 1);
            else if (style is "subtitle") b = new Block(BlockKind.Heading, 2);
            else if (numId != null && numId != "0") b = new Block(numbered.TryGetValue(numId, out var isNum) && isNum ? BlockKind.Number : BlockKind.Bullet);
            else if (style.Contains("list bullet")) b = new Block(BlockKind.Bullet);
            else if (style.Contains("list number")) b = new Block(BlockKind.Number);
            else if (style.Contains("quote")) b = new Block(BlockKind.Quote);
            else b = new Block(BlockKind.Para);

            foreach (var r in p.Descendants(W + "r"))
            {
                if (r.Ancestors(W + "p").FirstOrDefault() != p) continue;   // text boxes have their own paragraphs
                var rPr = r.Element(W + "rPr");
                bool bo = On(rPr?.Element(W + "b")), it = On(rPr?.Element(W + "i"));
                var sb = new StringBuilder();
                foreach (var e in r.Elements())
                {
                    if (e.Name == W + "t") sb.Append(e.Value);
                    else if (e.Name == W + "tab") sb.Append('\t');
                    else if (e.Name == W + "br" || e.Name == W + "cr") sb.Append('\n');
                }
                if (sb.Length > 0) b.Runs.Add(new Run(sb.ToString(), bo, it));
            }
            o.Add(b);
        }
        return o;
    }

    static XDocument? Load(ZipArchive z, string name)
    {
        var e = z.GetEntry(name);
        if (e == null) return null;
        using var s = e.Open();
        return XDocument.Load(s);
    }

    // MARK: OpenDocument

    static readonly XNamespace T = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    static readonly XNamespace S = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";

    public static List<Block> Odt(string src)
    {
        using var z = ZipFile.OpenRead(src);
        var doc = Load(z, "content.xml") ?? throw new ChompException("Can't read document");
        var styleDocs = new[] { doc, Load(z, "styles.xml") }.Where(d => d != null).ToList();

        // Which text styles are bold or italic, and which list styles count.
        var looks = new Dictionary<string, (bool B, bool I)>();
        var numberedLists = new HashSet<string>();
        foreach (var d in styleDocs)
        {
            foreach (var st in d!.Descendants(S + "style"))
            {
                var name = (string?)st.Attribute(S + "name");
                var tp = st.Element(S + "text-properties");
                if (name == null || tp == null) continue;
                looks[name] = ((string?)tp.Attribute(Fo + "font-weight") is "bold" or "600" or "700" or "800" or "900",
                               (string?)tp.Attribute(Fo + "font-style") is "italic" or "oblique");
            }
            foreach (var ls in d.Descendants(T + "list-style"))
                if (ls.Elements().FirstOrDefault()?.Name == T + "list-level-style-number" && (string?)ls.Attribute(S + "name") is string n)
                    numberedLists.Add(n);
        }

        var o = new List<Block>();
        var body = doc.Descendants(Office + "text").FirstOrDefault() ?? throw new ChompException("Can't read document");
        void Walk(XElement parent, string? list)
        {
            foreach (var e in parent.Elements())
            {
                if (e.Name == T + "h")
                    o.Add(Runs(new Block(BlockKind.Heading, Math.Clamp((int?)e.Attribute(T + "outline-level") ?? 1, 1, 6)), e));
                else if (e.Name == T + "p")
                {
                    var kind = list == null ? BlockKind.Para : (numberedLists.Contains(list) ? BlockKind.Number : BlockKind.Bullet);
                    var style = (string?)e.Attribute(T + "style-name") ?? "";
                    if (list == null && style.Contains("Quot")) kind = BlockKind.Quote;
                    o.Add(Runs(new Block(kind), e));
                }
                else if (e.Name == T + "list")
                    Walk(e, (string?)e.Attribute(T + "style-name") ?? list ?? "");
                else if (e.Name == T + "list-item" || e.Name == T + "section")
                    Walk(e, list);
            }
        }
        Block Runs(Block b, XElement e)
        {
            void Add(XNode n, bool bo, bool it)
            {
                switch (n)
                {
                    case XText t: b.Runs.Add(new Run(Regex.Replace(t.Value, @"\s+", " "), bo, it)); break;
                    case XElement x when x.Name == T + "s": b.Runs.Add(new Run(new string(' ', (int?)x.Attribute(T + "c") ?? 1), bo, it)); break;
                    case XElement x when x.Name == T + "tab": b.Runs.Add(new Run("\t", bo, it)); break;
                    case XElement x when x.Name == T + "line-break": b.Runs.Add(new Run("\n", bo, it)); break;
                    case XElement x when x.Name == T + "span" || x.Name == T + "a":
                    {
                        var look = (string?)x.Attribute(T + "style-name") is string sn && looks.TryGetValue(sn, out var l) ? l : (false, false);
                        foreach (var c in x.Nodes()) Add(c, bo || look.Item1, it || look.Item2);
                        break;
                    }
                    case XElement x when x.Name != T + "note": foreach (var c in x.Nodes()) Add(c, bo, it); break;
                }
            }
            var pl = (string?)e.Attribute(T + "style-name") is string ps && looks.TryGetValue(ps, out var pLook) ? pLook : (false, false);
            foreach (var n in e.Nodes()) Add(n, pl.Item1, pl.Item2);
            return b;
        }
        Walk(body, null);
        return o;
    }

    // MARK: RTF

    static readonly HashSet<string> Skip = new()
    {
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "object", "header", "footer", "headerl", "headerr", "footerl", "footerr",
        "listtable", "listoverridetable", "rsidtbl", "generator", "xmlnstbl", "themedata", "colorschememapping", "latentstyles",
        "datastore", "mmathPr", "listtext", "pntext", "pntxta", "pntxtb", "fldinst", "filetbl", "revtbl", "bkmkstart", "bkmkend",
    };

    public static List<Block> Rtf(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var s = Encoding.Latin1.GetString(bytes);
        if (!s.StartsWith("{\\rtf")) throw new ChompException("Can't read document");
        var enc = Encoding.GetEncoding(1252);

        var o = new List<Block>();
        var cur = new Block(BlockKind.Para);
        var kind = BlockKind.Para;
        var level = 0;
        var text = new StringBuilder();
        var bytesRun = new List<byte>();
        (bool B, bool I, bool Skip, int Uc) st = (false, false, false, 1);
        var stack = new Stack<(bool, bool, bool, int)>();
        var toSkip = 0;   // fallback characters after \uN
        bool bo = false, it = false;

        void FlushBytes()
        {
            if (bytesRun.Count == 0) return;
            text.Append(enc.GetString(bytesRun.ToArray()));
            bytesRun.Clear();
        }
        void FlushRun()
        {
            FlushBytes();
            if (text.Length > 0) cur.Runs.Add(new Run(text.ToString(), bo, it));
            text.Clear();
            bo = st.B;
            it = st.I;
        }
        void Style()
        {
            if (st.B == bo && st.I == it) return;
            FlushRun();
        }
        void EndPara()
        {
            FlushRun();
            o.Add(cur);
            cur = new Block(kind, level);
        }

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '{') { stack.Push(st); continue; }
            if (c == '}') { if (stack.Count > 0) { Style(); st = stack.Pop(); } continue; }
            if (c == '\r' || c == '\n') continue;
            if (c != '\\')
            {
                if (st.Skip) continue;
                if (toSkip > 0) { toSkip--; continue; }
                Style();
                bytesRun.Add((byte)c);
                continue;
            }

            // A control word, symbol or hex byte.
            if (i + 1 >= s.Length) break;
            var n = s[i + 1];
            if (n == '\'' && i + 3 < s.Length)
            {
                i += 3;
                if (st.Skip) continue;
                if (toSkip > 0) { toSkip--; continue; }
                Style();
                bytesRun.Add(System.Convert.ToByte(s.Substring(i - 1, 2), 16));
                continue;
            }
            if (!char.IsLetter(n))
            {
                i++;
                if (st.Skip) continue;
                if (n == '*') { st.Skip = true; continue; }
                if (n is '\\' or '{' or '}') { Style(); bytesRun.Add((byte)n); }
                else if (n == '~') { Style(); bytesRun.Add(0xA0); }
                else if (n == '\n' || n == '\r') EndPara();
                continue;
            }
            var j = i + 1;
            while (j < s.Length && char.IsLetter(s[j])) j++;
            var word = s[(i + 1)..j];
            var k = j;
            if (k < s.Length && (s[k] == '-' || char.IsDigit(s[k]))) { k++; while (k < s.Length && char.IsDigit(s[k])) k++; }
            int? arg = k > j ? int.Parse(s[j..k]) : null;
            if (k < s.Length && s[k] == ' ') k++;
            i = k - 1;

            if (Skip.Contains(word)) { st.Skip = true; continue; }
            if (word == "ansicpg" && arg is int cp) { try { enc = Encoding.GetEncoding(cp); } catch { } continue; }
            if (st.Skip) continue;
            switch (word)
            {
                case "par": EndPara(); break;
                case "line": FlushBytes(); text.Append('\n'); break;
                case "tab": FlushBytes(); text.Append('\t'); break;
                case "cell": FlushBytes(); text.Append('\t'); break;
                case "row": EndPara(); break;
                case "emdash": FlushBytes(); text.Append('—'); break;
                case "endash": FlushBytes(); text.Append('–'); break;
                case "bullet": FlushBytes(); text.Append('•'); break;
                case "lquote": FlushBytes(); text.Append('‘'); break;
                case "rquote": FlushBytes(); text.Append('’'); break;
                case "ldblquote": FlushBytes(); text.Append('“'); break;
                case "rdblquote": FlushBytes(); text.Append('”'); break;
                case "b": st.B = arg != 0; break;
                case "i": st.I = arg != 0; break;
                case "plain": st.B = st.I = false; break;
                case "uc": st.Uc = arg ?? 1; break;
                case "u":
                    Style();
                    FlushBytes();
                    text.Append((char)(arg is int u && u < 0 ? u + 65536 : arg ?? '?'));
                    toSkip = st.Uc;
                    break;
                case "pard":
                    kind = BlockKind.Para;
                    level = 0;
                    if (cur.Runs.Count == 0 && text.Length == 0 && bytesRun.Count == 0) cur = new Block(kind, level);
                    break;
                case "outlinelevel":
                    kind = BlockKind.Heading;
                    level = Math.Clamp((arg ?? 0) + 1, 1, 6);
                    if (cur.Runs.Count == 0 && text.Length == 0 && bytesRun.Count == 0) cur = new Block(kind, level);
                    break;
                case "ls" or "ilvl" or "pnlvlblt":
                    if (kind == BlockKind.Para) kind = BlockKind.Bullet;
                    if (cur.Runs.Count == 0 && text.Length == 0 && bytesRun.Count == 0) cur = new Block(kind, level);
                    break;
            }
        }
        EndPara();
        return o;
    }
}

// MARK: - Writing

static class DocWriters
{
    // MARK: Plain text and Markdown

    public static string Txt(IEnumerable<Block> blocks)
    {
        var sb = new StringBuilder();
        var n = 0;
        foreach (var b in blocks)
        {
            if (b.Empty) continue;
            n = b.Kind == BlockKind.Number ? n + 1 : 0;
            var t = b.Plain.Trim('\n');
            sb.Append(b.Kind switch
            {
                BlockKind.Bullet => "• " + t,
                BlockKind.Number => $"{n}. " + t,
                BlockKind.Quote => "    " + t.Replace("\n", "\n    "),
                _ => t,
            });
            sb.Append(b.Kind is BlockKind.Bullet or BlockKind.Number ? "\n" : "\n\n");
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    public static string Markdown(IEnumerable<Block> blocks)
    {
        var sb = new StringBuilder();
        var n = 0;
        BlockKind? last = null;
        foreach (var b in blocks)
        {
            n = b.Kind == BlockKind.Number ? n + 1 : 0;
            var list = b.Kind is BlockKind.Bullet or BlockKind.Number;
            if (last != null && !(list && last == b.Kind)) sb.Append('\n');
            var t = string.Concat(b.Runs.Select(MdRun)).Trim();
            sb.Append(b.Kind switch
            {
                BlockKind.Heading => new string('#', b.Level) + " " + t.Replace("\n", " "),
                BlockKind.Bullet => "- " + t,
                BlockKind.Number => $"{n}. " + t,
                BlockKind.Quote => "> " + t.Replace("  \n", "  \n> "),
                _ => t,
            });
            sb.Append('\n');
            last = b.Kind;
        }
        return sb.ToString();
    }

    static string MdRun(Run r)
    {
        var t = Regex.Replace(r.Text, @"([\\`*_\[\]])", @"\$1").Replace("\n", "  \n");
        if (t.Trim().Length == 0 || !(r.Bold || r.Italic)) return t;
        // Marks go inside the spaces, or Markdown won't see them.
        var lead = t[..(t.Length - t.TrimStart().Length)];
        var trail = t[t.TrimEnd().Length..];
        var mark = r.Bold && r.Italic ? "***" : (r.Bold ? "**" : "*");
        return lead + mark + t.Trim() + mark + trail;
    }

    // MARK: HTML

    public static string Html(List<Block> blocks, string title)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<title>").Append(WebUtility.HtmlEncode(title)).Append("</title>\n");
        sb.Append("<style>body{font:16px/1.6 system-ui,'Segoe UI',sans-serif;max-width:42em;margin:3em auto;padding:0 1em;color:#222}blockquote{margin-left:0;padding-left:1em;border-left:3px solid #ccc;color:#555}</style>\n");
        sb.Append("</head>\n<body>\n");
        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            var inner = string.Concat(b.Runs.Select(HtmlRun));
            if (b.Kind is BlockKind.Bullet or BlockKind.Number)
            {
                var tag = b.Kind == BlockKind.Number ? "ol" : "ul";
                if (i == 0 || blocks[i - 1].Kind != b.Kind) sb.Append('<').Append(tag).Append(">\n");
                sb.Append("<li>").Append(inner).Append("</li>\n");
                if (i == blocks.Count - 1 || blocks[i + 1].Kind != b.Kind) sb.Append("</").Append(tag).Append(">\n");
                continue;
            }
            sb.Append(b.Kind switch
            {
                BlockKind.Heading => $"<h{b.Level}>{inner}</h{b.Level}>",
                BlockKind.Quote => $"<blockquote><p>{inner}</p></blockquote>",
                _ => $"<p>{inner}</p>",
            }).Append('\n');
        }
        return sb.Append("</body>\n</html>\n").ToString();
    }

    static string HtmlRun(Run r)
    {
        var t = WebUtility.HtmlEncode(r.Text).Replace("\n", "<br>\n");
        if (r.Italic) t = $"<em>{t}</em>";
        if (r.Bold) t = $"<strong>{t}</strong>";
        return t;
    }

    // MARK: RTF

    public static string Rtf(IEnumerable<Block> blocks)
    {
        var sb = new StringBuilder(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fswiss Calibri;}}\viewkind4\uc1" + "\n");
        var n = 0;
        foreach (var b in blocks)
        {
            n = b.Kind == BlockKind.Number ? n + 1 : 0;
            sb.Append(@"\pard\sa160\sl276\slmult1\f0");
            switch (b.Kind)
            {
                case BlockKind.Heading:
                    sb.Append(@"\outlinelevel").Append(b.Level - 1).Append(@"\sb240\b\fs").Append(b.Level switch { 1 => 36, 2 => 30, 3 => 26, _ => 24 }).Append(' ');
                    break;
                case BlockKind.Bullet: sb.Append(@"\fi-360\li720\fs22 \bullet\tab "); break;
                case BlockKind.Number: sb.Append(@"\fi-360\li720\fs22 ").Append(n).Append(@".\tab "); break;
                case BlockKind.Quote: sb.Append(@"\li720\fs22\i "); break;
                default: sb.Append(@"\fs22 "); break;
            }
            foreach (var r in b.Runs)
            {
                var on = (r.Bold ? @"\b " : "") + (r.Italic ? @"\i " : "");
                var off = (r.Bold ? @"\b0 " : "") + (r.Italic ? @"\i0 " : "");
                sb.Append('{').Append(on).Append(Escape(r.Text)).Append(off).Append('}');
            }
            sb.Append(b.Kind == BlockKind.Heading ? @"\b0" : "").Append(b.Kind == BlockKind.Quote ? @"\i0" : "").Append("\\par\n");
        }
        return sb.Append('}').ToString();
    }

    static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (c is '\\' or '{' or '}') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append(@"\line ");
            else if (c == '\t') sb.Append(@"\tab ");
            else if (c < 0x80) sb.Append(c);
            else sb.Append(@"\u").Append((short)c).Append('?');
        }
        return sb.ToString();
    }

    // MARK: Word

    static string X(string s) => System.Security.SecurityElement.Escape(Regex.Replace(s, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "")) ?? "";

    public static void Docx(List<Block> blocks, string o)
    {
        var body = new StringBuilder();
        var nums = 2;   // numId 1 is bullets; each numbered list gets its own, so it starts at 1
        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            if (b.Kind == BlockKind.Number && (i == 0 || blocks[i - 1].Kind != BlockKind.Number)) nums++;
            body.Append("<w:p><w:pPr>");
            body.Append(b.Kind switch
            {
                BlockKind.Heading => $"<w:pStyle w:val=\"Heading{Math.Min(b.Level, 4)}\"/>",
                BlockKind.Bullet => "<w:pStyle w:val=\"ListParagraph\"/><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr>",
                BlockKind.Number => $"<w:pStyle w:val=\"ListParagraph\"/><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"{nums}\"/></w:numPr>",
                BlockKind.Quote => "<w:pStyle w:val=\"Quote\"/>",
                _ => "",
            });
            body.Append("</w:pPr>");
            foreach (var r in b.Runs)
            {
                body.Append("<w:r>");
                if (r.Bold || r.Italic) body.Append("<w:rPr>").Append(r.Bold ? "<w:b/>" : "").Append(r.Italic ? "<w:i/>" : "").Append("</w:rPr>");
                var parts = Regex.Split(r.Text, "(\n|\t)");
                foreach (var p in parts)
                {
                    if (p == "\n") body.Append("<w:br/>");
                    else if (p == "\t") body.Append("<w:tab/>");
                    else if (p.Length > 0) body.Append("<w:t xml:space=\"preserve\">").Append(X(p)).Append("</w:t>");
                }
                body.Append("</w:r>");
            }
            body.Append("</w:p>\n");
        }

        var numbering = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:numbering xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">");
        numbering.Append("<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"singleLevel\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"•\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum>");
        numbering.Append("<w:abstractNum w:abstractNumId=\"1\"><w:multiLevelType w:val=\"singleLevel\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum>");
        numbering.Append("<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>");
        for (var id = 3; id <= nums; id++)
            numbering.Append($"<w:num w:numId=\"{id}\"><w:abstractNumId w:val=\"1\"/><w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"1\"/></w:lvlOverride></w:num>");
        numbering.Append("</w:numbering>");

        string Heading(int n, int size) =>
            $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{n}\"><w:name w:val=\"heading {n}\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>" +
            $"<w:pPr><w:keepNext/><w:spacing w:before=\"{(n == 1 ? 360 : 240)}\" w:after=\"120\"/><w:outlineLvl w:val=\"{n - 1}\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"{size}\"/></w:rPr></w:style>";
        var styles = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:eastAsia=\"Calibri\" w:cs=\"Calibri\"/><w:sz w:val=\"22\"/></w:rPr></w:rPrDefault>" +
            "<w:pPrDefault><w:pPr><w:spacing w:after=\"160\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>" +
            Heading(1, 36) + Heading(2, 30) + Heading(3, 26) + Heading(4, 24) +
            "<w:style w:type=\"paragraph\" w:styleId=\"ListParagraph\"><w:name w:val=\"List Paragraph\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:spacing w:after=\"60\"/><w:ind w:left=\"720\"/><w:contextualSpacing/></w:pPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Quote\"><w:name w:val=\"Quote\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/><w:pPr><w:ind w:left=\"720\" w:right=\"720\"/></w:pPr><w:rPr><w:i/><w:color w:val=\"555555\"/></w:rPr></w:style>" +
            "</w:styles>";

        using var z = ZipFile.Open(o, ZipArchiveMode.Create);
        Put(z, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
            "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/></Types>");
        Put(z, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Put(z, "word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/></Relationships>");
        Put(z, "word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>\n" +
            body + "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>");
        Put(z, "word/styles.xml", styles);
        Put(z, "word/numbering.xml", numbering.ToString());
    }

    static void Put(ZipArchive z, string name, string text, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var w = new StreamWriter(z.CreateEntry(name, level).Open(), new UTF8Encoding(false));
        w.Write(text);
    }

    // MARK: OpenDocument

    public static void Odt(List<Block> blocks, string o)
    {
        var body = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            var inner = string.Concat(b.Runs.Select(OdtRun));
            if (b.Kind is BlockKind.Bullet or BlockKind.Number)
            {
                if (i == 0 || blocks[i - 1].Kind != b.Kind) body.Append($"<text:list text:style-name=\"{(b.Kind == BlockKind.Number ? "LNum" : "LBul")}\">");
                body.Append("<text:list-item><text:p text:style-name=\"P_List\">").Append(inner).Append("</text:p></text:list-item>");
                if (i == blocks.Count - 1 || blocks[i + 1].Kind != b.Kind) body.Append("</text:list>");
                body.Append('\n');
                continue;
            }
            body.Append(b.Kind switch
            {
                BlockKind.Heading => $"<text:h text:style-name=\"Heading_20_{Math.Min(b.Level, 4)}\" text:outline-level=\"{b.Level}\">{inner}</text:h>",
                BlockKind.Quote => $"<text:p text:style-name=\"Quotations\">{inner}</text:p>",
                _ => $"<text:p text:style-name=\"Standard\">{inner}</text:p>",
            }).Append('\n');
        }

        const string ns = "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
                          "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" office:version=\"1.3\"";
        string Heading(int n, string size) =>
            $"<style:style style:name=\"Heading_20_{n}\" style:display-name=\"Heading {n}\" style:family=\"paragraph\" style:parent-style-name=\"Standard\" style:next-style-name=\"Standard\" style:default-outline-level=\"{n}\">" +
            $"<style:paragraph-properties fo:margin-top=\"0.15in\" fo:margin-bottom=\"0.08in\" fo:keep-with-next=\"always\"/><style:text-properties fo:font-size=\"{size}\" fo:font-weight=\"bold\"/></style:style>";
        var styles = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<office:document-styles {ns}><office:styles>" +
            "<style:default-style style:family=\"paragraph\"><style:text-properties style:font-name=\"Calibri\" fo:font-family=\"Calibri\" fo:font-size=\"11pt\"/></style:default-style>" +
            "<style:style style:name=\"Standard\" style:family=\"paragraph\"><style:paragraph-properties fo:margin-bottom=\"0.11in\" fo:line-height=\"115%\"/></style:style>" +
            Heading(1, "18pt") + Heading(2, "15pt") + Heading(3, "13pt") + Heading(4, "12pt") +
            "<style:style style:name=\"Quotations\" style:family=\"paragraph\" style:parent-style-name=\"Standard\"><style:paragraph-properties fo:margin-left=\"0.5in\" fo:margin-right=\"0.5in\"/><style:text-properties fo:font-style=\"italic\" fo:color=\"#555555\"/></style:style>" +
            "</office:styles></office:document-styles>";
        var content = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<office:document-content {ns}><office:automatic-styles>" +
            "<style:style style:name=\"P_List\" style:family=\"paragraph\" style:parent-style-name=\"Standard\"><style:paragraph-properties fo:margin-bottom=\"0.03in\"/></style:style>" +
            "<style:style style:name=\"T_B\" style:family=\"text\"><style:text-properties fo:font-weight=\"bold\"/></style:style>" +
            "<style:style style:name=\"T_I\" style:family=\"text\"><style:text-properties fo:font-style=\"italic\"/></style:style>" +
            "<style:style style:name=\"T_BI\" style:family=\"text\"><style:text-properties fo:font-weight=\"bold\" fo:font-style=\"italic\"/></style:style>" +
            "<text:list-style style:name=\"LBul\"><text:list-level-style-bullet text:level=\"1\" text:bullet-char=\"•\"><style:list-level-properties text:list-level-position-and-space-mode=\"label-alignment\"><style:list-level-label-alignment text:label-followed-by=\"listtab\" fo:text-indent=\"-0.25in\" fo:margin-left=\"0.5in\"/></style:list-level-properties></text:list-level-style-bullet></text:list-style>" +
            "<text:list-style style:name=\"LNum\"><text:list-level-style-number text:level=\"1\" style:num-suffix=\".\" style:num-format=\"1\"><style:list-level-properties text:list-level-position-and-space-mode=\"label-alignment\"><style:list-level-label-alignment text:label-followed-by=\"listtab\" fo:text-indent=\"-0.25in\" fo:margin-left=\"0.5in\"/></style:list-level-properties></text:list-level-style-number></text:list-style>" +
            "</office:automatic-styles><office:body><office:text>\n" + body + "</office:text></office:body></office:document-content>";

        using var z = ZipFile.Open(o, ZipArchiveMode.Create);
        // The mimetype has to come first, uncompressed, so other apps can sniff it.
        Put(z, "mimetype", "application/vnd.oasis.opendocument.text", CompressionLevel.NoCompression);
        Put(z, "META-INF/manifest.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" manifest:version=\"1.3\">" +
            "<manifest:file-entry manifest:full-path=\"/\" manifest:media-type=\"application/vnd.oasis.opendocument.text\"/>" +
            "<manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/>" +
            "<manifest:file-entry manifest:full-path=\"styles.xml\" manifest:media-type=\"text/xml\"/></manifest:manifest>");
        Put(z, "content.xml", content);
        Put(z, "styles.xml", styles);
    }

    static string OdtRun(Run r)
    {
        var t = Regex.Replace(X(r.Text), "  +", m => " " + $"<text:s text:c=\"{m.Length - 1}\"/>").Replace("\n", "<text:line-break/>").Replace("\t", "<text:tab/>");
        if (!r.Bold && !r.Italic) return t;
        return $"<text:span text:style-name=\"{(r.Bold && r.Italic ? "T_BI" : (r.Bold ? "T_B" : "T_I"))}\">{t}</text:span>";
    }

    // MARK: PDF

    static DocWriters() => GlobalFontSettings.FontResolver ??= new WindowsFonts();

    const double PageW = 595, PageH = 842, Margin = 64;   // A4, in points
    const string Face = "Segoe UI";

    public static void Pdf(List<Block> blocks, string title, string o)
    {
        using var doc = new PdfDocument();
        doc.Info.Title = title;
        doc.Info.Creator = "Chomp";
        XGraphics? g = null;
        double y = 0;

        void NewPage()
        {
            g?.Dispose();
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(PageW);
            page.Height = XUnit.FromPoint(PageH);
            g = XGraphics.FromPdfPage(page);
            y = Margin;
        }
        NewPage();

        var n = 0;
        foreach (var b in blocks)
        {
            n = b.Kind == BlockKind.Number ? n + 1 : 0;
            var size = b.Kind == BlockKind.Heading ? b.Level switch { 1 => 22, 2 => 17, 3 => 14, _ => 12 } : 11;
            var lineH = size * 1.45;
            double indent = b.Kind is BlockKind.Bullet or BlockKind.Number or BlockKind.Quote ? 22 : 0;
            var before = b.Kind == BlockKind.Heading ? size * 0.9 : 0;
            var after = b.Kind is BlockKind.Bullet or BlockKind.Number ? 3 : size * 0.75;
            var color = b.Kind == BlockKind.Quote ? XBrushes.DimGray : XBrushes.Black;

            if (y > Margin) y += before;
            // Keep a heading with at least a line of what follows it.
            if (y + lineH * (b.Kind == BlockKind.Heading ? 3 : 1) > PageH - Margin) NewPage();

            XFont Font(Run r)
            {
                var bold = r.Bold || b.Kind == BlockKind.Heading;
                var italic = r.Italic || b.Kind == BlockKind.Quote;
                var style = bold && italic ? XFontStyleEx.BoldItalic : bold ? XFontStyleEx.Bold : italic ? XFontStyleEx.Italic : XFontStyleEx.Regular;
                return new XFont(Face, size, style);
            }

            // Words (with their trailing space) laid out left to right, wrapping at the margin.
            var line = new List<(string Text, XFont Font, double W)>();
            double x = 0, width = PageW - 2 * Margin - indent;
            var first = true;
            void Flush()
            {
                if (y + lineH > PageH - Margin) NewPage();
                if (first && b.Kind is BlockKind.Bullet or BlockKind.Number)
                    g!.DrawString(b.Kind == BlockKind.Bullet ? "•" : $"{n}.", new XFont(Face, size), color, Margin + indent - 16, y + size);
                var cx = Margin + indent;
                foreach (var w in line) { g!.DrawString(w.Text, w.Font, color, cx, y + size); cx += w.W; }
                y += lineH;
                line.Clear();
                x = 0;
                first = false;
            }
            foreach (var r in b.Runs)
            {
                var font = Font(r);
                foreach (Match m in Regex.Matches(r.Text, @"\n|\t|[^\s]+\s*|\s+"))
                {
                    var t = m.Value;
                    if (t == "\n") { Flush(); continue; }
                    if (t == "\t") t = "    ";
                    var w = g!.MeasureString(t, font).Width;
                    if (x + w > width && x > 0 && t.Trim().Length > 0)
                    {
                        Flush();
                        if (t.Trim().Length == 0) continue;
                    }
                    // A single word wider than the page gets cut into pieces that fit.
                    while (w > width && t.Length > 1)
                    {
                        var k = t.Length - 1;
                        while (k > 1 && g.MeasureString(t[..k], font).Width > width) k--;
                        line.Add((t[..k], font, g.MeasureString(t[..k], font).Width));
                        Flush();
                        t = t[k..];
                        w = g.MeasureString(t, font).Width;
                    }
                    line.Add((t, font, w));
                    x += w;
                }
            }
            if (line.Count > 0 || first) Flush();
            y += after;
        }
        g?.Dispose();
        doc.Save(o);
    }
}

/// <summary>Hands PDFsharp the font files from Windows\Fonts: Segoe UI, or Arial if that's missing.</summary>
sealed class WindowsFonts : IFontResolver
{
    static readonly string Dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    static readonly string[][] Families =
    {
        new[] { "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf" },
        new[] { "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf" },
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var i = (bold ? 1 : 0) + (italic ? 2 : 0);
        foreach (var f in Families)
        {
            if (File.Exists(Path.Combine(Dir, f[i]))) return new FontResolverInfo(f[i]);
            // No bold or italic file: let PDFsharp fake it from the regular one.
            if (File.Exists(Path.Combine(Dir, f[0]))) return new FontResolverInfo(f[0], bold, italic);
        }
        return null;
    }

    public byte[]? GetFont(string faceName) => File.ReadAllBytes(Path.Combine(Dir, faceName));
}
