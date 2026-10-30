using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

using static Chomp.Loc;

namespace Chomp;

/// <summary>Bottom-of-screen toast: progress, result, size saved, Show / Undo. For when the wheel has already gone.
/// Like HUD.swift.</summary>
public sealed class Toast : Window
{
    public static readonly Toast Shared = new();

    public enum Phase { Working, Done, Failed }

    const double W = 500, H = 96;
    Phase phase;
    string title = "", detail = "";
    List<string> outputs = new();
    bool hovering;
    int token;

    readonly Grid root = new() { Width = W, Height = H };
    readonly Image mimic = new();
    readonly DispatcherTimer anim = new() { Interval = TimeSpan.FromMilliseconds(40) };
    DispatcherTimer? hideTimer;
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    Toast()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Width = W;
        Height = H;
        UseLayoutRounding = true;
        Title = "Chomp";
        Content = root;
        RenderOptions.SetBitmapScalingMode(mimic, BitmapScalingMode.NearestNeighbor);

        anim.Tick += (_, _) => Animate();
        SourceInitialized += (_, _) => Native.NoActivate(new WindowInteropHelper(this).Handle, true);
        MouseEnter += (_, _) => hovering = true;
        MouseLeave += (_, _) => hovering = false;
        Prefs.Changed += () => { if (IsVisible) Render(); };
    }

    public void Show(Phase phase, string title, string detail, IReadOnlyList<string>? outputs = null)
    {
        this.phase = phase;
        this.title = title;
        this.detail = detail;
        this.outputs = outputs?.ToList() ?? new();
        token++;
        Render();

        // Bottom centre of the screen the cursor is on, just above the taskbar.
        var (work, scale) = Native.MonitorAt(Native.Cursor());
        int w = (int)Math.Round(W * scale), h = (int)Math.Round(H * scale);
        var x = (work.Left + work.Right - w) / 2;
        var y = work.Bottom - h - (int)Math.Round(28 * scale);
        Opacity = 1;
        if (!IsVisible) Show();
        Native.Place(new WindowInteropHelper(this).Handle, x, y, w, h);
        anim.Start();

        switch (phase)
        {
            case Phase.Working: hideTimer?.Stop(); break;
            case Phase.Done: ScheduleHide(4.5); break;
            default: ScheduleHide(7); break;
        }
    }

    void ScheduleHide(double secs)
    {
        hideTimer?.Stop();
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(secs) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (hovering) ScheduleHide(1.5); else HideNow();
        };
        hideTimer = t;
        t.Start();
    }

    void HideNow()
    {
        var t = token;
        Opacity = 0.5;
        var fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        fade.Tick += (_, _) =>
        {
            fade.Stop();
            if (token != t) return;
            Hide();
            anim.Stop();
        };
        fade.Start();
    }

    void Reveal()
    {
        if (outputs.Count > 0) Native.Reveal(outputs[0]);
        HideNow();
    }

    void Undo()
    {
        var o = outputs;
        if (o.Count == 0) return;
        Native.Recycle(o);
        History.Remove(o);
        Show(Phase.Failed, L("Spat it out"), L(o.Count == 1 ? "Moved {0} file to the Recycle Bin" : "Moved {0} files to the Recycle Bin", o.Count));
    }

    Mood Mood_() => phase switch { Phase.Working => Mood.Working, Phase.Done => Mood.Happy, _ => Mood.Closed };

    void Animate() =>
        PixelUI.Set(mimic, Mimic.Draw(Mimic.At(Mood_(), Clock.Elapsed.TotalSeconds, 0, 0), Skin.Current).ToBitmap(), 2);

    void Render()
    {
        var k = Skin.Current;
        if (mimic.Parent is Panel old) old.Children.Remove(mimic);
        root.Children.Clear();

        var row = new DockPanel { Margin = new Thickness(16, 0, 16, 0), LastChildFill = true };
        mimic.VerticalAlignment = VerticalAlignment.Center;
        mimic.Margin = new Thickness(0, 2, 16, 0);
        DockPanel.SetDock(mimic, Dock.Left);
        row.Children.Add(mimic);

        if (phase == Phase.Done && outputs.Count > 0)
        {
            var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            chips.Children.Add(Chip(L("Show"), k, Reveal));
            chips.Children.Add(Chip(L("Undo"), k, Undo));
            DockPanel.SetDock(chips, Dock.Right);
            row.Children.Add(chips);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t = Fonts.Display(title, 19, phase == Phase.Failed ? Pal.Tongue : Pal.Tooth);
        t.TextAlignment = TextAlignment.Left;
        text.Children.Add(t);
        if (detail.Length > 0)
        {
            var d = Fonts.BodyText(detail, 13.5);
            d.TextAlignment = TextAlignment.Left;
            d.Margin = new Thickness(0, 6, 0, 0);
            text.Children.Add(d);
        }
        row.Children.Add(text);

        var box = PixelUI.Box(row, Pal.Panel, px: 3);
        box.Width = W - 8;
        box.Height = H - 10;
        box.VerticalAlignment = VerticalAlignment.Top;
        root.Children.Add(box);
        Animate();
    }

    static UIElement Chip(string text, Skin k, Action onClick)
    {
        var holder = new Grid { Margin = new Thickness(6, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
        void Paint(bool on)
        {
            holder.Children.Clear();
            holder.Children.Add(PixelUI.Keycap(text, on, k, 76, 30));
        }
        Paint(false);
        holder.MouseEnter += (_, _) => Paint(true);
        holder.MouseLeave += (_, _) => Paint(false);
        holder.MouseLeftButtonUp += (_, _) => onClick();
        return holder;
    }
}
