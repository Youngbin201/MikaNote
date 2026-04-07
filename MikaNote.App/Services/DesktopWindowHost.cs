using System.Runtime.InteropServices;
using System.Windows;

namespace MikaNote.App;

public static class DesktopWindowHost
{
    private const int WM_SPAWN_WORKER = 0x052C;
    private const uint SMTO_NORMAL = 0x0000;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    private const nint WS_CHILD = 0x40000000;
    private const nint WS_POPUP = unchecked((int)0x80000000);
    private const nint WS_CAPTION = 0x00C00000;
    private const nint WS_THICKFRAME = 0x00040000;
    private const nint WS_SYSMENU = 0x00080000;
    private const nint WS_MINIMIZEBOX = 0x00020000;
    private const nint WS_MAXIMIZEBOX = 0x00010000;
    private const nint WS_CLIPSIBLINGS = 0x04000000;
    private const nint WS_CLIPCHILDREN = 0x02000000;

    private const nint WS_EX_APPWINDOW = 0x00040000;

    private static readonly IntPtr HwndTop = IntPtr.Zero;

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_ERASE = 0x0004;
    private const uint RDW_ALLCHILDREN = 0x0080;
    private const uint RDW_UPDATENOW = 0x0100;

    public static bool TryAttachToDesktop(IntPtr noteWindowHandle, int x, int y, int width, int height)
    {
        if (noteWindowHandle == IntPtr.Zero)
        {
            return false;
        }

        IntPtr desktopHost = FindDesktopHostWindow();
        if (desktopHost == IntPtr.Zero)
        {
            return false;
        }

        nint style = GetWindowLongPtr(noteWindowHandle, GWL_STYLE);
        nint exStyle = GetWindowLongPtr(noteWindowHandle, GWL_EXSTYLE);

        nint childStyle =
            (style | WS_CHILD | WS_CLIPSIBLINGS | WS_CLIPCHILDREN) &
            ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);

        nint childExStyle = exStyle & ~WS_EX_APPWINDOW;

        SetWindowLongPtr(noteWindowHandle, GWL_STYLE, childStyle);
        SetWindowLongPtr(noteWindowHandle, GWL_EXSTYLE, childExStyle);

        IntPtr previousParent = SetParent(noteWindowHandle, desktopHost);
        if (previousParent == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
        {
            SetWindowLongPtr(noteWindowHandle, GWL_STYLE, style);
            SetWindowLongPtr(noteWindowHandle, GWL_EXSTYLE, exStyle);
            return false;
        }

        SetWindowPos(
            noteWindowHandle,
            HwndTop,
            x,
            y,
            width,
            height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);

        return true;
    }

    public static bool TryAttachToDesktop(IntPtr windowHandle, nint style, nint exStyle, int x, int y, int width, int height)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        IntPtr desktopHost = FindDesktopHostWindow();
        if (desktopHost == IntPtr.Zero)
        {
            return false;
        }

        nint childStyle =
            (style | WS_CHILD | WS_CLIPSIBLINGS | WS_CLIPCHILDREN) &
            ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);

        nint childExStyle = exStyle & ~WS_EX_APPWINDOW;

        SetWindowLongPtr(windowHandle, GWL_STYLE, childStyle);
        SetWindowLongPtr(windowHandle, GWL_EXSTYLE, childExStyle);

        IntPtr previousParent = SetParent(windowHandle, desktopHost);
        if (previousParent == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
        {
            SetWindowLongPtr(windowHandle, GWL_STYLE, style);
            SetWindowLongPtr(windowHandle, GWL_EXSTYLE, exStyle);
            return false;
        }

        SetWindowPos(
            windowHandle,
            HwndTop,
            x,
            y,
            width,
            height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);

        return true;
    }

    public static void DetachFromDesktop(IntPtr windowHandle, nint style, nint exStyle, int x, int y, int width, int height)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        SetParent(windowHandle, IntPtr.Zero);
        SetWindowLongPtr(windowHandle, GWL_STYLE, style);
        SetWindowLongPtr(windowHandle, GWL_EXSTYLE, exStyle);
        SetWindowPos(
            windowHandle,
            HwndTop,
            x,
            y,
            width,
            height,
            SWP_SHOWWINDOW | SWP_FRAMECHANGED);
    }

    public static void BringToFront(IntPtr noteWindowHandle)
    {
        if (noteWindowHandle == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            noteWindowHandle,
            HwndTop,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

        SetFocus(noteWindowHandle);
    }

    public static void UpdateBounds(IntPtr windowHandle, int x, int y, int width, int height, bool showWindow = true)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        uint flags = showWindow
            ? SWP_NOACTIVATE | SWP_SHOWWINDOW
            : SWP_NOACTIVATE;

        SetWindowPos(
            windowHandle,
            HwndTop,
            x,
            y,
            width,
            height,
            flags);
    }

    public static void RefreshDesktopHost(IntPtr noteWindowHandle)
    {
        if (noteWindowHandle != IntPtr.Zero)
        {
            RedrawWindow(
                noteWindowHandle,
                IntPtr.Zero,
                IntPtr.Zero,
                RDW_INVALIDATE | RDW_ERASE | RDW_UPDATENOW);
        }

        IntPtr desktopHost = FindDesktopHostWindow();
        if (desktopHost == IntPtr.Zero)
        {
            return;
        }

        RedrawWindow(
            desktopHost,
            IntPtr.Zero,
            IntPtr.Zero,
            RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
    }

    public static bool ContainsScreenPoint(IntPtr windowHandle, double x, double y)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (!GetWindowRect(windowHandle, out NativeRect rect))
        {
            return false;
        }

        return x >= rect.Left
            && x < rect.Right
            && y >= rect.Top
            && y < rect.Bottom;
    }

    public static nint GetWindowStyle(IntPtr windowHandle) => GetWindowLongPtr(windowHandle, GWL_STYLE);

    public static nint GetWindowExStyle(IntPtr windowHandle) => GetWindowLongPtr(windowHandle, GWL_EXSTYLE);

    private static IntPtr FindDesktopHostWindow()
    {
        IntPtr progman = FindWindow("Progman", "Program Manager");
        if (progman != IntPtr.Zero)
        {
            SendMessageTimeout(
                progman,
                WM_SPAWN_WORKER,
                IntPtr.Zero,
                IntPtr.Zero,
                SMTO_NORMAL,
                1000,
                out _);
        }

        IntPtr shellView = IntPtr.Zero;

        EnumWindows((topLevelWindow, _) =>
        {
            IntPtr currentShellView = FindWindowEx(topLevelWindow, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (currentShellView == IntPtr.Zero)
            {
                return true;
            }

            shellView = currentShellView;
            return false;
        }, IntPtr.Zero);

        if (shellView != IntPtr.Zero)
        {
            return shellView;
        }

        if (progman != IntPtr.Zero)
        {
            return progman;
        }

        return GetShellWindow();
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RedrawWindow(
        IntPtr hWnd,
        IntPtr lprcUpdate,
        IntPtr hrgnUpdate,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        int Msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
