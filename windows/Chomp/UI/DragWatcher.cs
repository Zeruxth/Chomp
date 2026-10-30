using System;
using System.Windows.Threading;

namespace Chomp;

/// <summary>Notices drags anywhere on Windows and reports a tapped Shift / Ctrl / Alt while one is in flight. Like DragWatcher.swift.
///
/// Windows has no shared drag pasteboard to peek at, so this only watches the mouse and keys: the left button held and
/// moved past the system's drag distance counts as a drag. The wheel then pops up under the cursor, and Windows' own
/// drag-and-drop tells it what's being carried (or that it's not files, and it goes away). No hooks, no admin rights.</summary>
public sealed class DragWatcher
{
    public Action<Mode>? OnTrigger;

    const int Shift = 0x10, Ctrl = 0x11, Alt = 0x12;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    bool down, dragging;
    Native.POINT start;
    (bool Shift, bool Ctrl, bool Alt) held;

    public void Start()
    {
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    static (bool, bool, bool) Keys() => (Native.KeyDown(Shift), Native.KeyDown(Ctrl), Native.KeyDown(Alt));

    void Tick()
    {
        if (!Native.LeftButtonDown())
        {
            down = dragging = false;
            return;
        }
        var c = Native.Cursor();
        if (!down)
        {
            down = true;
            dragging = false;
            start = c;
            held = Keys();
            return;
        }
        if (!dragging)
        {
            var (dx, dy) = Native.DragDistance();
            if (Math.Abs(c.X - start.X) <= dx && Math.Abs(c.Y - start.Y) <= dy) return;
            dragging = true;
            held = Keys();     // keys already down when the drag started don't count
        }

        var now = Keys();
        var (s, k, a) = (now.Item1 && !held.Shift, now.Item2 && !held.Ctrl, now.Item3 && !held.Alt);
        held = now;
        if (s) OnTrigger?.Invoke(Mode.Convert);
        else if (k) OnTrigger?.Invoke(Mode.Tools);
        else if (a) OnTrigger?.Invoke(Mode.Actions);
    }
}
