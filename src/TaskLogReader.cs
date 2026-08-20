using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CodexQuotaOverlay
{
    internal enum TaskLogState
    {
        Unknown,
        Running,
        Completed,
        Failed
    }

    internal sealed class TaskLogResult
    {
        public TaskLogState State { get; private set; }
        public DateTimeOffset ActivityAtUtc { get; private set; }
        public DateTimeOffset LogUpdatedAtUtc { get; private set; }
        public long EventOffset { get; private set; }

        public TaskLogResult(
            TaskLogState state,
            DateTimeOffset activityAtUtc,
            DateTimeOffset logUpdatedAtUtc,
            long eventOffset)
        {
            State = state;
            ActivityAtUtc = activityAtUtc;
            LogUpdatedAtUtc = logUpdatedAtUtc;
            EventOffset = eventOffset;
        }

        public static TaskLogResult Unknown
        {
            get { return new TaskLogResult(TaskLogState.Unknown, DateTimeOffset.MinValue, DateTimeOffset.MinValue, -1); }
        }
    }

    internal sealed class TaskLogReader
    {
        private const int BlockSize = 64 * 1024;
        private const int EventContextBefore = 1024;
        private const int EventContextAfter = 128 * 1024;
        private const string TaskStartedPattern = "\"type\":\"task_started\"";
        private const string TaskCompletePattern = "\"type\":\"task_complete\"";
        private static readonly int PatternOverlap = Math.Max(
            Encoding.ASCII.GetByteCount(TaskStartedPattern),
            Encoding.ASCII.GetByteCount(TaskCompletePattern)) - 1;

        private sealed class CacheEntry
        {
            public long Length;
            public DateTime LastWriteUtc;
            public TaskLogResult Result;
        }

        private sealed class LocatedEvent
        {
            public long Offset;
            public bool IsComplete;
        }

        private readonly object gate = new object();
        private readonly Dictionary<string, CacheEntry> cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        public TaskLogResult Read(string path)
        {
            path = NormalizePath(path);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return TaskLogResult.Unknown;
            }

            try
            {
                FileInfo info = new FileInfo(path);
                CacheEntry existing;
                lock (gate)
                {
                    cache.TryGetValue(path, out existing);
                    if (existing != null &&
                        existing.Length == info.Length &&
                        existing.LastWriteUtc == info.LastWriteTimeUtc)
                    {
                        return existing.Result;
                    }
                }

                TaskLogResult result;
                if (existing != null && info.Length >= existing.Length)
                {
                    LocatedEvent appended = FindLatestForward(path, existing.Length, info.Length);
                    result = appended != null && appended.Offset > existing.Result.EventOffset
                        ? AnalyzeEvent(path, appended)
                        : existing.Result;
                }
                else
                {
                    LocatedEvent latest = FindLatestReverse(path, info.Length);
                    result = latest == null ? TaskLogResult.Unknown : AnalyzeEvent(path, latest);
                }

                result = new TaskLogResult(
                    result.State,
                    result.ActivityAtUtc,
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                    result.EventOffset);

                CacheEntry updated = new CacheEntry
                {
                    Length = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc,
                    Result = result
                };
                lock (gate)
                {
                    cache[path] = updated;
                }

                return result;
            }
            catch (IOException)
            {
                return TaskLogResult.Unknown;
            }
            catch (UnauthorizedAccessException)
            {
                return TaskLogResult.Unknown;
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
            {
                return "\\\\" + path.Substring(8);
            }

            if (path.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(4);
            }

            return path;
        }

        private static LocatedEvent FindLatestReverse(string path, long length)
        {
            using (FileStream stream = OpenRead(path))
            {
                long cursor = Math.Min(length, stream.Length);
                while (cursor > 0)
                {
                    long start = Math.Max(0, cursor - BlockSize);
                    long end = Math.Min(stream.Length, cursor + PatternOverlap);
                    byte[] buffer = ReadRange(stream, start, checked((int)(end - start)));
                    LocatedEvent located = FindLatestInBuffer(buffer, start);
                    if (located != null)
                    {
                        return located;
                    }

                    cursor = start;
                }
            }

            return null;
        }

        private static LocatedEvent FindLatestForward(string path, long previousLength, long currentLength)
        {
            if (currentLength <= 0 || currentLength <= previousLength)
            {
                return null;
            }

            LocatedEvent latest = null;
            using (FileStream stream = OpenRead(path))
            {
                long endLimit = Math.Min(currentLength, stream.Length);
                long cursor = Math.Max(0, previousLength - PatternOverlap);
                while (cursor < endLimit)
                {
                    long end = Math.Min(endLimit, cursor + BlockSize + PatternOverlap);
                    byte[] buffer = ReadRange(stream, cursor, checked((int)(end - cursor)));
                    LocatedEvent located = FindLatestInBuffer(buffer, cursor);
                    if (located != null && (latest == null || located.Offset > latest.Offset))
                    {
                        latest = located;
                    }

                    long next = cursor + BlockSize;
                    if (next <= cursor)
                    {
                        break;
                    }

                    cursor = next;
                }
            }

            return latest;
        }

        private static LocatedEvent FindLatestInBuffer(byte[] buffer, long absoluteStart)
        {
            string text = Encoding.UTF8.GetString(buffer);
            int startedIndex = text.LastIndexOf(TaskStartedPattern, StringComparison.Ordinal);
            int completeIndex = text.LastIndexOf(TaskCompletePattern, StringComparison.Ordinal);
            if (startedIndex < 0 && completeIndex < 0)
            {
                return null;
            }

            bool isComplete = completeIndex > startedIndex;
            int selectedIndex = isComplete ? completeIndex : startedIndex;
            int selectedByteIndex = Encoding.UTF8.GetByteCount(text.Substring(0, selectedIndex));
            return new LocatedEvent
            {
                Offset = absoluteStart + selectedByteIndex,
                IsComplete = isComplete
            };
        }

        private static TaskLogResult AnalyzeEvent(string path, LocatedEvent located)
        {
            using (FileStream stream = OpenRead(path))
            {
                long contextStart = Math.Max(0, located.Offset - EventContextBefore);
                long contextEnd = Math.Min(stream.Length, located.Offset + EventContextAfter);
                byte[] buffer = ReadRange(stream, contextStart, checked((int)(contextEnd - contextStart)));
                string text = Encoding.UTF8.GetString(buffer);
                string pattern = located.IsComplete ? TaskCompletePattern : TaskStartedPattern;
                int patternIndex = text.IndexOf(pattern, StringComparison.Ordinal);
                if (patternIndex < 0)
                {
                    return new TaskLogResult(
                        located.IsComplete ? TaskLogState.Completed : TaskLogState.Running,
                        DateTimeOffset.MinValue,
                        DateTimeOffset.MinValue,
                        located.Offset);
                }

                int lineEnd = text.IndexOf('\n', patternIndex);
                if (lineEnd < 0)
                {
                    lineEnd = text.Length;
                }

                string eventText = text.Substring(patternIndex, lineEnd - patternIndex);
                DateTimeOffset activityAt = ExtractActivityTime(text, patternIndex, eventText, located.IsComplete);
                TaskLogState state = located.IsComplete && HasStructuredError(eventText)
                    ? TaskLogState.Failed
                    : (located.IsComplete ? TaskLogState.Completed : TaskLogState.Running);
                return new TaskLogResult(state, activityAt, DateTimeOffset.MinValue, located.Offset);
            }
        }

        private static DateTimeOffset ExtractActivityTime(
            string context,
            int patternIndex,
            string eventText,
            bool isComplete)
        {
            long unixSeconds;
            string key = isComplete ? "\"completed_at\":" : "\"started_at\":";
            if (TryExtractInteger(eventText, key, out unixSeconds))
            {
                return FromUnixSeconds(unixSeconds);
            }

            int timestampIndex = context.LastIndexOf("\"timestamp\":\"", patternIndex, StringComparison.Ordinal);
            if (timestampIndex >= 0)
            {
                int valueStart = timestampIndex + "\"timestamp\":\"".Length;
                int valueEnd = context.IndexOf('"', valueStart);
                DateTimeOffset parsed;
                if (valueEnd > valueStart &&
                    DateTimeOffset.TryParse(
                        context.Substring(valueStart, valueEnd - valueStart),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal,
                        out parsed))
                {
                    return parsed.ToUniversalTime();
                }
            }

            return DateTimeOffset.MinValue;
        }

        private static bool TryExtractInteger(string text, string key, out long value)
        {
            value = 0;
            int keyIndex = text.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return false;
            }

            int start = keyIndex + key.Length;
            while (start < text.Length && char.IsWhiteSpace(text[start]))
            {
                start++;
            }

            int end = start;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '-'))
            {
                end++;
            }

            return end > start &&
                   long.TryParse(text.Substring(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool HasStructuredError(string eventText)
        {
            int errorIndex = eventText.IndexOf("\"error\":", StringComparison.Ordinal);
            if (errorIndex < 0)
            {
                return false;
            }

            int valueStart = errorIndex + "\"error\":".Length;
            while (valueStart < eventText.Length && char.IsWhiteSpace(eventText[valueStart]))
            {
                valueStart++;
            }

            return valueStart < eventText.Length &&
                   !eventText.Substring(valueStart).StartsWith("null", StringComparison.Ordinal);
        }

        private static DateTimeOffset FromUnixSeconds(long unixSeconds)
        {
            try
            {
                return new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(unixSeconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTimeOffset.MinValue;
            }
        }

        private static FileStream OpenRead(string path)
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BlockSize,
                FileOptions.SequentialScan);
        }

        private static byte[] ReadRange(FileStream stream, long start, int requestedLength)
        {
            if (requestedLength <= 0)
            {
                return new byte[0];
            }

            byte[] buffer = new byte[requestedLength];
            stream.Seek(start, SeekOrigin.Begin);
            int total = 0;
            while (total < requestedLength)
            {
                int read = stream.Read(buffer, total, requestedLength - total);
                if (read <= 0)
                {
                    break;
                }

                total += read;
            }

            if (total == requestedLength)
            {
                return buffer;
            }

            byte[] trimmed = new byte[total];
            Buffer.BlockCopy(buffer, 0, trimmed, 0, total);
            return trimmed;
        }
    }
}
