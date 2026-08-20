using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace CodexQuotaOverlay
{
    internal sealed class CodexWindowInfo
    {
        public IntPtr Handle { get; private set; }
        public uint ProcessId { get; private set; }
        public Rectangle ClientBounds { get; private set; }
        public uint Dpi { get; private set; }

        public CodexWindowInfo(IntPtr handle, uint processId, Rectangle clientBounds, uint dpi)
        {
            Handle = handle;
            ProcessId = processId;
            ClientBounds = clientBounds;
            Dpi = dpi == 0 ? 96U : dpi;
        }
    }

    internal static class CodexWindowTracker
    {
        // Codex 26.814 在账户栏右侧加入了“语音”，预留 20px 避免覆盖其图标和点击区域。
        private const int DesignPillRight = 202;
        private const int DesignPillWidth = 58;
        private const int DesignPillHeight = 26;
        private const int DesignBottomOffset = 36;

        public static CodexWindowInfo FindBestWindow()
        {
            List<CodexWindowInfo> candidates = new List<CodexWindowInfo>();
            NativeMethods.EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                CodexWindowInfo info = TryCreateWindowInfo(handle);
                if (info != null)
                {
                    candidates.Add(info);
                }

                return true;
            }, IntPtr.Zero);

            CodexWindowInfo best = null;
            long bestArea = 0;
            foreach (CodexWindowInfo candidate in candidates)
            {
                long area = (long)candidate.ClientBounds.Width * candidate.ClientBounds.Height;
                if (best == null || area > bestArea)
                {
                    best = candidate;
                    bestArea = area;
                }
            }

            return best;
        }

        public static CodexWindowInfo RefreshWindow(IntPtr handle)
        {
            return TryCreateWindowInfo(handle, false);
        }

        public static Rectangle CalculateOverlayBounds(CodexWindowInfo window)
        {
            double scale = Math.Max(1.0, window.Dpi / 96.0);
            int width = Math.Max(50, (int)Math.Round(DesignPillWidth * scale));
            int height = Math.Max(24, (int)Math.Round(DesignPillHeight * scale));
            int pillRight = (int)Math.Round(DesignPillRight * scale);
            int bottomOffset = (int)Math.Round(DesignBottomOffset * scale);

            int x = window.ClientBounds.Left + pillRight - width;
            int y = window.ClientBounds.Bottom - bottomOffset;
            return new Rectangle(x, y, width, height);
        }

        public static bool IsCodexForeground(CodexWindowInfo window)
        {
            if (window == null)
            {
                return false;
            }

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            uint foregroundProcessId;
            NativeMethods.GetWindowThreadProcessId(foreground, out foregroundProcessId);
            uint overlayProcessId = (uint)Process.GetCurrentProcess().Id;
            return foregroundProcessId == window.ProcessId || foregroundProcessId == overlayProcessId;
        }

        public static bool IsCodexDesktopRunning()
        {
            Process[] chatGptProcesses = new Process[0];
            Process[] codexProcesses = new Process[0];
            try
            {
                chatGptProcesses = Process.GetProcessesByName("ChatGPT");
                foreach (Process process in chatGptProcesses)
                {
                    using (process)
                    {
                        if (IsDesktopProcessPath(GetProcessPath(process)))
                        {
                            return true;
                        }
                    }
                }

                // Windows Store 沙盒偶尔拒绝读取 MainModule；该安装目前的桌面进程名固定为 ChatGPT。
                if (chatGptProcesses.Length > 0)
                {
                    return true;
                }

                // 兼容未来将桌面进程名改为 Codex 的安装包；排除本工具启动的本地 CLI 子进程。
                codexProcesses = Process.GetProcessesByName("Codex");
                foreach (Process process in codexProcesses)
                {
                    using (process)
                    {
                        string path = GetProcessPath(process);
                        if (path.IndexOf("\\WindowsApps\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                DisposeProcesses(chatGptProcesses);
                DisposeProcesses(codexProcesses);
            }

            return false;
        }

        private static CodexWindowInfo TryCreateWindowInfo(IntPtr handle)
        {
            return TryCreateWindowInfo(handle, true);
        }

        private static CodexWindowInfo TryCreateWindowInfo(IntPtr handle, bool validateProcess)
        {
            if (handle == IntPtr.Zero || !NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle))
            {
                return null;
            }

            int cloaked;
            if (NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DwmwaCloaked, out cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return null;
            }

            uint processId;
            NativeMethods.GetWindowThreadProcessId(handle, out processId);
            if (processId == 0 || (validateProcess && !IsCodexProcess(processId, handle)))
            {
                return null;
            }

            NativeMethods.RECT client;
            NativeMethods.POINT origin = new NativeMethods.POINT();
            if (!NativeMethods.GetClientRect(handle, out client) || !NativeMethods.ClientToScreen(handle, ref origin))
            {
                return null;
            }

            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            if (width < 700 || height < 500)
            {
                return null;
            }

            uint dpi = 96;
            try
            {
                dpi = NativeMethods.GetDpiForWindow(handle);
            }
            catch (Exception)
            {
            }

            return new CodexWindowInfo(handle, processId, new Rectangle(origin.X, origin.Y, width, height), dpi);
        }

        private static bool IsCodexProcess(uint processId, IntPtr handle)
        {
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    string path = string.Empty;
                    try
                    {
                        path = process.MainModule.FileName ?? string.Empty;
                    }
                    catch (Exception)
                    {
                    }

                    if (path.IndexOf("\\WindowsApps\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        path.IndexOf("\\OpenAI\\Codex\\", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }

                    string processName = process.ProcessName ?? string.Empty;
                    if (!string.Equals(processName, "ChatGPT", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(processName, "Codex", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            StringBuilder className = new StringBuilder(128);
            NativeMethods.GetClassName(handle, className, className.Capacity);
            return string.Equals(className.ToString(), "Chrome_WidgetWin_1", StringComparison.Ordinal);
        }

        private static string GetProcessPath(Process process)
        {
            try
            {
                return process.MainModule.FileName ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static bool IsDesktopProcessPath(string path)
        {
            return path.IndexOf("\\WindowsApps\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (path.IndexOf("\\OpenAI\\Codex\\", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    path.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) < 0);
        }

        private static void DisposeProcesses(Process[] processes)
        {
            if (processes == null)
            {
                return;
            }

            foreach (Process process in processes)
            {
                try
                {
                    process.Dispose();
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
