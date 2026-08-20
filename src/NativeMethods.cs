using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexQuotaOverlay
{
    internal static class NativeMethods
    {
        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        internal const int GwlExStyle = -20;
        internal const long WsExToolWindow = 0x00000080L;
        internal const long WsExLayered = 0x00080000L;
        internal const long WsExNoActivate = 0x08000000L;
        internal const int SwHide = 0;
        internal const int SwShowNoActivate = 4;
        internal const uint SwpNoActivate = 0x0010;
        internal const uint SwpShowWindow = 0x0040;
        internal const uint SwpNoOwnerZOrder = 0x0200;
        internal const uint SpiGetClientAreaAnimation = 0x1042;
        internal const int DwmwaCloaked = 14;
        internal static readonly IntPtr HwndTopMost = new IntPtr(-1);
        internal static readonly IntPtr HwndNoTopMost = new IntPtr(-2);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(
            uint action,
            uint parameter,
            out bool value,
            uint updateFlags);

        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hWnd, int command);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int valueSize);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int awareness);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        internal static void EnableBestDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4)))
                {
                    return;
                }
            }
            catch (Exception)
            {
            }

            try
            {
                if (SetProcessDpiAwareness(2) == 0)
                {
                    return;
                }
            }
            catch (Exception)
            {
            }

            try
            {
                SetProcessDPIAware();
            }
            catch (Exception)
            {
            }
        }

        internal static bool AreClientAreaAnimationsEnabled()
        {
            try
            {
                bool enabled;
                return SystemParametersInfo(SpiGetClientAreaAnimation, 0, out enabled, 0) && enabled;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
