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

            if (args.Length > 0 && string.Equals(args[0], "--thread-uri-self-test", StringComparison.OrdinalIgnoreCase))
            {
                return RunThreadUriSelfTest();
            }

            if (args.Length > 1 && string.Equals(args[0], "--open-thread-probe", StringComparison.OrdinalIgnoreCase))
            {
                return RunOpenThreadProbe(args[1]);
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

            if (args.Length > 0 && string.Equals(args[0], "--task-ui-preview", StringComparison.OrdinalIgnoreCase))
            {
                string screenshotPath = args.Length > 1 ? args[1] : string.Empty;
                string threadId = args.Length > 2 ? args[2] : "11111111-1111-4111-8111-111111111111";
                return RunTaskUiPreview(screenshotPath, threadId);
            }

            if (args.Length > 1 && string.Equals(args[0], "--notification-preview", StringComparison.OrdinalIgnoreCase))
            {
                string threadId = args.Length > 2 ? args[2] : "019fef0d-6a70-73f3-b182-daca3a3d2ff3";
                string screenshotPath = args.Length > 3 ? args[3] : string.Empty;
                return RunNotificationPreview(args[1], threadId, screenshotPath);
            }

            if (args.Length > 1 && string.Equals(args[0], "--notification-flow-preview", StringComparison.OrdinalIgnoreCase))
            {
                string threadId = args.Length > 2 ? args[2] : "019fef0d-6a70-73f3-b182-daca3a3d2ff3";
                return RunNotificationFlowPreview(args[1], threadId);
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

        private static int RunThreadUriSelfTest()
        {
            string result;
            bool success = CodexThreadNavigator.RunSelfTest(out result);
            Console.WriteLine(result);
            return success ? 0 : 1;
        }

        private static int RunOpenThreadProbe(string threadId)
        {
            TaskSnapshot task = new TaskSnapshot(
                threadId,
                "Codex 任务跳转验证",
                string.Empty,
                string.Empty,
                CodexTaskState.Completed,
                string.Empty,
                DateTimeOffset.UtcNow);
            string status;
            bool success = CodexThreadNavigator.TryOpen(task, out status);
            Console.WriteLine(
                "{\"ok\":" + (success ? "true" : "false") +
                ",\"status\":\"" + EscapeJson(status) + "\"}");
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
                ",\"detailsHandle\":" + snapshot.DetailsHandle.ToInt64().ToString(CultureInfo.InvariantCulture) +
                ",\"detailsLeft\":" + snapshot.DetailsBounds.Left.ToString(CultureInfo.InvariantCulture) +
                ",\"detailsTop\":" + snapshot.DetailsBounds.Top.ToString(CultureInfo.InvariantCulture) +
                ",\"detailsWidth\":" + snapshot.DetailsBounds.Width.ToString(CultureInfo.InvariantCulture) +
                ",\"detailsHeight\":" + snapshot.DetailsBounds.Height.ToString(CultureInfo.InvariantCulture) +
                ",\"detailsTopMost\":" + (snapshot.DetailsTopMost ? "true" : "false") +
                ",\"toastVisible\":" + (snapshot.ToastHandle != IntPtr.Zero ? "true" : "false") +
                ",\"toastHandle\":" + snapshot.ToastHandle.ToInt64().ToString(CultureInfo.InvariantCulture) +
                ",\"toastLeft\":" + snapshot.ToastBounds.Left.ToString(CultureInfo.InvariantCulture) +
                ",\"toastTop\":" + snapshot.ToastBounds.Top.ToString(CultureInfo.InvariantCulture) +
                ",\"toastWidth\":" + snapshot.ToastBounds.Width.ToString(CultureInfo.InvariantCulture) +
                ",\"toastHeight\":" + snapshot.ToastBounds.Height.ToString(CultureInfo.InvariantCulture) +
                ",\"toastTopMost\":" + (snapshot.ToastTopMost ? "true" : "false") + "}");
            return success ? 0 : 1;
        }

        private static int RunTaskScreenshot(string outputPath)
        {
            NativeMethods.EnableBestDpiAwareness();
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

        private static int RunTaskUiPreview(string screenshotPath, string targetThreadId)
        {
            InitializePreviewApplication();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            TaskListSnapshot preview = new TaskListSnapshot(new[]
            {
                new TaskSnapshot(
                    targetThreadId,
                    "应用数据库迁移",
                    string.Empty,
                    string.Empty,
                    CodexTaskState.NeedsAttention,
                    "等待你批准执行命令",
                    now.AddSeconds(-20)),
                new TaskSnapshot(
                    "22222222-2222-4222-8222-222222222222",
                    "分析交易信号延迟",
                    string.Empty,
                    string.Empty,
                    CodexTaskState.Running,
                    "正在读取运行日志",
                    now.AddMinutes(-8).AddSeconds(-42)),
                new TaskSnapshot(
                    "33333333-3333-4333-8333-333333333333",
                    "修复登录页响应式布局",
                    string.Empty,
                    string.Empty,
                    CodexTaskState.Running,
                    "正在运行界面测试",
                    now.AddMinutes(-3).AddSeconds(-15))
            });

            using (TaskLightForm form = new TaskLightForm())
            using (System.Windows.Forms.Timer captureTimer = new System.Windows.Forms.Timer())
            using (System.Windows.Forms.Timer exitTimer = new System.Windows.Forms.Timer())
            {
                form.TaskActivated += delegate(object sender, TaskActivatedEventArgs args)
                {
                    string ignoredStatus;
                    CodexThreadNavigator.TryOpen(args == null ? null : args.Task, out ignoredStatus);
                };
                form.ShowTaskLight();
                form.UpdateTasks(preview);
                form.ShowDetailsForPreview();
                if (!string.IsNullOrWhiteSpace(screenshotPath))
                {
                    captureTimer.Interval = 700;
                    captureTimer.Tick += delegate
                    {
                        captureTimer.Stop();
                        form.SavePreviewScreenshot(screenshotPath);
                    };
                    captureTimer.Start();
                }

                exitTimer.Interval = 120000;
                exitTimer.Tick += delegate
                {
                    exitTimer.Stop();
                    form.CloseDetails();
                    form.Hide();
                    Application.ExitThread();
                };
                exitTimer.Start();
                Application.Run();
            }

            Console.WriteLine("{\"ok\":true,\"preview\":\"task-ui\"}");
            return 0;
        }

        private static int RunNotificationPreview(string previewKind, string threadId, string screenshotPath)
        {
            InitializePreviewApplication();
            bool needsAttention = string.Equals(previewKind, "attention", StringComparison.OrdinalIgnoreCase);
            TaskSnapshot task = new TaskSnapshot(
                threadId,
                needsAttention ? "应用数据库迁移" : "分析交易信号延迟",
                string.Empty,
                string.Empty,
                needsAttention ? CodexTaskState.NeedsAttention : CodexTaskState.Completed,
                needsAttention ? "等待你批准操作" : "已完成",
                DateTimeOffset.UtcNow);

            using (TaskToastForm toast = new TaskToastForm())
            using (System.Windows.Forms.Timer captureTimer = new System.Windows.Forms.Timer())
            using (System.Windows.Forms.Timer exitTimer = new System.Windows.Forms.Timer())
            {
                toast.TaskActivated += delegate(object sender, TaskActivatedEventArgs args)
                {
                    string ignoredStatus;
                    CodexThreadNavigator.TryOpen(args == null ? null : args.Task, out ignoredStatus);
                };
                toast.ShowNotification(
                    needsAttention ? TaskToastKind.NeedsAttention : TaskToastKind.Completed,
                    task,
                    needsAttention ? "Codex 需要你处理" : "Codex 任务已完成",
                    task.Title,
                    "点击打开对应任务");
                if (!string.IsNullOrWhiteSpace(screenshotPath))
                {
                    captureTimer.Interval = 700;
                    captureTimer.Tick += delegate
                    {
                        captureTimer.Stop();
                        toast.SavePreviewScreenshot(screenshotPath);
                    };
                    captureTimer.Start();
                }

                exitTimer.Interval = 11000;
                exitTimer.Tick += delegate
                {
                    exitTimer.Stop();
                    toast.CloseToast(false);
                    Application.ExitThread();
                };
                exitTimer.Start();
                Application.Run();
            }

            Console.WriteLine("{\"ok\":true,\"preview\":\"notification\"}");
            return 0;
        }

        private static int RunNotificationFlowPreview(string previewKind, string threadId)
        {
            InitializePreviewApplication();
            bool needsAttention = string.Equals(previewKind, "attention", StringComparison.OrdinalIgnoreCase);
            TaskSnapshot baselineTask = new TaskSnapshot(
                threadId,
                needsAttention ? "应用数据库迁移" : "分析交易信号延迟",
                string.Empty,
                string.Empty,
                CodexTaskState.Running,
                "Codex 正在处理",
                DateTimeOffset.UtcNow.AddMinutes(-1));
            TaskSnapshot changedTask = new TaskSnapshot(
                threadId,
                baselineTask.Title,
                string.Empty,
                string.Empty,
                needsAttention ? CodexTaskState.NeedsAttention : CodexTaskState.Completed,
                needsAttention ? "等待你批准操作" : "已完成",
                DateTimeOffset.UtcNow);

            using (TaskLightNotifier notifier = new TaskLightNotifier())
            using (System.Windows.Forms.Timer transitionTimer = new System.Windows.Forms.Timer())
            using (System.Windows.Forms.Timer exitTimer = new System.Windows.Forms.Timer())
            {
                notifier.Update(needsAttention
                    ? new TaskListSnapshot(null)
                    : new TaskListSnapshot(new[] { baselineTask }));
                transitionTimer.Interval = 350;
                transitionTimer.Tick += delegate
                {
                    transitionTimer.Stop();
                    notifier.Update(new TaskListSnapshot(new[] { changedTask }));
                };
                transitionTimer.Start();

                exitTimer.Interval = 11000;
                exitTimer.Tick += delegate
                {
                    exitTimer.Stop();
                    Application.ExitThread();
                };
                exitTimer.Start();
                Application.Run();
            }

            Console.WriteLine("{\"ok\":true,\"preview\":\"notification-flow\"}");
            return 0;
        }

        private static void InitializePreviewApplication()
        {
            NativeMethods.EnableBestDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
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
