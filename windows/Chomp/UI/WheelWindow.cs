using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Chomp;

/// <summary>Geometry shared by drawing and hit-testing, in device-independent pixels (y down). Same numbers as Wheel.swift.</summary>
static class Geo
{
    public const double Size = WheelArt.Grid * WheelArt.Scale;   // 440
    public const double PillsH = 58;
    public const double Outer = WheelArt.OuterR * WheelArt.Scale;
    public const double Inner = WheelArt.InnerR * WheelArt.Scale;
    public const double PillW = 112, PillH = 30, PillGap = 14;
    public const double W = Size, H = Size + PillsH;
    public static readonly Point Center = new(Size / 2, Size / 2);

    public static Rect Pill(int i)
    {
        const double total = PillW * 3 + PillGap * 2;
        var x0 = (Size - total) / 2;
        return new Rect(x0 + i * (PillW + PillGap), H - (PillsH - PillH) / 2 + 2 - PillH, PillW, PillH);
    }
}

/// <summary>The wheel: a borderless, see-through window that opens under the cursor. Drop or click on a slice
/// and Chomp gets to work right there, then shows the result with Show / Undo / Close.</summary>
public sealed class WheelWindow : Window
{
    public static readonly WheelWindow Shared = new();

    enum Stage { Pick, Eating, Working, Done, Failed, Spat }
    enum ZoneKind { Segment, Center, Pill, None }
    readonly record struct Zone(ZoneKind Kind, int Index = 0);

    /// <summary>Called with the choice, the files and a job number.</summary>
    public Action<Op, IReadOnlyList<string>, int>? OnRun;

    // Model
    Mode mode = Mode.Convert;
    List<string> files = new();
    Kind kind = Kind.Other;
    int count;
    List<Op> ops = new();
    int? hover;
    int? pillHover;
    bool interactive;
    bool dragging;
    double? chompAt;
    Stage stage = Stage.Pick;
    string jobTitle = "", jobDetail = "";
    List<string> outputs = new();
    int jobID, jobs, food, token;

    // Views
    readonly Grid root = new() { Width = Geo.W, Height = Geo.H, Background = Brushes.Transparent };
    readonly Canvas layer = new() { Width = Geo.W, Height = Geo.H };
    readonly ScaleTransform pop = new(1, 1, Geo.Center.X, Geo.Center.Y);
    readonly Image mimic = new();
    readonly Image ring = new();
    readonly DispatcherTimer anim = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly DispatcherTimer watch = new() { Interval = TimeSpan.FromMilliseconds(25) };
    DispatcherTimer? hideTimer;
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static double Now => Clock.Elapsed.TotalSeconds;

    WheelWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.Manual;
        Width = Geo.W;
        Height = Geo.H;
        UseLayoutRounding = true;
        AllowDrop = true;
        Title = "Chomp";

        RenderOptions.SetBitmapScalingMode(mimic, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(ring, BitmapScalingMode.NearestNeighbor);
        root.RenderTransform = pop;
        root.Children.Add(layer);
        Content = root;

        anim.Tick += (_, _) => Animate();
        watch.Tick += (_, _) => WatchButton();
        Prefs.Changed += () => { if (IsVisible) Render(); };

        SourceInitialized += (_, _) => Native.NoActivate(new WindowInteropHelper(this).Handle, true);
        Deactivated += (_, _) => { if (interactive) Close_(); };

        MouseMove += (_, e) =>
        {
            if (interactive || stage != Stage.Pick) Apply(ZoneAt(e.GetPosition(root)), dragging: false);
        };
        MouseLeave += (_, _) =>
        {
            if (interactive) SetHover(null);
            if (pillHover != null) { pillHover = null; Render(); }
        };
        MouseLeftButtonDown += (_, e) => Click(ZoneAt(e.GetPosition(root)));
        KeyDown += (_, e) => e.Handled = Key_(e.Key);

        DragEnter += (_, e) => DragUpdate(e);
        DragOver += (_, e) => DragUpdate(e);
        DragLeave += (_, _) => SetHover(null);
        Drop += (_, e) =>
        {
            e.Handled = true;
            if (stage != Stage.Pick) return;
            var z = ZoneAt(e.GetPosition(root));
            if (z.Kind != ZoneKind.Segment) return;
            var dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
            Fire(z.Index, dropped);
        };
    }

    // MARK: Showing and hiding

    /// <summary>interactive: opened by click, Send To or the panel's file picker, so it takes keys and clicks.
    /// Otherwise it's mid-drag: it waits for a drop and goes away when the button comes up anywhere else.</summary>
    public void Open(IReadOnlyList<string> items, Mode m, bool interactive)
    {
        if (items.Count == 0) return;
        Present(items, m, interactive);
    }

    /// <summary>Shift, Ctrl or Alt was tapped mid-drag somewhere in Windows. If the wheel is already up for this drag it
    /// switches mode; otherwise it pops up empty under the cursor, and the drag fills it in as soon as it crosses into the
    /// window (Windows only says what's being dragged to the window it's over).</summary>
    public void Trigger(Mode m)
    {
        if (IsVisible && !interactive && stage == Stage.Pick)
        {
            if (files.Count > 0) SwitchMode(m);
            else { mode = m; Render(); }
            return;
        }
        if (IsVisible && interactive) return;   // the wheel is busy with a click-opened pick
        Present(Array.Empty<string>(), m, interactive: false);
        // Nothing crossed in: it wasn't a drag Windows could hand over (a window being moved, text being selected…).
        var t = token;
        After(1.2, () => { if (token == t && files.Count == 0 && IsVisible) Close_(); });
    }

    void Present(IReadOnlyList<string> items, Mode m, bool interactive)
    {
        hideTimer?.Stop();
        token++;
        this.interactive = interactive;
        dragging = !interactive;
        Load(items, m);

        // Centre the wheel on the cursor, kept inside the monitor's work area (physical pixels).
        var c = Native.Cursor();
        var (work, scale) = Native.MonitorAt(c);
        int w = (int)Math.Round(Geo.W * scale), h = (int)Math.Round(Geo.H * scale);
        var x = (int)Math.Round(c.X - Geo.Center.X * scale);
        var y = (int)Math.Round(c.Y - Geo.Center.Y * scale);
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - w));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - h));

        Opacity = 1;
        SetPop(0);
        Render();
        if (!IsVisible) Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.NoActivate(hwnd, !interactive);
        Native.Place(hwnd, x, y, w, h);
        if (interactive) { Activate(); Focus(); }
        anim.Start();
        if (dragging) watch.Start();

        // Two hard steps instead of a spring, like a sprite popping in.
        var t = token;
        After(0.0, () => { if (token == t) SetPop(1); });
        After(0.05, () => { if (token == t) SetPop(2); });
    }

    void SetPop(int s)
    {
        var k = s == 2 ? 1 : (s == 1 ? 0.94 : 0.8);
        pop.ScaleX = pop.ScaleY = k;
        root.Opacity = s == 0 ? 0 : 1;
    }

    public void Close_()
    {
        hideTimer?.Stop();
        if (!IsVisible) return;
        // A job still running reports to the tray instead.
        stage = Stage.Pick;
        token++;
        var t = token;
        SetPop(1);
        After(0.08, () =>
        {
            if (token != t) return;
            Hide();
            anim.Stop();
            watch.Stop();
            hover = null;
            dragging = false;
        });
    }

    /// <summary>Mid-drag, the button came up somewhere: give a drop on the wheel a moment to land, then go.</summary>
    void WatchButton()
    {
        if (Native.LeftButtonDown()) return;
        watch.Stop();
        dragging = false;
        After(0.18, () => { if (!interactive && chompAt == null) Close_(); });
    }

    static void After(double secs, Action a)
    {
        if (secs <= 0) { Application.Current.Dispatcher.InvokeAsync(a, DispatcherPriority.Render); return; }
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(secs) };
        t.Tick += (_, _) => { t.Stop(); a(); };
        t.Start();
    }

    // MARK: Model

    void Load(IReadOnlyList<string> items, Mode m)
    {
        chompAt = null;
        stage = Stage.Pick;
        outputs = new();
        pillHover = null;
        files = items.ToList();
        (kind, count) = Catalog.Summary(files);
        SetMode(m);
    }

    void SetMode(Mode m)
    {
        mode = m;
        ops = Catalog.Ops(m, kind);
        hover = null;
    }

    string Label_()
    {
        if (files.Count == 1 && Catalog.KindOf(files[0]) != Kind.Folder) return Path.GetFileName(files[0]);
        var n = mode == Mode.Actions ? files.Count : count;
        var noun = mode == Mode.Actions ? "item" : kind.Noun();
        return $"{n} {noun}{(n == 1 ? "" : "s")}";
    }

    /// <summary>Chomp follows the hovered slice with its eyes.</summary>
    Mood Mood_()
    {
        switch (stage)
        {
            case Stage.Eating: return Mood.Chomp;
            case Stage.Working: return Mood.Working;
            case Stage.Done: return Mood.Happy;
            case Stage.Failed: case Stage.Spat: return Mood.Closed;
        }
        if (hover is not int h) return Mood.Idle;
        if (h < 0) return Mood.Closed;
        var s = Math.Sin(2 * Math.PI * h / Math.Max(ops.Count, 1));
        return s > 0.35 ? Mood.PeekRight : (s < -0.35 ? Mood.PeekLeft : Mood.Peek);
    }

    void SetHover(int? i)
    {
        if (hover == i || chompAt != null) return;
        hover = i;
        Render();
    }

    void SwitchMode(Mode m)
    {
        if (mode == m) return;
        SetMode(m);
        Render();
    }

    // MARK: Jobs

    void Fire(int index, IReadOnlyList<string>? dropped = null)
    {
        if (index < 0 || index >= ops.Count) return;
        var op = ops[index];
        var f = dropped is { Count: > 0 } ? dropped : files;
        // The wheel stays: Chomp bites, chews while the job runs, then shows the result in the ring.
        hover = null;
        chompAt = Now;
        food = Food.Random();
        stage = Stage.Eating;
        jobTitle = jobDetail = "";
        outputs = new();
        jobID = ++jobs;
        var t = token;
        Render();
        After(Mimic.ChompLength, () => { if (token == t && stage == Stage.Eating) { stage = Stage.Working; Render(); } });
        OnRun?.Invoke(op, f, jobs);
    }

    bool Showing(int job) => IsVisible && jobID == job && stage is Stage.Eating or Stage.Working;

    /// <summary>False when the wheel isn't showing this job any more (closed, or a new drop took over).</summary>
    public bool JobStarted(int job, string title, string detail)
    {
        if (!Showing(job)) return false;
        jobTitle = title;
        jobDetail = detail;
        Render();
        return true;
    }

    public bool JobFinished(int job, bool ok, string title, string detail, IReadOnlyList<string> outs)
    {
        if (!Showing(job)) return false;
        // Let the bite finish before the result lands.
        var t = token;
        After(Math.Max(0, Mimic.ChompLength - (Now - (chompAt ?? 0))), () =>
        {
            if (token != t) return;
            stage = ok ? Stage.Done : Stage.Failed;
            jobTitle = title;
            jobDetail = detail;
            outputs = outs.ToList();
            Render();
            ScheduleHide(ok ? 3 : 6);
        });
        return true;
    }

    /// <summary>Fades out by itself; hovering one of the keys under the wheel holds it open.</summary>
    void ScheduleHide(double secs)
    {
        hideTimer?.Stop();
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(secs) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (pillHover != null) ScheduleHide(1.5); else Close_();
        };
        hideTimer = t;
        t.Start();
    }

    /// <summary>The keys under the wheel once it shows a job: 0 Show, 1 Undo, 2 Close (Hide while working).</summary>
    void ResultAction(int i)
    {
        switch (i)
        {
            case 0:
                if (outputs.Count == 0) return;
                Native.Reveal(outputs[0]);
                Close_();
                break;
            case 1:
            {
                var o = outputs;
                if (o.Count == 0) return;
                Native.Recycle(o);
                History.Remove(o);
                outputs = new();
                stage = Stage.Spat;
                jobTitle = "Spat it out";
                jobDetail = $"Moved {o.Count} file{(o.Count == 1 ? "" : "s")} to the Recycle Bin";
                Render();
                ScheduleHide(2.5);
                break;
            }
            default:
                Close_();
                break;
        }
    }

    // MARK: Input

    Zone ZoneAt(Point p)
    {
        if (p.Y > Geo.Size)
        {
            for (var i = 0; i < 3; i++)
            {
                var r = Geo.Pill(i);
                r.Inflate(3, 6);
                if (r.Contains(p)) return new Zone(ZoneKind.Pill, i);
            }
            return new Zone(ZoneKind.None);
        }
        double dx = p.X - Geo.Center.X, dy = p.Y - Geo.Center.Y;
        var d = Math.Sqrt(dx * dx + dy * dy);
        if (d < Geo.Inner) return new Zone(ZoneKind.Center);
        if (d > Geo.Outer + 10 || ops.Count == 0) return new Zone(ZoneKind.None);
        var a = Math.Atan2(dx, -dy);                 // 0 = up, clockwise
        if (a < 0) a += 2 * Math.PI;
        var w = 2 * Math.PI / ops.Count;
        return new Zone(ZoneKind.Segment, (int)(((a + w / 2) % (2 * Math.PI)) / w) % ops.Count);
    }

    void Apply(Zone z, bool dragging)
    {
        if (stage != Stage.Pick)
        {
            int? h = z.Kind == ZoneKind.Pill ? z.Index : null;
            if (pillHover != h) { pillHover = h; Render(); }
            return;
        }
        switch (z.Kind)
        {
            case ZoneKind.Segment: SetHover(z.Index); break;
            case ZoneKind.Center: SetHover(-1); break;
            case ZoneKind.Pill:
                SetHover(null);
                if (dragging) SwitchMode((Mode)z.Index);   // hover a key mid-drag to switch
                break;
            default: SetHover(null); break;
        }
    }

    void Click(Zone z)
    {
        if (stage != Stage.Pick)
        {
            if (z.Kind == ZoneKind.Pill) ResultAction(z.Index);
            else if (z.Kind == ZoneKind.Center) Close_();
            return;
        }
        if (chompAt != null) return;
        switch (z.Kind)
        {
            case ZoneKind.Segment: Fire(z.Index); break;
            case ZoneKind.Pill: SwitchMode((Mode)z.Index); break;
            default: Close_(); break;
        }
    }

    void DragUpdate(DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (stage == Stage.Pick && files.Count == 0)
        {
            // Opened by a key mid-drag: now we know what's being carried.
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } got) { Load(got, mode); Render(); }
            else { Close_(); return; }
        }
        if (stage != Stage.Pick || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var z = ZoneAt(e.GetPosition(root));
        Apply(z, dragging: true);
        if (z.Kind == ZoneKind.Segment)
            e.Effects = (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : e.AllowedEffects & (DragDropEffects.Link | DragDropEffects.Move);
    }

    /// <summary>Keyboard, when the wheel was opened by click.</summary>
    bool Key_(Key k)
    {
        if (stage != Stage.Pick)
        {
            if (k == Key.Escape) Close_();
            return true;
        }
        switch (k)
        {
            case Key.Escape: Close_(); return true;
            case Key.Tab: SwitchMode(mode.Next()); return true;
            case Key.Left:
            case Key.Right:
            {
                var n = ops.Count;
                if (n == 0) return true;
                var right = k == Key.Right;
                var cur = hover ?? (right ? -1 : 0);
                SetHover(right ? (cur + 1 + n) % n : (cur - 1 + n) % n);
                return true;
            }
            case Key.Enter:
                if (hover is int h && h >= 0) Fire(h);
                return true;
        }
        var digit = k >= Key.D1 && k <= Key.D9 ? k - Key.D1 + 1 : (k >= Key.NumPad1 && k <= Key.NumPad9 ? k - Key.NumPad1 + 1 : 0);
        if (digit >= 1 && digit <= ops.Count) { Fire(digit - 1); return true; }
        return false;
    }

    // MARK: Drawing

    void Animate()
    {
        var k = Skin.Current;
        PixelUI.Set(mimic, Mimic.Draw(Mimic.At(Mood_(), Now, chompAt ?? 0, food), k).ToBitmap(), stage == Stage.Pick ? 2 : 4);
        if (stage is Stage.Eating or Stage.Working)
            PixelUI.Set(ring, WheelArt.Compact(WheelArt.Ring.Spinning((int)(Now / 0.08) % WheelArt.SpinSteps), k), WheelArt.Scale);
    }

    static void Put(Canvas c, UIElement e, double cx, double cy, double w, double h)
    {
        if (e is FrameworkElement f) { f.Width = w; f.Height = h; }
        Canvas.SetLeft(e, cx - w / 2);
        Canvas.SetTop(e, cy - h / 2);
        c.Children.Add(e);
    }

    /// <summary>Snapped to the 2-pixel wheel grid so icons line up with the disc's pixels.</summary>
    static Point PointAt(double a, double r)
    {
        static double Snap(double v) => Math.Round(v / WheelArt.Scale) * WheelArt.Scale;
        return new Point(Snap(Geo.Size / 2 + r * Math.Sin(a)), Snap(Geo.Size / 2 - r * Math.Cos(a)));
    }

    void Render()
    {
        var k = Skin.Current;
        // Chomp and the ring are reused across redraws; free them from last time's parents first.
        foreach (var e in new UIElement[] { mimic, ring })
            if (VisualTreeHelper.GetParent(e) is Panel p) p.Children.Remove(e);
        layer.Children.Clear();
        // Nearly invisible, but it makes the whole wheel a drop target: a see-through window lets drags fall through
        // its fully transparent pixels to whatever is underneath.
        if (stage == Stage.Pick)
        {
            var hit = Geo.Outer * 2 + 20;
            Put(layer, new System.Windows.Shapes.Ellipse { Fill = Pal.B(0x000000, 1) }, Geo.Center.X, Geo.Center.Y, hit, hit);
        }
        if (stage == Stage.Pick) RenderPick(k); else RenderCompact(k);
        RenderPills(k);
        Animate();
    }

    void RenderPick(Skin k)
    {
        layer.Children.Add(PixelUI.Art(WheelArt.Image(ops.Count, hover, k), WheelArt.Scale));

        var n = ops.Count;
        var w = n > 0 ? 2 * Math.PI / n : 0;
        var r = (Geo.Inner + Geo.Outer) / 2 + 4;
        for (var i = 0; i < n; i++)
        {
            var p = PointAt(i * w, r);
            var width = Math.Min(104, w * r * 0.86);
            Put(layer, SegmentLabel(ops[i], hover == i, i + 1, width), p.X, p.Y + (ops[i].Icon == null ? 0 : -4), width, 120);
        }

        // Centre: Chomp and a caption.
        var caption = files.Count == 0 ? "sniff sniff" : (hover == -1 ? "nevermind" : (ops.Count == 0 ? "can't eat that" : Label_()));
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        mimic.Margin = new Thickness(0, 2, 0, 0);
        mimic.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(mimic);
        var cap = Fonts.Label(caption, 8, hover == -1 ? k.Accent : Pal.Tooth);
        cap.MaxWidth = Geo.Inner * 2 - 40;
        cap.Margin = new Thickness(0, 2, 0, 0);
        stack.Children.Add(cap);
        var holder = new Grid();
        holder.Children.Add(stack);
        Put(layer, holder, Geo.Center.X, Geo.Center.Y - 4, Geo.Inner * 2, Geo.Inner * 2);
    }

    UIElement SegmentLabel(Op op, bool hot, int index, double width)
    {
        var fg = hot ? Pal.Ink : Pal.Tooth;
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = width };
        if (op.Icon != null)
        {
            var icon = PixelUI.Art(PixelIcons.Draw(op.Icon, fg).ToBitmap(), 2);
            icon.Margin = new Thickness(0, 0, 0, 6);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            s.Children.Add(icon);
        }
        var title = Fonts.Display(op.Title, op.Icon == null ? 20 : 15, fg);
        s.Children.Add(Shrink(title, width));
        if (op.Detail.Length > 0)
        {
            var d = Fonts.BodyText(op.Detail, 10, fg);
            d.Opacity = hot ? 0.8 : 0.6;
            d.Margin = new Thickness(0, 4, 0, 0);
            s.Children.Add(Shrink(d, width));
        }
        if (interactive && index <= 9)
        {
            var num = Fonts.Label(index.ToString(), 8, fg);
            num.Opacity = 0.4;
            num.Margin = new Thickness(0, 3, 0, 0);
            s.Children.Add(num);
        }
        var g = new Grid();
        g.Children.Add(s);
        return g;
    }

    /// <summary>Scales a line down a little rather than cutting it off, like minimumScaleFactor.</summary>
    static UIElement Shrink(TextBlock t, double width) =>
        new Viewbox { Child = t, StretchDirection = StretchDirection.DownOnly, MaxWidth = width, HorizontalAlignment = HorizontalAlignment.Center };

    void RenderCompact(Skin k)
    {
        var d = WheelArt.CompactGrid * WheelArt.Scale;
        var r = stage switch
        {
            Stage.Done => WheelArt.Compact(WheelArt.Ring.Solid(k.Accent), k),
            Stage.Failed => WheelArt.Compact(WheelArt.Ring.Solid(Pal.Tongue), k),
            Stage.Spat => WheelArt.Compact(WheelArt.Ring.Solid(Pal.SegmentRim), k),
            _ => WheelArt.Compact(WheelArt.Ring.Spinning(0), k),
        };
        PixelUI.Set(ring, r, WheelArt.Scale);
        Put(layer, ring, Geo.Center.X, Geo.Center.Y, d, d);
        mimic.Margin = new Thickness(0);
        Put(layer, mimic, Geo.Center.X, Geo.Center.Y + 4, 32 * 4, 33 * 4);

        var text = new StackPanel { Margin = new Thickness(16, 10, 16, 10), VerticalAlignment = VerticalAlignment.Center };
        var title = Fonts.Display(jobTitle, 20, stage == Stage.Failed ? Pal.Tongue : Pal.Tooth);
        text.Children.Add(Shrink(title, 330));
        var detail = Fonts.BodyText(jobDetail, 14);
        detail.Margin = new Thickness(0, 6, 0, 0);
        text.Children.Add(detail);
        var box = PixelUI.Box(text, Pal.Panel);
        var bw = Geo.PillW * 3 + Geo.PillGap * 2;   // lines up with the keys below
        Put(layer, box, Geo.Center.X, Geo.Center.Y + d / 2.0 + 46, bw, 76);
    }

    void RenderPills(Skin k)
    {
        for (var i = 0; i < 3; i++)
        {
            var rect = Geo.Pill(i);
            Grid key;
            if (stage == Stage.Pick)
            {
                key = PixelUI.Keycap(((Mode)i).Title(), mode == (Mode)i, k, rect.Width, rect.Height);
            }
            else
            {
                // Same three keys become Show / Undo / Close.
                var busy = stage is Stage.Eating or Stage.Working;
                var enabled = i == 2 || outputs.Count > 0;
                var text = i == 0 ? "Show" : (i == 1 ? "Undo" : (busy ? "Hide" : "Close"));
                key = PixelUI.Keycap(text, enabled && pillHover == i, k, rect.Width, rect.Height, enabled ? 1 : 0.35);
            }
            Canvas.SetLeft(key, rect.X);
            Canvas.SetTop(key, rect.Y);
            layer.Children.Add(key);
        }
    }
}
