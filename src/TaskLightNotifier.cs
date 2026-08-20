using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CodexQuotaOverlay
{
    internal sealed class TaskLightNotifier : IDisposable
    {
        private readonly NotifyIcon notifyIcon;
        private readonly Timer hideTimer;
        private readonly HashSet<string> previousAttentionIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool hasBaseline;
        private int previousRunningCount;

        public TaskLightNotifier()
        {
            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = SystemIcons.Application;
            notifyIcon.Text = "Codex 任务灯";
            notifyIcon.Visible = false;

            hideTimer = new Timer();
            hideTimer.Interval = 9000;
            hideTimer.Tick += delegate
            {
                hideTimer.Stop();
                notifyIcon.Visible = false;
            };
        }

        public void Update(TaskListSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            HashSet<string> currentAttentionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            TaskSnapshot newestAttention = null;
            foreach (TaskSnapshot task in snapshot.Tasks)
            {
                if (task.State != CodexTaskState.NeedsAttention)
                {
                    continue;
                }

                currentAttentionIds.Add(task.Id);
                if (!previousAttentionIds.Contains(task.Id) && newestAttention == null)
                {
                    newestAttention = task;
                }
            }

            if (hasBaseline)
            {
                if (newestAttention != null)
                {
                    Show(
                        "Codex 需要你处理",
                        string.IsNullOrWhiteSpace(newestAttention.Title)
                            ? "有一个任务失败或正在等待你的操作。"
                            : newestAttention.Title,
                        ToolTipIcon.Warning);
                }
                else if (previousRunningCount > 0 &&
                         snapshot.RunningCount == 0 &&
                         snapshot.AttentionCount == 0)
                {
                    Show("Codex 任务已完成", "所有正在执行的任务都已完成。", ToolTipIcon.Info);
                }
            }

            previousAttentionIds.Clear();
            foreach (string id in currentAttentionIds)
            {
                previousAttentionIds.Add(id);
            }

            previousRunningCount = snapshot.RunningCount;
            hasBaseline = true;
        }

        public void Reset()
        {
            hasBaseline = false;
            previousRunningCount = 0;
            previousAttentionIds.Clear();
            hideTimer.Stop();
            notifyIcon.Visible = false;
        }

        public void Dispose()
        {
            hideTimer.Stop();
            hideTimer.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
        }

        private void Show(string title, string text, ToolTipIcon icon)
        {
            hideTimer.Stop();
            notifyIcon.BalloonTipTitle = title;
            notifyIcon.BalloonTipText = text;
            notifyIcon.BalloonTipIcon = icon;
            notifyIcon.Visible = true;
            notifyIcon.ShowBalloonTip(6500);
            hideTimer.Start();
        }
    }
}
