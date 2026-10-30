using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

using static Chomp.Loc;

namespace Chomp;

/// <summary>The settings that don't fit in the tray panel: size limit, watched folder, clipboard, updates and which
/// slices the wheel shows. Like SettingsWindow.swift.</summary>
public sealed class SettingsWindow : Window
{
    static SettingsWindow? open;

    public static void ShowIt()
    {
        open ??= new SettingsWindow();
        open.Show();
        open.Activate();
    }

    readonly StackPanel body = new() { Margin = new Thickness(24) };
    Kind kind = Kind.Image;

    static readonly (Kind Kind, string Name)[] Kinds =
    {
        (Kind.Image, L("Images")), (Kind.Video, L("Videos")), (Kind.Audio, L("Audio")), (Kind.Pdf, L("PDFs")), (Kind.Document, L("Docs")), (Kind.Other, L("Actions")),
    };

    SettingsWindow()
    {
        Title = L("Chomp settings");
        Width = 540;
        Height = 680;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Pal.B(Pal.Panel);
        UseLayoutRounding = true;
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Closed += (_, _) => open = null;
        Prefs.Changed += () => { if (IsVisible) Build(); };
        Build();
    }

    void Build()
    {
        var k = Skin.Current;
        var p = Prefs.Current;
        body.Children.Clear();

        Section("Size limit", "What the “fit” slices aim under. Discord allows 10 MB, most email 25 MB.", k);
        var sizes = Row();
        foreach (var mb in Prefs.SizeLimits)
            sizes.Children.Add(Key($"{mb} MB", p.Limit == mb, 90, k, () => { p.SizeLimit = mb; p.Save(); }));

        Section("Watch a folder", "New files that land in it get chewed automatically. The originals stay.", k);
        var folder = Row();
        folder.Children.Add(Key(p.WatchFolder.Length == 0 ? L("Choose folder…") : L("Change…"), false, 150, k, ChooseFolder));
        if (p.WatchFolder.Length > 0)
        {
            folder.Children.Add(Key(L("Stop"), false, 80, k, () =>
            {
                p.WatchFolder = "";
                p.Save();
                FolderWatcher.Shared.Restart();
            }));
            body.Children.Add(Left(Fonts.BodyText(p.WatchFolder, 12), new Thickness(0, 8, 0, 6)));
            foreach (var rule in WatchRule.All)
            {
                var row = Row();
                row.Margin = new Thickness(0, 4, 0, 0);
                row.Children.Add(Key(rule.Title, WatchRule.Current == rule, 280, k, () => { p.WatchRule = rule.Id; p.Save(); }));
            }
        }

        Section("Clipboard", "Copy a big screenshot and Chomp swaps it for a much smaller JPG, ready to paste into chats.", k);
        Toggle("Shrink copied pictures", p.ShrinkClipboard, v => { p.ShrinkClipboard = v; p.Save(); }, k);

        Section("Updates", "Asks GitHub once a day if there's a newer Chomp. Nothing about you or your files is sent.", k);
        Toggle("Check for updates", p.CheckUpdates, v => { p.CheckUpdates = v; p.Save(); _ = Updates.Check(); }, k);

        Section("Wheel slices", "Switch off the slices you never use.", k);
        var tabs = new WrapPanel();
        body.Children.Add(tabs);
        foreach (var (kd, name) in Kinds)
        {
            var key = Key(name, kind == kd, 76, k, () => { kind = kd; Build(); });
            key.Margin = new Thickness(0, 0, 6, 6);
            tabs.Children.Add(key);
        }
        foreach (var m in kind == Kind.Other ? new[] { Mode.Actions } : new[] { Mode.Convert, Mode.Tools })
        {
            var ops = Catalog.AllOps(m, kind);
            if (ops.Count == 0) continue;
            body.Children.Add(Left(Fonts.Label(m.Title(), 9, Pal.Muted), new Thickness(0, 10, 0, 6)));
            var grid = new WrapPanel();
            body.Children.Add(grid);
            foreach (var op in ops)
            {
                var sk = Catalog.SliceKey(op, m, kind);
                var key = Key(op.Title, !p.HiddenSlices.Contains(sk), 150, k, () =>
                {
                    if (!p.HiddenSlices.Remove(sk)) p.HiddenSlices.Add(sk);
                    p.Save();
                });
                key.Margin = new Thickness(0, 0, 8, 8);
                grid.Children.Add(key);
            }
        }
    }

    void Section(string title, string note, Skin k)
    {
        body.Children.Add(Left(Fonts.Display(L(title), 18, k.Accent), new Thickness(0, body.Children.Count == 0 ? 0 : 22, 0, 4)));
        var n = Left(Fonts.BodyText(L(note), 12), new Thickness(0, 0, 0, 8));
        n.TextWrapping = TextWrapping.Wrap;
        n.TextTrimming = TextTrimming.None;
        body.Children.Add(n);
    }

    WrapPanel Row()
    {
        var r = new WrapPanel();
        body.Children.Add(r);
        return r;
    }

    static TextBlock Left(TextBlock t, Thickness m)
    {
        t.TextAlignment = TextAlignment.Left;
        t.Margin = m;
        return t;
    }

    static Grid Key(string text, bool on, double w, Skin k, Action click)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
        g.Children.Add(PixelUI.Keycap(text, on, k, w, 28));
        g.MouseLeftButtonUp += (_, _) => click();
        return g;
    }

    void Toggle(string title, bool value, Action<bool> set, Skin k)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var key = Key(value ? L("On") : L("Off"), value, 52, k, () => set(!value));
        key.Margin = new Thickness(0);
        DockPanel.SetDock(key, Dock.Right);
        row.Children.Add(key);
        var label = Left(Fonts.Display(L(title), 15), new Thickness(0));
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        body.Children.Add(row);
    }

    void ChooseFolder()
    {
        var d = new OpenFolderDialog { Title = L("Chomp will chew new files that land in this folder.") };
        if (Prefs.Current.WatchFolder.Length > 0) d.InitialDirectory = Prefs.Current.WatchFolder;
        if (d.ShowDialog(this) != true) return;
        Prefs.Current.WatchFolder = d.FolderName;
        Prefs.Current.Save();
        FolderWatcher.Shared.Restart();
    }
}
