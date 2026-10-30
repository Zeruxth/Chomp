using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IOPath = System.IO.Path;

using static Chomp.Loc;

namespace Chomp;

/// <summary>The Trim slice: a small window to pick the part of a video to keep and, if you like, a shape to crop it to.
/// The wheel steps aside while it's open; the result shows in the bottom toast like any other job. Like Editor.swift.</summary>
public sealed class EditorWindow : Window
{
    /// <summary>Shows the editor for one video and waits. null means it was closed without saving.</summary>
    public static Task<EditPlan?> Ask(string src)
    {
        WheelWindow.Shared.Close_();
        var w = new EditorWindow(src);
        w.Show();
        w.Activate();
        return w.done.Task;
    }

    const double W = 640, VideoH = 360;
    readonly TaskCompletionSource<EditPlan?> done = new();
    readonly MediaElement video = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, ScrubbingEnabled = true, Stretch = Stretch.Uniform };
    readonly Canvas overlay = new() { ClipToBounds = true, Background = Brushes.Transparent };
    readonly Canvas track = new() { Height = 30, Background = Brushes.Transparent };
    readonly TextBlock startText = Fonts.BodyText("", 11), endText = Fonts.BodyText("", 11), keepText = Fonts.BodyText("", 12, Pal.Tooth);
    readonly StackPanel shapes = new() { Orientation = Orientation.Horizontal };
    readonly Skin k = Skin.Current;

    double duration, start, end, offset = 0.5;
    double? ratio;
    Size natural = new(16, 9);
    bool playing;

    EditorWindow(string src)
    {
        Title = IOPath.GetFileName(src);
        Width = W;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Pal.B(Pal.Panel);
        UseLayoutRounding = true;
        Topmost = true;

        var body = new StackPanel { Margin = new Thickness(20) };
        var stage = new Grid { Height = VideoH, Background = Brushes.Black, Cursor = Cursors.Hand, ToolTip = L("Click to play or pause") };
        stage.Children.Add(video);
        stage.Children.Add(overlay);
        overlay.SizeChanged += (_, _) => DrawCrop();
        stage.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not Rectangle { Tag: "box" }) PlayPause(); };
        body.Children.Add(stage);

        track.Margin = new Thickness(0, 16, 0, 4);
        track.SizeChanged += (_, _) => DrawTrack();
        body.Children.Add(track);
        var times = new DockPanel();
        DockPanel.SetDock(startText, Dock.Left);
        DockPanel.SetDock(endText, Dock.Right);
        times.Children.Add(startText);
        times.Children.Add(endText);
        times.Children.Add(keepText);
        body.Children.Add(times);

        var row = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Cap(L("Cancel"), false, 84, () => Close()));
        buttons.Children.Add(Cap(L("Save"), true, 84, Save));
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        var label = Fonts.Display(L("Shape"), 14, Pal.Muted);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 10, 0);
        shapes.Children.Add(label);
        row.Children.Add(shapes);
        body.Children.Add(row);
        Content = body;
        DrawShapes();

        video.MediaOpened += (_, _) =>
        {
            if (video.NaturalDuration.HasTimeSpan) duration = video.NaturalDuration.TimeSpan.TotalSeconds;
            natural = new Size(Math.Max(1, video.NaturalVideoWidth), Math.Max(1, video.NaturalVideoHeight));
            end = duration;
            Seek(0);
            DrawTrack();
            DrawCrop();
        };
        video.MediaFailed += async (_, _) =>
        {
            // Windows can't preview this one (WebM without the extension, say). Trimming still works by the clock.
            try { duration = await Media.Duration(src); } catch { }
            end = duration;
            DrawTrack();
        };
        video.MediaEnded += (_, _) => { playing = false; video.Pause(); };
        video.Source = new Uri(src);
        video.Play();
        video.Pause();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) Save();
            else if (e.Key == Key.Space) PlayPause();
        };
        Closed += (_, _) =>
        {
            video.Close();
            done.TrySetResult(null);
        };
    }

    void Save()
    {
        if (duration <= 0) return;
        done.TrySetResult(new EditPlan(start, end, ratio, offset));
        Close();
    }

    void PlayPause()
    {
        if (playing) video.Pause();
        else
        {
            if (video.Position.TotalSeconds >= end - 0.05) Seek(start);
            video.Play();
        }
        playing = !playing;
    }

    void Seek(double t) => video.Position = TimeSpan.FromSeconds(Math.Max(0, t));

    Grid Cap(string text, bool on, double w, Action click)
    {
        var holder = new Grid { Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        holder.Children.Add(PixelUI.Keycap(text, on, k, w, 30));
        holder.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return holder;
    }

    void DrawShapes()
    {
        while (shapes.Children.Count > 1) shapes.Children.RemoveAt(1);
        foreach (var (title, r) in EditPlan.Shapes)
        {
            var on = ratio == r;
            var key = Cap(title, on, 64, () =>
            {
                ratio = r;
                offset = 0.5;
                DrawShapes();
                DrawCrop();
            });
            key.Margin = new Thickness(0, 0, 6, 0);
            shapes.Children.Add(key);
        }
    }

    // MARK: Trim bar

    void DrawTrack()
    {
        track.Children.Clear();
        var w = track.ActualWidth;
        if (w <= 0) return;
        var d = Math.Max(duration, 0.001);
        double x0 = w * start / d, x1 = w * end / d;
        Add(track, new Rectangle { Width = w, Height = 14, Fill = Pal.B(Pal.Segment) }, 0, 8);
        Add(track, new Rectangle { Width = Math.Max(0, x1 - x0), Height = 14, Fill = Pal.B(k.Accent, 140) }, x0, 8);
        Handle(x0, true);
        Handle(x1, false);
        startText.Text = Clock(start);
        endText.Text = Clock(end);
        keepText.Text = L("Keeping {0}", Clock(end - start));
    }

    void Handle(double x, bool isStart)
    {
        var h = new Rectangle { Width = 10, Height = 30, Fill = Pal.B(Pal.Tooth), Stroke = Pal.B(Pal.Ink), StrokeThickness = 2, Cursor = Cursors.SizeWE };
        Add(track, h, x - 5, 0);
        h.MouseLeftButtonDown += (_, e) => { h.CaptureMouse(); e.Handled = true; };
        h.MouseLeftButtonUp += (_, _) => h.ReleaseMouseCapture();
        h.MouseMove += (_, e) =>
        {
            if (!h.IsMouseCaptured) return;
            var t = e.GetPosition(track).X / Math.Max(1, track.ActualWidth) * duration;
            if (isStart) { start = Math.Clamp(t, 0, end - 0.1); Seek(start); }
            else { end = Math.Clamp(t, start + 0.1, duration); Seek(end); }
            if (playing) { video.Pause(); playing = false; }
            // Redraw without losing the captured handle: move it rather than rebuilding.
            Canvas.SetLeft(h, (isStart ? start : end) / Math.Max(duration, 0.001) * track.ActualWidth - 5);
            var bar = (Rectangle)track.Children[1];
            double x0 = track.ActualWidth * start / Math.Max(duration, 0.001), x1 = track.ActualWidth * end / Math.Max(duration, 0.001);
            Canvas.SetLeft(bar, x0);
            bar.Width = Math.Max(0, x1 - x0);
            startText.Text = Clock(start);
            endText.Text = Clock(end);
            keepText.Text = L("Keeping {0}", Clock(end - start));
        };
    }

    static string Clock(double t)
    {
        var s = Math.Max(0, t);
        return $"{(int)s / 60}:{s % 60:00.0}";
    }

    // MARK: Crop

    Rect Fitted()
    {
        double aw = overlay.ActualWidth, ah = overlay.ActualHeight;
        var kf = Math.Min(aw / natural.Width, ah / natural.Height);
        double w = natural.Width * kf, h = natural.Height * kf;
        return new Rect((aw - w) / 2, (ah - h) / 2, w, h);
    }

    Rect Box(Rect f, double r)
    {
        double w = Math.Min(f.Width, f.Height * r), h = Math.Min(f.Height, f.Width / r);
        return new Rect(f.X + (f.Width - w) * offset, f.Y + (f.Height - h) * offset, w, h);
    }

    /// <summary>Everything outside the crop dimmed. Drag the bright box to move it.</summary>
    void DrawCrop()
    {
        overlay.Children.Clear();
        if (ratio is not double r) return;
        var f = Fitted();
        var b = Box(f, r);
        var dim = new Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(f), new RectangleGeometry(b)),
            IsHitTestVisible = false,
        };
        overlay.Children.Add(dim);
        var box = new Rectangle { Width = b.Width, Height = b.Height, Stroke = Pal.B(k.Accent), StrokeThickness = 2, Fill = Brushes.Transparent, Cursor = Cursors.SizeAll, Tag = "box" };
        Add(overlay, box, b.X, b.Y);
        box.MouseLeftButtonDown += (_, e) => { box.CaptureMouse(); e.Handled = true; };
        box.MouseLeftButtonUp += (_, e) => { box.ReleaseMouseCapture(); e.Handled = true; };
        box.MouseMove += (_, e) =>
        {
            if (!box.IsMouseCaptured) return;
            var p = e.GetPosition(overlay);
            double roomX = f.Width - b.Width, roomY = f.Height - b.Height, room = Math.Max(roomX, roomY);
            if (room < 1) return;
            var along = roomX > roomY ? p.X - f.X - b.Width / 2 : p.Y - f.Y - b.Height / 2;
            offset = Math.Clamp(along / room, 0, 1);
            var nb = Box(f, r);
            Canvas.SetLeft(box, nb.X);
            Canvas.SetTop(box, nb.Y);
            dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(f), new RectangleGeometry(nb));
        };
    }

    static void Add(Canvas c, UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        c.Children.Add(e);
    }
}
