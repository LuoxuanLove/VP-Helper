using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace VPHelper.WinUI.Services;

/// <summary>Apply sizing limits during the native resize operation, before XAML is arranged.</summary>
internal sealed class WindowSizeGuard : IDisposable
{
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private readonly SubclassProc _callback;
    private bool _attached;
    public double Scale => Math.Max(96u, GetDpiForWindow(_hwnd)) / 96d;

    public WindowSizeGuard(Window window)
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _appWindow = window.AppWindow;
        _callback = HandleMessage;
        _attached = SetWindowSubclass(_hwnd, _callback, 1, 0);
        if (!_attached) throw new InvalidOperationException("Unable to attach native window size constraints.");
    }

    public RectInt32 WorkArea(RectInt32? preferred = null) => (preferred.HasValue
        ? DisplayArea.GetFromRect(preferred.Value, DisplayAreaFallback.Nearest)
        : DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest)).WorkArea;

    private nint HandleMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        const uint getMinMaxInfo = 0x0024, sizing = 0x0214, destroy = 0x0082;
        if (message == destroy)
        {
            Dispose();
            return DefSubclassProc(hwnd, message, wParam, lParam);
        }
        if (message == getMinMaxInfo)
        {
            var result = DefSubclassProc(hwnd, message, wParam, lParam);
            var work = WorkArea();
            var limits = WindowLayoutPolicy.GetLimits(work.Width, work.Height, Scale);
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MinTrackSize = new Point(limits.MinWidth, limits.MinHeight);
            info.MaxTrackSize = new Point(limits.MaxWidth, limits.MaxHeight);
            Marshal.StructureToPtr(info, lParam, false);
            return result;
        }
        if (message == sizing)
        {
            var rect = Marshal.PtrToStructure<Rect>(lParam);
            var work = WorkArea();
            var edge = (int)wParam;
            var (width, height) = WindowLayoutPolicy.Constrain(rect.Right - rect.Left, rect.Bottom - rect.Top,
                WindowLayoutPolicy.GetLimits(work.Width, work.Height, Scale), heightDriven: edge is 3 or 6);
            if (edge is 1 or 4 or 7) rect.Left = rect.Right - width;
            else rect.Right = rect.Left + width;
            if (edge is 3 or 4 or 5) rect.Top = rect.Bottom - height;
            else rect.Bottom = rect.Top + height;
            Marshal.StructureToPtr(rect, lParam, false);
            return 1;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (!_attached) return;
        RemoveWindowSubclass(_hwnd, _callback, 1);
        _attached = false;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point(int x, int y) { public int X = x; public int Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
}
