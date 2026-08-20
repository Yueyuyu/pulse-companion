using System;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace CodexQuotaOverlay
{
    internal static class Program
    {
        private const string MutexName = "Local\\CodexQuotaOverlay.SingleInstance";

        [STAThread]
        private static int Main(string[] args)
        {
#if CONSOLE
            if (args.Length > 0 && string.Equals(args[0], "--probe", StringComparison.OrdinalIgnoreCase))
            {
                return RunQuotaProbe();
            }

            if (args.Length > 0 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                return RunSelfTest();
            }

            if (args.Length > 0 && string.Equals(args[0], "--window-probe", StringComparison.OrdinalIgnoreCase))
            {
                return RunWindowProbe();
            }

            if (args.Length > 0 && string.Equals(args[0], "--task-self-test", StringComparison.OrdinalIgnoreCase))
            {
                return RunTaskSelfTest();
            }

            if (args.Length > 0 && string.Equals(args[0], "--task-probe", StringComparison.OrdinalIgnoreCase))
            {
                return RunTaskProbe();
            }

            if (args.Length > 0 && string.Equals(args[0], "--codex-path-probe", StringComparison.OrdinalIgnoreCase))
            {
                return RunCodexPathProbe();
            }

            if (args.Length > 0 && string.Equals(args[0], "--task-window-probe", StringComparison.OrdinalIgnoreCase))
            {
                return RunTaskWindowProbe();
            }

            if (args.Length > 1 && string.Equals(args[0], "--task-screenshot", StringComparison.OrdinalIgnoreCase))
            {
                return RunTaskScreenshot(args[1]);
            }
#endif

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return 0;
                }

                NativeMethods.EnableBestDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (OverlayApplicationContext context = new OverlayApplicationContext())
                {
                    Application.Run(context);
                }

                GC.KeepAlive(mutex);
                return 0;
            }
        }

#if CONSOLE
        private static int RunQuotaProbe()
        {
            QuotaSnapshot snapshot;
            string status;
            bool success = AppServerClient.TryReadOnce(TimeSpan.FromSeconds(25), out snapshot, out status);
            if (!success || snapshot == null)
            {
                Console.WriteLine("{\"ok\":false,\"status\":\"" + EscapeJson(status) + "\"}");
                return 1;
            }

            Console.WriteLine(
                "{\"ok\":true,\"remainingPercent\":" + snapshot.RemainingPercent.ToString(CultureInfo.InvariantCulture) +
                ",\"usedPercent\":" + snapshot.UsedPercent.ToString("0.##", CultureInfo.InvariantCulture) +
                ",\"windowDurationMins\":" + snapshot.WindowDurationMins.ToString(CultureInfo.InvariantCulture) +
                ",\"resetsAt\":\"" + snapshot.ResetsAtUtc.ToString("o", CultureInfo.InvariantCulture) +
                "\",\"limitId\":\"" + EscapeJson(snapshot.LimitId) + "\"}");
            return 0;
        }

        private static int RunSelfTest()
        {
            string result;
            bool success = QuotaParser.RunSelfTest(out result);
            Console.WriteLine(result);
            return success ? 0 : 1;
        }

        private static int RunWindowProbe()
        {
            CodexWindowInfo window = CodexWindowTracker.FindBestWindow();
            if (window == null)
            {
                Console.WriteLine("{\"ok\":false,\"status\":\"未找到 Codex 主窗口\"}");
                return 1;
            }

            System.Drawing.Rectangle overlay = CodexWindowTracker.CalculateOverlayBounds(window);
            Console.WriteLine(
                "{\"ok\":true,\"windowHandle\":" + window.Handle.ToInt64().ToString(CultureInfo.InvariantCulture) +
                ",\"clientLeft\":" + window.ClientBounds.Left.ToString(CultureInfo.InvariantCulture) +
                ",\"clientTop\":" + window.ClientBounds.Top.ToString(CultureInfo.InvariantCulture) +
                ",\"clientWidth\":" + window.ClientBounds.Width.ToString(CultureInfo.InvariantCulture) +
                ",\"clientHeight\":" + window.ClientBounds.Height.ToString(CultureInfo.InvariantCulture) +
                ",\"dpi\":" + window.Dpi.ToString(CultureInfo.InvariantCulture) +
                ",\"overlayLeft\":" + overlay.Left.ToString(CultureInfo.InvariantCulture) +
                ",\"overlayTop\":" + overlay.Top.ToString(CultureInfo.InvariantCulture) +
                ",\"overlayWidth\":" + overlay.Width.ToString(CultureInfo.InvariantCulture) +
                ",\"overlayHeight\":" + overlay.Height.ToString(CultureInfo.InvariantCulture) + "}");
            return 0;
        }

        private static int RunTaskSelfTest()
        {
            string result;
            bool success = TaskParser.RunSelfTest(out result);
            Console.WriteLine(result);
            return success ? 0 : 1;
        }

        private static int RunTaskProbe()
        {
            TaskListSnapshot snapshot;
            string status;
            bool success = AppServerClient.TryReadTasksOnce(TimeSpan.FromSeconds(30), out snapshot, out status);
            if (!success || snapshot == null)
            {
                Console.WriteLine("{\"ok\":false,\"status\":\"" + EscapeJson(status) + "\"}");
                return 1;
            }

            Console.WriteLine(
                "{\"ok\":true,\"running\":" + snapshot.RunningCount.ToString(CultureInfo.InvariantCulture) +
                ",\"attention\":" + snapshot.AttentionCount.ToString(CultureInfo.InvariantCulture) +
                ",\"tracked\":" + snapshot.Tasks.Count.ToString(CultureInfo.InvariantCulture) + "}");
            return 0;
        }

        private static int RunCodexPathProbe()
        {
            string path = AppServerClient.FindCodexExecutable();
            Console.WriteLine(
                "{\"ok\":" + (System.IO.File.Exists(path) ? "true" : "false") +
                ",\"path\":\"" + EscapeJson(path) + "\"}");
            return System.IO.File.Exists(path) ? 0 : 1;
        }

        private static int RunTaskWindowProbe()
        {
            TaskLightWindowSnapshot snapshot = TaskLightWindowProbe.Read();
            bool success = snapshot.LightHandle != IntPtr.Zero;
            Console.WriteLine(
                "{\"ok\":" + (success ? "true" : "false") +
                ",\"lightHandle\":" + snapshot.LightHandle.ToInt64().ToString(CultureInfo.InvariantCulture) +
                ",\"lightLeft\":" + snapshot.LightBounds.Left.ToString(CultureInfo.InvariantCulture) +
                ",\"lightTop\":" + snapshot.LightBounds.Top.ToString(CultureInfo.InvariantCulture) +
                ",\"lightWidth\":" + snapshot.LightBounds.Width.ToString(CultureInfo.InvariantCulture) +
                ",\"lightHeight\":" + snapshot.LightBounds.Height.ToString(CultureInfo.InvariantCulture) +
                ",\"lightTopMost\":" + (snapshot.LightTopMost ? "true" : "false") +
                ",\"detailsVisible\":" + (snapshot.DetailsHandle != IntPtr.Zero ? "true" : "false") +
                ",\"detailsWidth\":" + snapshot.DetailsBounds.Width.ToString(CultureInfo.InvariantCulture) +
                ",\"detailsHeight\":" + snapshot.DetailsBounds.Height.ToString(CultureInfo.InvariantCulture) +
                ",\"detailsTopMost\":" + (snapshot.DetailsTopMost ? "true" : "false") + "}");
            return success ? 0 : 1;
        }

        private static int RunTaskScreenshot(string outputPath)
        {
            TaskLightWindowSnapshot snapshot;
            string status;
            bool success = TaskLightWindowProbe.Capture(outputPath, out snapshot, out status);
            Console.WriteLine(
                "{\"ok\":" + (success ? "true" : "false") +
                ",\"detailsVisible\":" + (snapshot != null && snapshot.DetailsHandle != IntPtr.Zero ? "true" : "false") +
                ",\"path\":\"" + EscapeJson(outputPath) +
                "\",\"status\":\"" + EscapeJson(status) + "\"}");
            return success ? 0 : 1;
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }
#endif
    }
}
