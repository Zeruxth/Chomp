using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

using static Chomp.Loc;

namespace Chomp;

/// <summary>The Windows share sheet: Nearby Sharing, Mail, Phone Link and whatever apps take files. It stands in for AirDrop.
/// Desktop apps reach it through IDataTransferManagerInterop with a window of their own to anchor it.</summary>
public static class Share
{
    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow(IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    static readonly Guid DataTransferManagerIid = new("a5caee9b-8708-49d1-8d36-67d25a8da00c");

    static Window? anchor;
    static IntPtr hwnd;
    static DataTransferManager? manager;
    static IDataTransferManagerInterop? interop;
    static IReadOnlyList<string> items = Array.Empty<string>();

    /// <summary>Must run on the UI thread.</summary>
    public static void Show(IReadOnlyList<string> paths)
    {
        items = paths.ToList();
        if (anchor == null)
        {
            // A tiny see-through window under the cursor: the sheet opens next to it.
            anchor = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                ShowInTaskbar = false, Topmost = true, Width = 1, Height = 1, Title = "Chomp",
            };
            hwnd = new WindowInteropHelper(anchor).EnsureHandle();
            var factory = WinRT.ActivationFactory.Get("Windows.ApplicationModel.DataTransfer.DataTransferManager");
            interop = (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);
            var iid = DataTransferManagerIid;
            var ptr = interop.GetForWindow(hwnd, ref iid);
            try { manager = DataTransferManager.FromAbi(ptr); }
            finally { Marshal.Release(ptr); }
            manager.DataRequested += OnRequested;
            manager.TargetApplicationChosen += (_, _) => HideSoon(2);
        }
        var c = Native.Cursor();
        anchor.Show();
        Native.Place(hwnd, c.X, c.Y, 1, 1);
        anchor.Activate();
        interop!.ShowShareUIForWindow(hwnd);
        // The sheet is anchored to it, so it stays (invisible) while the sheet is up.
        HideSoon(120);
    }

    static System.Windows.Threading.DispatcherTimer? hideTimer;

    static void HideSoon(double secs)
    {
        anchor?.Dispatcher.InvokeAsync(() =>
        {
            hideTimer?.Stop();
            hideTimer = new() { Interval = TimeSpan.FromSeconds(secs) };
            hideTimer.Tick += (_, _) => { hideTimer.Stop(); anchor?.Hide(); };
            hideTimer.Start();
        });
    }

    static async void OnRequested(DataTransferManager sender, DataRequestedEventArgs e)
    {
        var req = e.Request;
        var wait = req.GetDeferral();
        try
        {
            var n = items.Count;
            req.Data.Properties.Title = n == 1 ? Path.GetFileName(items[0].TrimEnd('\\')) : Counted(n, "item", "items");
            req.Data.Properties.Description = L("From Chomp");
            var things = new List<IStorageItem>();
            foreach (var p in items)
                things.Add(Directory.Exists(p) ? await StorageFolder.GetFolderFromPathAsync(p) : (IStorageItem)await StorageFile.GetFileFromPathAsync(p));
            req.Data.SetStorageItems(things);
        }
        catch (Exception ex) { req.FailWithDisplayText(ex.Message); }
        finally { wait.Complete(); }
    }
}
