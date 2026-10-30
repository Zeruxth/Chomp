using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Chomp;

/// <summary>The few Win32 calls WPF doesn't cover: cursor, monitors, non-activating windows, the Recycle Bin.</summary>
public static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr m, int type, out uint x, out uint y);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int cy, uint flags);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLong(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);

    public static POINT Cursor() { GetCursorPos(out var p); return p; }

    /// <summary>Work area (no taskbar) and scale of the monitor under a point, in physical pixels.</summary>
    public static (RECT Work, double Scale) MonitorAt(POINT p)
    {
        var m = MonitorFromPoint(p, 2 /* nearest */);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(m, ref mi);
        var scale = GetDpiForMonitor(m, 0, out var dx, out _) == 0 ? dx / 96.0 : 1;
        return (mi.rcWork, scale);
    }

    /// <summary>Puts a window at a physical-pixel position and size, in front of everything.</summary>
    public static void Place(IntPtr hwnd, int x, int y, int w, int h) =>
        SetWindowPos(hwnd, new IntPtr(-1) /* topmost */, x, y, w, h, 0x0010 /* no activate */);

    public static bool LeftButtonDown()
    {
        // Respect swapped buttons, like the system does.
        var vk = GetSystemMetrics(23 /* SM_SWAPBUTTON */) != 0 ? 0x02 : 0x01;
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    public static bool KeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>How far the mouse has to move with the button down before Windows calls it a drag, in physical pixels.</summary>
    public static (int X, int Y) DragDistance() => (GetSystemMetrics(68 /* SM_CXDRAG */), GetSystemMetrics(69 /* SM_CYDRAG */));

    /// <summary>Clicks don't steal focus from the app you're dragging out of, and no taskbar button.</summary>
    public static void NoActivate(IntPtr hwnd, bool on)
    {
        const int GWL_EXSTYLE = -20;
        const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
        var s = GetWindowLong(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW;
        s = on ? s | WS_EX_NOACTIVATE : s & ~WS_EX_NOACTIVATE;
        SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(s));
    }

    // MARK: Recycle Bin

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    /// <summary>Moves files to the Recycle Bin, so Undo can itself be undone.</summary>
    public static bool Recycle(IEnumerable<string> paths)
    {
        var from = string.Join("\0", paths.Select(Path.GetFullPath)) + "\0\0";
        var op = new SHFILEOPSTRUCT
        {
            wFunc = 3, // FO_DELETE
            pFrom = from,
            fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400, // allow undo, no confirmation, silent, no error UI
        };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    /// <summary>Opens Explorer with the file selected.</summary>
    public static void Reveal(string path)
    {
        var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        psi.Arguments = $"/select,\"{path}\"";
        Process.Start(psi);
    }
}
