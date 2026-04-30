using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace ShushVault.Windows;

/// <summary>
/// Enforces a minimum window track size by subclassing the HWND and intercepting WM_GETMINMAXINFO.
/// </summary>
internal static class WindowMinSize
{
    private const int GWLP_WNDPROC = -4;
    private const uint WM_GETMINMAXINFO = 0x0024;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(IntPtr hWnd);

    private static readonly Dictionary<IntPtr, (IntPtr Prev, int MinW, int MinH, WndProcDelegate Proc)> Tracked = new();

    public static void Apply(Window window, int minWidth, int minHeight)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (Tracked.ContainsKey(hwnd))
        {
            // Update tracked dimensions; reuse existing subclass.
            var existing = Tracked[hwnd];
            Tracked[hwnd] = (existing.Prev, minWidth, minHeight, existing.Proc);
            return;
        }

        WndProcDelegate proc = null!;
        proc = (IntPtr h, uint msg, IntPtr w, IntPtr l) =>
        {
            if (msg == WM_GETMINMAXINFO && Tracked.TryGetValue(h, out var info))
            {
                var dpi = GetDpiForWindow(h);
                var scale = dpi <= 0 ? 1.0 : dpi / 96.0;
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(l);
                mmi.ptMinTrackSize.X = (int)(info.MinW * scale);
                mmi.ptMinTrackSize.Y = (int)(info.MinH * scale);
                Marshal.StructureToPtr(mmi, l, false);
                return IntPtr.Zero;
            }

            return CallWindowProc(Tracked[h].Prev, h, msg, w, l);
        };

        var procPtr = Marshal.GetFunctionPointerForDelegate(proc);
        var prev = SetWindowLongPtr(hwnd, GWLP_WNDPROC, procPtr);
        Tracked[hwnd] = (prev, minWidth, minHeight, proc);

        window.Closed += (_, _) =>
        {
            if (Tracked.TryGetValue(hwnd, out var entry))
            {
                SetWindowLongPtr(hwnd, GWLP_WNDPROC, entry.Prev);
                Tracked.Remove(hwnd);
            }
        };
    }
}
