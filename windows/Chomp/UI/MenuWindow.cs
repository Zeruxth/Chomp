using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Chomp;

/// <summary>The tray panel: a place to drop files, recent results, and settings. Like MenuPanel.swift.</summary>
public sealed class MenuWindow : Window
{
    public static readonly MenuWindow Shared = new();

    const double PanelW = 340;
    readonly StackPanel body = new() { Margin = new Thickness(16) };

    MenuWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = PanelW;
        SizeToContent = SizeToContent.Height;
        Background = Pal.B(Pal.Disc);
        BorderBrush = Pal.B(Pal.Ink);
        BorderThickness = new Thickness(2);
        UseLayoutRounding = true;
        Title = "Chomp";
        Content = body;

        Deactivated += (_, _) => Hide();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
        Prefs.Changed += () => { if (IsVisible) Build(); };
        History.Items.CollectionChanged += (_, _) => { if (IsVisible) Build(); };
    }

    /// <summary>Opens just above the tray, at the bottom right of the screen the cursor is on.</summary>
    public void Toggle()
    {
        if (IsVisible) { Hide(); return; }
        Build();
        Show();
        UpdateLayout();
        var c = Native.Cursor();
        var (work, scale) = Native.MonitorAt(c);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int w = (int)Math.Round(ActualWidth * scale), h = (int)Math.Round(ActualHeight * scale), gap = (int)(12 * scale);
        Native.Place(hwnd, work.Right - w - gap, work.Bottom - h - gap, w, h);
        Activate();
    }

    void Build()
    {
        var k = Skin.Current;
        body.Children.Clear();

        // Header
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var portrait = PixelUI.Art(Mimic.Draw(Mimic.Portrait, k).ToBitmap(), 2);
        portrait.Margin = new Thickness(0, 0, 10, 0);
        head.Children.Add(portrait);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Fonts.Display("Chomp", 22, k.Accent, bold: true);
        name.TextAlignment = TextAlignment.Left;
        titles.Children.Add(name);
        var tag = Fonts.BodyText("Drop a file on me. I'll eat it.", 11);
        tag.TextAlignment = TextAlignment.Left;
        titles.Children.Add(tag);
        head.Children.Add(titles);
        body.Children.Add(head);

        body.Children.Add(DropZone(k));

        if (Bin.FFmpeg == null) body.Children.Add(FFmpegNote(k));
        if (History.Items.Count > 0) body.Children.Add(Recent());

        body.Children.Add(Divider());
        Settings(k);
        body.Children.Add(Divider());

        var foot = new DockPanel();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        var ver = Fonts.BodyText($"v{v?.ToString(3)}", 11);
        foot.Children.Add(ver);
        var quit = Fonts.Label("Quit", 9, k.Accent);
        quit.Cursor = Cursors.Hand;
        quit.HorizontalAlignment = HorizontalAlignment.Right;
        quit.MouseLeftButtonUp += (_, _) => Application.Current.Shutdown();
        DockPanel.SetDock(quit, Dock.Right);
        foot.Children.Insert(0, quit);
        body.Children.Add(foot);
    }

    static Border Divider() => new() { Height = 2, Background = Pal.B(Pal.SegmentRim), Margin = new Thickness(0, 14, 0, 14) };

    static TextBlock AlignLeft(TextBlock t) { t.TextAlignment = TextAlignment.Left; return t; }

    UIElement DropZone(Skin k)
    {
        var s = new StackPanel { Margin = new Thickness(12, 18, 12, 18) };
        s.Children.Add(Fonts.Display("Drop files here", 16));
        var sub = Fonts.BodyText("or click to choose", 11);
        sub.Margin = new Thickness(0, 4, 0, 0);
        s.Children.Add(sub);
        var box = PixelUI.Box(s, Pal.Segment);
        box.AllowDrop = true;
        box.Cursor = Cursors.Hand;
        box.Background = Brushes.Transparent;

        // Dragging over the zone pops the wheel up under the cursor; it takes the drop from there.
        box.DragEnter += (_, e) =>
        {
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                WheelWindow.Shared.Open(files, Mode.Convert, interactive: false);
        };
        box.Drop += (_, e) =>
        {
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                WheelWindow.Shared.Open(files, Mode.Convert, interactive: true);
        };
        box.MouseLeftButtonUp += (_, _) => Pick();
        return box;
    }

    void Pick()
    {
        var d = new OpenFileDialog { Multiselect = true, Title = "Feed Chomp" };
        Hide();
        if (d.ShowDialog() == true && d.FileNames.Length > 0)
            WheelWindow.Shared.Open(d.FileNames, Mode.Convert, interactive: true);
    }

    UIElement FFmpegNote(Skin k)
    {
        var s = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        s.Children.Add(AlignLeft(Fonts.BodyText("Video, audio and AVIF need ffmpeg", 11, Pal.Tooth)));
        var copy = AlignLeft(Fonts.Label("Copy: winget install ffmpeg", 9, k.Accent));
        copy.Cursor = Cursors.Hand;
        copy.Margin = new Thickness(0, 4, 0, 0);
        copy.MouseLeftButtonUp += (_, _) => Clipboard.SetText("winget install ffmpeg");
        s.Children.Add(copy);
        return s;
    }

    UIElement Recent()
    {
        var s = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        s.Children.Add(AlignLeft(Fonts.Label("Recent", 9, Pal.Muted)));
        foreach (var item in History.Items.Take(5))
        {
            var row = new StackPanel { Margin = new Thickness(0, 6, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "Show in Explorer" };
            row.Children.Add(AlignLeft(Fonts.BodyText(item.Title, 12, Pal.Tooth)));
            row.Children.Add(AlignLeft(Fonts.BodyText(item.Detail, 10)));
            row.MouseLeftButtonUp += (_, _) => { if (item.Outputs.Count > 0 && File.Exists(item.Outputs[0])) Native.Reveal(item.Outputs[0]); };
            s.Children.Add(row);
        }
        return s;
    }

    void Settings(Skin k)
    {
        var p = Prefs.Current;

        // Save to
        body.Children.Add(AlignLeft(Fonts.Display("Save to", 14)));
        var where = new UniformGrid3();
        foreach (var (title, v) in new[] { ("Beside", SaveTo.Beside), ("Downloads", SaveTo.Downloads), ("Folder…", SaveTo.Custom) })
        {
            var key = PixelUI.Keycap(title, p.SaveTo == v, k, 96, 26);
            key.Cursor = Cursors.Hand;
            key.MouseLeftButtonUp += (_, _) =>
            {
                if (v == SaveTo.Custom)
                {
                    var d = new OpenFolderDialog { Title = "Chomp will save everything it makes here" };
                    if (p.CustomFolder.Length > 0) d.InitialDirectory = p.CustomFolder;
                    if (d.ShowDialog() != true) return;
                    p.CustomFolder = d.FolderName;
                }
                p.SaveTo = v;
                p.Save();
            };
            where.Add(key);
        }
        body.Children.Add(where.Panel);
        var hint = p.SaveTo switch
        {
            SaveTo.Custom => p.CustomFolder,
            SaveTo.Downloads => @"Downloads\Chomp",
            _ => "Next to the original",
        };
        var hintText = AlignLeft(Fonts.BodyText(hint, 11));
        hintText.Margin = new Thickness(0, 6, 0, 12);
        body.Children.Add(hintText);

        // Quality
        var q = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var qLabel = AlignLeft(Fonts.Display($"Quality {(int)Math.Round(p.Quality * 100)}%", 14));
        qLabel.Width = 110;
        q.Children.Add(qLabel);
        var slider = new Slider { Minimum = 0.5, Maximum = 1, Value = p.Quality, TickFrequency = 0.05, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) => qLabel.Text = $"Quality {(int)Math.Round(e.NewValue * 100)}%";
        slider.PreviewMouseLeftButtonUp += (_, _) => { p.Quality = slider.Value; p.Save(); };
        slider.LostKeyboardFocus += (_, _) => { if (p.Quality != slider.Value) { p.Quality = slider.Value; p.Save(); } };
        q.Children.Add(slider);
        body.Children.Add(q);

        // Skins
        body.Children.Add(AlignLeft(Fonts.Display("Chest skin", 14)));
        var grid = new UniformGrid3();
        foreach (var s in Skin.All)
        {
            var on = k == s;
            var cell = new StackPanel { Margin = new Thickness(0, 7, 0, 7) };
            var pic = PixelUI.Art(Mimic.Draw(Mimic.Portrait, s).ToBitmap(), 1);
            pic.Opacity = s.Locked ? 0.35 : 1;
            cell.Children.Add(pic);
            cell.Children.Add(Fonts.Label(s.Title, 8));
            cell.Children.Add(Fonts.BodyText(s.Locked ? $"{p.Chomps}/{Skin.RoyalAt}" : s.Note, 9));
            var b = new Border
            {
                Child = cell, Width = 96, Cursor = Cursors.Hand,
                Background = on ? Pal.B(s.Accent, 46) : Pal.B(Pal.Segment),
                BorderBrush = Pal.B(on ? s.Accent : Pal.Ink), BorderThickness = new Thickness(2),
                ToolTip = s.Locked ? $"Unlocks after {Skin.RoyalAt} chomps" : s.Title,
            };
            b.MouseLeftButtonUp += (_, _) =>
            {
                if (s.Locked) return;
                p.Skin = s.Id;
                p.Save();
            };
            grid.Add(b);
        }
        body.Children.Add(grid.Panel);

        Toggle("Pixel text", p.PixelText, v => { p.PixelText = v; p.Save(); }, k);
        Toggle("Sounds", p.Sounds, v => { p.Sounds = v; p.Save(); }, k);
        Toggle("Start with Windows", Integration.StartsWithWindows, v => { Integration.StartsWithWindows = v; Build(); }, k);
        Toggle("In Send to menu", Integration.InSendTo, v => { Integration.InSendTo = v; Build(); }, k);
    }

    void Toggle(string title, bool value, Action<bool> set, Skin k)
    {
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var key = PixelUI.Keycap(value ? "On" : "Off", value, k, 52, 24);
        key.Cursor = Cursors.Hand;
        key.MouseLeftButtonUp += (_, _) => set(!value);
        DockPanel.SetDock(key, Dock.Right);
        row.Children.Add(key);
        var label = AlignLeft(Fonts.Display(title, 14));
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        body.Children.Add(row);
    }

    /// <summary>Three equal columns with a small gap.</summary>
    sealed class UniformGrid3
    {
        public readonly WrapPanel Panel = new() { Margin = new Thickness(0, 8, 0, 0) };
        int n;

        public void Add(FrameworkElement e)
        {
            e.Margin = new Thickness(n % 3 == 0 ? 0 : 8, n >= 3 ? 8 : 0, 0, 0);
            n++;
            Panel.Children.Add(e);
        }
    }
}

/// <summary>Bits of Windows Chomp hooks into: starting at sign-in and the Send to menu.</summary>
public static class Integration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Chomp.exe");

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Chomp") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("Chomp", $"\"{Exe}\"");
            else key.DeleteValue("Chomp", false);
        }
    }

    static string SendToLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "Chomp.lnk");

    /// <summary>Right-click a file → Send to → Chomp opens the wheel for it.</summary>
    public static bool InSendTo
    {
        get => File.Exists(SendToLink);
        set
        {
            if (!value) { Files.TryDelete(SendToLink); return; }
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return;
            dynamic shell = Activator.CreateInstance(t)!;
            var link = shell.CreateShortcut(SendToLink);
            link.TargetPath = Exe;
            link.IconLocation = Exe + ",0";
            link.Description = "Feed it to Chomp";
            link.Save();
        }
    }
}
