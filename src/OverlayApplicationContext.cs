using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexQuotaOverlay
{
    internal sealed class OverlayApplicationContext : ApplicationContext, IDisposable
    {
        private readonly QuotaOverlayForm overlay;
        private readonly TaskLightForm taskLight;
        private readonly TaskLightNotifier taskNotifier;
        private AppServerClient appServerClient;
        private readonly Timer trackingTimer;
        private readonly Timer refreshTimer;
        private readonly Timer taskRefreshTimer;
        private CodexWindowInfo currentWindow;
        private int trackingTick;
        private bool codexDesktopRunning;
        private bool disposed;

        public OverlayApplicationContext()
        {
            overlay = new QuotaOverlayForm();
            IntPtr unused = overlay.Handle;
            overlay.RefreshRequested += delegate
            {
                AppServerClient client = appServerClient;
                if (client != null)
                {
                    client.RequestRefresh();
                }
            };
            overlay.ExitRequested += delegate { ExitApplication(); };

            taskLight = new TaskLightForm();
            IntPtr unusedTaskLight = taskLight.Handle;
            taskLight.ShowTaskLight();
            taskNotifier = new TaskLightNotifier();

            trackingTimer = new Timer();
            trackingTimer.Interval = 120;
            trackingTimer.Tick += OnTrackingTick;
            trackingTimer.Start();

            refreshTimer = new Timer();
            refreshTimer.Interval = 5 * 60 * 1000;
            refreshTimer.Tick += delegate
            {
                AppServerClient client = appServerClient;
                if (client != null)
                {
                    client.RequestRefresh();
                }
            };
            refreshTimer.Start();

            taskRefreshTimer = new Timer();
            taskRefreshTimer.Interval = 2000;
            taskRefreshTimer.Tick += delegate
            {
                AppServerClient client = appServerClient;
                if (client != null)
                {
                    client.RequestTaskRefresh();
                }
            };
            taskRefreshTimer.Start();
        }

        private void OnTrackingTick(object sender, EventArgs args)
        {
            trackingTick++;
            if (trackingTick % 10 == 1)
            {
                codexDesktopRunning = CodexWindowTracker.IsCodexDesktopRunning();
            }

            if (!codexDesktopRunning)
            {
                currentWindow = null;
                StopAppServerClient();
                overlay.HideOverlay();
                taskLight.UpdateConnection(false, "Codex 暂未运行");
                return;
            }

            EnsureAppServerClient();
            if (currentWindow == null || trackingTick % 10 == 1)
            {
                currentWindow = CodexWindowTracker.FindBestWindow();
            }
            else
            {
                currentWindow = CodexWindowTracker.RefreshWindow(currentWindow.Handle);
            }

            if (currentWindow == null)
            {
                overlay.HideOverlay();
                return;
            }

            if (!CodexWindowTracker.IsCodexForeground(currentWindow))
            {
                overlay.HideOverlay();
                return;
            }

            Rectangle bounds = CodexWindowTracker.CalculateOverlayBounds(currentWindow);
            overlay.ShowAt(currentWindow.Handle, bounds);
        }

        private void OnQuotaUpdated(object sender, QuotaEventArgs args)
        {
            if (disposed || overlay.IsDisposed || sender != appServerClient)
            {
                return;
            }

            try
            {
                overlay.BeginInvoke(new Action<QuotaSnapshot>(overlay.UpdateQuota), args.Snapshot);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void OnStatusChanged(object sender, StatusEventArgs args)
        {
            if (disposed || overlay.IsDisposed || sender != appServerClient)
            {
                return;
            }

            try
            {
                overlay.BeginInvoke(new Action<string>(overlay.UpdateStatus), args.Status);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void OnTasksUpdated(object sender, TaskListEventArgs args)
        {
            if (disposed || taskLight.IsDisposed || sender != appServerClient)
            {
                return;
            }

            try
            {
                taskLight.BeginInvoke(new Action<TaskListSnapshot>(ApplyTaskSnapshot), args.Snapshot);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ApplyTaskSnapshot(TaskListSnapshot snapshot)
        {
            if (disposed)
            {
                return;
            }

            taskLight.UpdateTasks(snapshot);
            taskNotifier.Update(snapshot);
        }

        private void OnTaskConnectionChanged(object sender, TaskConnectionEventArgs args)
        {
            if (disposed || taskLight.IsDisposed || sender != appServerClient || args.Connected)
            {
                return;
            }

            try
            {
                taskLight.BeginInvoke(
                    new Action<bool, string>(taskLight.UpdateConnection),
                    args.Connected,
                    args.Status);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ExitApplication()
        {
            Dispose();
            ExitThread();
        }

        private void EnsureAppServerClient()
        {
            if (appServerClient != null || disposed)
            {
                return;
            }

            AppServerClient client = new AppServerClient();
            client.QuotaUpdated += OnQuotaUpdated;
            client.TasksUpdated += OnTasksUpdated;
            client.TaskConnectionChanged += OnTaskConnectionChanged;
            client.StatusChanged += OnStatusChanged;
            appServerClient = client;
            taskLight.UpdateConnection(false, "正在连接 Codex");
            client.Start();
        }

        private void StopAppServerClient()
        {
            AppServerClient client = appServerClient;
            if (client == null)
            {
                return;
            }

            appServerClient = null;
            client.QuotaUpdated -= OnQuotaUpdated;
            client.TasksUpdated -= OnTasksUpdated;
            client.TaskConnectionChanged -= OnTaskConnectionChanged;
            client.StatusChanged -= OnStatusChanged;
            client.Dispose();
            taskNotifier.Reset();
        }

        protected override void ExitThreadCore()
        {
            Dispose();
            base.ExitThreadCore();
        }

        public new void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            trackingTimer.Stop();
            refreshTimer.Stop();
            taskRefreshTimer.Stop();
            trackingTimer.Dispose();
            refreshTimer.Dispose();
            taskRefreshTimer.Dispose();
            StopAppServerClient();
            overlay.HideOverlay();
            overlay.Dispose();
            taskLight.CloseDetails();
            taskLight.Hide();
            taskLight.Dispose();
            taskNotifier.Dispose();
        }
    }
}
