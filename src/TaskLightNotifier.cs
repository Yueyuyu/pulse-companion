using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CodexQuotaOverlay
{
    internal sealed class TaskLightNotifier : IDisposable
    {
        private readonly NotifyIcon fallbackNotifyIcon;
        private readonly Timer fallbackHideTimer;
        private readonly TaskToastForm toast;
        private readonly HashSet<string> previousAttentionIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TaskSnapshot> previousRunningTasks =
            new Dictionary<string, TaskSnapshot>(StringComparer.OrdinalIgnoreCase);
        private bool hasBaseline;
        private TaskSnapshot fallbackTask;
        private readonly Action<TaskToastKind, TaskSnapshot> testSink;

        public event EventHandler<TaskActivatedEventArgs> TaskActivated;

        public TaskLightNotifier()
            : this(null)
        {
        }

        internal TaskLightNotifier(Action<TaskToastKind, TaskSnapshot> isolatedTestSink)
        {
            testSink = isolatedTestSink;
            toast = new TaskToastForm();
            toast.TaskActivated += ForwardTaskActivated;

            // 只在自绘通知无法显示时启用系统托盘气泡，避免正常情况下出现双重提醒。
            fallbackNotifyIcon = new NotifyIcon();
            fallbackNotifyIcon.Icon = SystemIcons.Application;
            fallbackNotifyIcon.Text = "Codex 任务灯";
            fallbackNotifyIcon.Visible = false;
            fallbackNotifyIcon.BalloonTipClicked += delegate
            {
                TaskSnapshot task = fallbackTask;
                if (task != null)
                {
                    RaiseTaskActivated(task);
                }
            };

            fallbackHideTimer = new Timer();
            fallbackHideTimer.Interval = 9000;
            fallbackHideTimer.Tick += delegate
            {
                fallbackHideTimer.Stop();
                fallbackNotifyIcon.Visible = false;
                fallbackTask = null;
            };
        }

        public void Update(TaskListSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            HashSet<string> currentAttentionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, TaskSnapshot> currentRunningTasks =
                new Dictionary<string, TaskSnapshot>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, TaskSnapshot> currentTasks =
                new Dictionary<string, TaskSnapshot>(StringComparer.OrdinalIgnoreCase);
            TaskSnapshot newestAttention = null;

            foreach (TaskSnapshot task in snapshot.Tasks)
            {
                if (!string.IsNullOrWhiteSpace(task.Id))
                {
                    currentTasks[task.Id] = task;
                }

                if (task.State == CodexTaskState.NeedsAttention)
                {
                    currentAttentionIds.Add(task.Id);
                    if (!previousAttentionIds.Contains(task.Id) && newestAttention == null)
                    {
                        newestAttention = task;
                    }
                }
                else if (task.State == CodexTaskState.Running && !string.IsNullOrWhiteSpace(task.Id))
                {
                    currentRunningTasks[task.Id] = task;
                }
            }

            if (hasBaseline)
            {
                if (newestAttention != null)
                {
                    Show(
                        TaskToastKind.NeedsAttention,
                        newestAttention,
                        "Codex 需要你处理",
                        DisplayTitle(newestAttention),
                        "点击打开对应任务");
                }
                else
                {
                    List<TaskSnapshot> completed = FindCompletedTasks(currentTasks);
                    // 快照截断、任务暂时消失或断线都不是完成证据，必须命中同一 ID 的完成状态。

                    if (completed.Count > 0)
                    {
                        TaskSnapshot newestCompleted = completed[0];
                        string detail = completed.Count == 1
                            ? "点击打开对应任务"
                            : "另有 " + (completed.Count - 1).ToString() + " 个任务也已完成 · 点击查看";
                        Show(
                            TaskToastKind.Completed,
                            newestCompleted,
                            "Codex 任务已完成",
                            DisplayTitle(newestCompleted),
                            detail);
                    }
                }
            }

            previousAttentionIds.Clear();
            foreach (string id in currentAttentionIds)
            {
                previousAttentionIds.Add(id);
            }

            previousRunningTasks.Clear();
            foreach (KeyValuePair<string, TaskSnapshot> pair in currentRunningTasks)
            {
                previousRunningTasks[pair.Key] = pair.Value;
            }

            hasBaseline = true;
        }

        public void ShowNavigationError(TaskSnapshot task, string status)
        {
            Show(
                TaskToastKind.NavigationError,
                task,
                "无法打开 Codex 任务",
                DisplayTitle(task),
                string.IsNullOrWhiteSpace(status) ? "请确认 Codex Desktop 已正确安装" : status);
        }

        public void Reset()
        {
            hasBaseline = false;
            previousAttentionIds.Clear();
            previousRunningTasks.Clear();
            toast.CloseToast(false);
            fallbackHideTimer.Stop();
            fallbackNotifyIcon.Visible = false;
            fallbackTask = null;
        }

        public void Dispose()
        {
            toast.TaskActivated -= ForwardTaskActivated;
            toast.CloseToast(false);
            toast.Dispose();
            fallbackHideTimer.Stop();
            fallbackHideTimer.Dispose();
            fallbackNotifyIcon.Visible = false;
            fallbackNotifyIcon.Dispose();
        }

        private List<TaskSnapshot> FindCompletedTasks(Dictionary<string, TaskSnapshot> currentTasks)
        {
            List<TaskSnapshot> completed = new List<TaskSnapshot>();
            foreach (KeyValuePair<string, TaskSnapshot> previous in previousRunningTasks)
            {
                TaskSnapshot current;
                if (currentTasks.TryGetValue(previous.Key, out current) && current.StateKnown &&
                    current.State == CodexTaskState.Completed && current.ActivityAtUtc >= previous.Value.ActivityAtUtc)
                {
                    completed.Add(current);
                }
            }

            completed.Sort(delegate(TaskSnapshot left, TaskSnapshot right)
            {
                return right.ActivityAtUtc.CompareTo(left.ActivityAtUtc);
            });
            return completed;
        }

        private void Show(TaskToastKind kind, TaskSnapshot task, string title, string body, string detail)
        {
            if (testSink != null) { testSink(kind, task); return; }
            fallbackHideTimer.Stop();
            fallbackNotifyIcon.Visible = false;
            fallbackTask = null;
            if (toast.ShowNotification(kind, task, title, body, detail))
            {
                return;
            }

            fallbackTask = task;
            fallbackNotifyIcon.BalloonTipTitle = title;
            fallbackNotifyIcon.BalloonTipText = body + (string.IsNullOrWhiteSpace(detail) ? string.Empty : Environment.NewLine + detail);
            fallbackNotifyIcon.BalloonTipIcon = kind == TaskToastKind.NeedsAttention || kind == TaskToastKind.NavigationError
                ? ToolTipIcon.Warning
                : ToolTipIcon.Info;
            fallbackNotifyIcon.Visible = true;
            fallbackNotifyIcon.ShowBalloonTip(6500);
            fallbackHideTimer.Start();
        }

        private void ForwardTaskActivated(object sender, TaskActivatedEventArgs args)
        {
            RaiseTaskActivated(args == null ? null : args.Task);
        }

        private void RaiseTaskActivated(TaskSnapshot task)
        {
            if (task == null)
            {
                return;
            }

            EventHandler<TaskActivatedEventArgs> handler = TaskActivated;
            if (handler != null)
            {
                handler(this, new TaskActivatedEventArgs(task));
            }
        }

        private static string DisplayTitle(TaskSnapshot task)
        {
            return task == null || string.IsNullOrWhiteSpace(task.Title)
                ? "未命名 Codex 任务"
                : task.Title;
        }
    }
}
