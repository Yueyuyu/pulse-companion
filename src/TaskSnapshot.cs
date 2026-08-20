using System;
using System.Collections.Generic;

namespace CodexQuotaOverlay
{
    internal enum CodexTaskState
    {
        Completed,
        Running,
        NeedsAttention
    }

    internal sealed class TaskSnapshot
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public string WorkingDirectory { get; private set; }
        public string LogPath { get; private set; }
        public CodexTaskState State { get; private set; }
        public string Detail { get; private set; }
        public DateTimeOffset ActivityAtUtc { get; private set; }

        public TaskSnapshot(
            string id,
            string title,
            string workingDirectory,
            string logPath,
            CodexTaskState state,
            string detail,
            DateTimeOffset activityAtUtc)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            WorkingDirectory = workingDirectory ?? string.Empty;
            LogPath = logPath ?? string.Empty;
            State = state;
            Detail = detail ?? string.Empty;
            ActivityAtUtc = activityAtUtc;
        }
    }

    internal sealed class TaskListSnapshot
    {
        private readonly List<TaskSnapshot> tasks;

        public IList<TaskSnapshot> Tasks
        {
            get { return tasks.AsReadOnly(); }
        }

        public int RunningCount { get; private set; }
        public int AttentionCount { get; private set; }

        public TaskListSnapshot(IEnumerable<TaskSnapshot> source)
        {
            tasks = source == null ? new List<TaskSnapshot>() : new List<TaskSnapshot>(source);
            foreach (TaskSnapshot task in tasks)
            {
                if (task.State == CodexTaskState.Running)
                {
                    RunningCount++;
                }
                else if (task.State == CodexTaskState.NeedsAttention)
                {
                    AttentionCount++;
                }
            }
        }
    }

    internal sealed class TaskListEventArgs : EventArgs
    {
        public TaskListSnapshot Snapshot { get; private set; }

        public TaskListEventArgs(TaskListSnapshot snapshot)
        {
            Snapshot = snapshot;
        }
    }

    internal sealed class TaskActivatedEventArgs : EventArgs
    {
        public TaskSnapshot Task { get; private set; }

        public TaskActivatedEventArgs(TaskSnapshot task)
        {
            Task = task;
        }
    }

    internal sealed class TaskConnectionEventArgs : EventArgs
    {
        public bool Connected { get; private set; }
        public string Status { get; private set; }

        public TaskConnectionEventArgs(bool connected, string status)
        {
            Connected = connected;
            Status = status ?? string.Empty;
        }
    }
}
