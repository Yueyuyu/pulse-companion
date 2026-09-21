using System;
using System.Collections.Generic;

namespace CodexQuotaOverlay
{
    internal enum WatchToggleResult
    {
        Added,
        Removed,
        LimitReached,
        InvalidTask
    }

    internal sealed class WatchedTaskRecord
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public CodexTaskState State { get; set; }
        public DateTimeOffset ActivityAtUtc { get; set; }

        public WatchedTaskRecord()
        {
            Id = string.Empty;
            Title = string.Empty;
            Detail = string.Empty;
            State = CodexTaskState.Completed;
            ActivityAtUtc = DateTimeOffset.MinValue;
        }

        public WatchedTaskRecord(TaskSnapshot task)
            : this()
        {
            UpdateFrom(task);
        }

        public WatchedTaskRecord Clone()
        {
            return new WatchedTaskRecord
            {
                Id = Id,
                Title = Title,
                Detail = Detail,
                State = State,
                ActivityAtUtc = ActivityAtUtc
            };
        }

        public bool UpdateFrom(TaskSnapshot task)
        {
            if (task == null)
            {
                return false;
            }

            bool changed = !string.Equals(Id, task.Id, StringComparison.OrdinalIgnoreCase) ||
                           !string.Equals(Title, task.Title, StringComparison.Ordinal) ||
                           !string.Equals(Detail, task.Detail, StringComparison.Ordinal) ||
                           State != task.State ||
                           ActivityAtUtc != task.ActivityAtUtc;
            Id = task.Id ?? string.Empty;
            Title = task.Title ?? string.Empty;
            Detail = task.Detail ?? string.Empty;
            State = task.State;
            ActivityAtUtc = task.ActivityAtUtc;
            return changed;
        }

        public TaskSnapshot ToTaskSnapshot()
        {
            return new TaskSnapshot(
                Id,
                string.IsNullOrWhiteSpace(Title) ? "未命名 Codex 任务" : Title,
                string.Empty,
                string.Empty,
                State,
                string.IsNullOrWhiteSpace(Detail) ? "等待状态更新" : Detail,
                ActivityAtUtc);
        }
    }

    internal sealed class WatchedTaskView
    {
        public TaskSnapshot Task { get; private set; }
        public bool Available { get; private set; }

        public WatchedTaskView(TaskSnapshot task, bool available)
        {
            Task = task;
            Available = available;
        }
    }

    /// <summary>
    /// 保存用户主动关注的任务。列表顺序只由用户选择顺序决定；状态刷新不会重排，
    /// 避免任务状态变化时常驻面板突然跳动。
    /// </summary>
    internal sealed class WatchedTaskCollection
    {
        public const int MaximumCount = 5;

        private readonly List<WatchedTaskRecord> records = new List<WatchedTaskRecord>();

        public int Count
        {
            get { return records.Count; }
        }

        public IList<WatchedTaskRecord> Records
        {
            get { return records.AsReadOnly(); }
        }

        public WatchedTaskCollection(IEnumerable<WatchedTaskRecord> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (WatchedTaskRecord record in source)
            {
                if (record == null || !IsValidTaskId(record.Id) || Contains(record.Id) || records.Count >= MaximumCount)
                {
                    continue;
                }

                records.Add(record.Clone());
            }
        }

        public bool Contains(string taskId)
        {
            return FindIndex(taskId) >= 0;
        }

        public WatchToggleResult Toggle(TaskSnapshot task)
        {
            if (task == null || !IsValidTaskId(task.Id))
            {
                return WatchToggleResult.InvalidTask;
            }

            int existingIndex = FindIndex(task.Id);
            if (existingIndex >= 0)
            {
                records.RemoveAt(existingIndex);
                return WatchToggleResult.Removed;
            }

            if (records.Count >= MaximumCount)
            {
                return WatchToggleResult.LimitReached;
            }

            records.Add(new WatchedTaskRecord(task));
            return WatchToggleResult.Added;
        }

        public IList<WatchedTaskView> BuildViews(TaskListSnapshot snapshot, bool connected, out bool recordsChanged)
        {
            recordsChanged = false;
            Dictionary<string, TaskSnapshot> current = new Dictionary<string, TaskSnapshot>(StringComparer.OrdinalIgnoreCase);
            if (snapshot != null)
            {
                foreach (TaskSnapshot task in snapshot.Tasks)
                {
                    if (task != null && !string.IsNullOrWhiteSpace(task.Id))
                    {
                        current[task.Id] = task;
                    }
                }
            }

            List<WatchedTaskView> views = new List<WatchedTaskView>();
            foreach (WatchedTaskRecord record in records)
            {
                TaskSnapshot currentTask;
                if (connected && current.TryGetValue(record.Id, out currentTask) && currentTask.StateKnown)
                {
                    recordsChanged = record.UpdateFrom(currentTask) || recordsChanged;
                    views.Add(new WatchedTaskView(currentTask, true));
                }
                else
                {
                    views.Add(new WatchedTaskView(record.ToTaskSnapshot(), false));
                }
            }

            return views.AsReadOnly();
        }

        public static bool RunSelfTest(out string result)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            WatchedTaskCollection collection = new WatchedTaskCollection(null);
            TaskSnapshot first = CreateFixture("11111111-1111-4111-8111-111111111111", "任务一", CodexTaskState.Running, now);
            bool added = collection.Toggle(first) == WatchToggleResult.Added;
            for (int index = 2; index <= MaximumCount; index++)
            {
                string id = index.ToString("00000000") + "-1111-4111-8111-111111111111";
                collection.Toggle(CreateFixture(id, "任务" + index.ToString(), CodexTaskState.Running, now));
            }

            TaskSnapshot overflow = CreateFixture("99999999-1111-4111-8111-111111111111", "超出上限", CodexTaskState.Running, now);
            bool limited = collection.Toggle(overflow) == WatchToggleResult.LimitReached;
            TaskSnapshot completed = CreateFixture(first.Id, first.Title, CodexTaskState.Completed, now.AddMinutes(1));
            bool changed;
            IList<WatchedTaskView> views = collection.BuildViews(
                new TaskListSnapshot(new[] { completed }),
                true,
                out changed);
            bool retainedCompleted = views.Count == MaximumCount &&
                                     views[0].Available &&
                                     views[0].Task.State == CodexTaskState.Completed;
            bool missingRetained = !views[1].Available;
            bool removed = collection.Toggle(completed) == WatchToggleResult.Removed && collection.Count == MaximumCount - 1;
            bool success = added && limited && changed && retainedCompleted && missingRetained && removed;
            result = success
                ? "{\"ok\":true,\"test\":\"watched-task-lifecycle\",\"maximum\":" + MaximumCount.ToString() + "}"
                : "{\"ok\":false,\"test\":\"watched-task-lifecycle\"}";
            return success;
        }

        private int FindIndex(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId))
            {
                return -1;
            }

            for (int index = 0; index < records.Count; index++)
            {
                if (string.Equals(records[index].Id, taskId, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool IsValidTaskId(string taskId)
        {
            Guid ignored;
            return Guid.TryParse(taskId, out ignored);
        }

        private static TaskSnapshot CreateFixture(string id, string title, CodexTaskState state, DateTimeOffset activityAtUtc)
        {
            return new TaskSnapshot(
                id,
                title,
                string.Empty,
                string.Empty,
                state,
                state == CodexTaskState.Completed ? "已完成" : "Codex 正在处理",
                activityAtUtc);
        }
    }
}
