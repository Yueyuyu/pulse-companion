using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexQuotaOverlay
{
    internal static class TaskParser
    {
        private const int MaximumThreads = 80;
        private static readonly TimeSpan RunningFreshness = TimeSpan.FromHours(12);
        private static readonly TimeSpan FailureFreshness = TimeSpan.FromDays(3);
        private static readonly TimeSpan LogInspectionFreshness = TimeSpan.FromDays(7);

        public static TaskListSnapshot ParseResult(object resultObject, TaskLogReader logReader)
        {
            IDictionary<string, object> result = resultObject as IDictionary<string, object>;
            if (result == null || logReader == null)
            {
                return null;
            }

            object dataObject;
            IEnumerable data = result.TryGetValue("data", out dataObject) ? dataObject as IEnumerable : null;
            if (data == null || dataObject is string)
            {
                return null;
            }

            List<TaskSnapshot> tasks = new List<TaskSnapshot>();
            int parsedCount = 0;
            foreach (object itemObject in data)
            {
                if (parsedCount >= MaximumThreads)
                {
                    break;
                }

                IDictionary<string, object> item = itemObject as IDictionary<string, object>;
                if (item == null || IsBackgroundThread(item))
                {
                    continue;
                }

                parsedCount++;
                TaskSnapshot task = ParseThread(item, logReader);
                if (task != null)
                {
                    tasks.Add(task);
                }
            }

            tasks.Sort(CompareTasks);
            return new TaskListSnapshot(tasks);
        }

        private static TaskSnapshot ParseThread(IDictionary<string, object> item, TaskLogReader logReader)
        {
            string id = GetString(item, "id");
            string name = GetString(item, "name");
            string preview = GetString(item, "preview");
            string cwd = GetString(item, "cwd");
            string logPath = GetString(item, "path");
            DateTimeOffset updatedAt = FromUnixSeconds(GetLong(item, "updatedAt"));
            TaskLogResult log = updatedAt == DateTimeOffset.MinValue || IsFresh(updatedAt, LogInspectionFreshness)
                ? logReader.Read(logPath)
                : TaskLogResult.Unknown;

            string runtimeType = string.Empty;
            bool waitingOnApproval = false;
            bool waitingOnUserInput = false;
            object statusObject;
            IDictionary<string, object> status = item.TryGetValue("status", out statusObject)
                ? statusObject as IDictionary<string, object>
                : null;
            if (status != null)
            {
                runtimeType = GetString(status, "type");
                object flagsObject;
                IEnumerable flags = status.TryGetValue("activeFlags", out flagsObject)
                    ? flagsObject as IEnumerable
                    : null;
                if (flags != null && !(flagsObject is string))
                {
                    foreach (object flagObject in flags)
                    {
                        string flag = Convert.ToString(flagObject, CultureInfo.InvariantCulture) ?? string.Empty;
                        waitingOnApproval |= string.Equals(flag, "waitingOnApproval", StringComparison.Ordinal);
                        waitingOnUserInput |= string.Equals(flag, "waitingOnUserInput", StringComparison.Ordinal);
                    }
                }
            }

            CodexTaskState state;
            string detail;
            bool stateKnown = true;
            if (waitingOnApproval)
            {
                state = CodexTaskState.NeedsAttention;
                detail = "等待你批准操作";
            }
            else if (waitingOnUserInput)
            {
                state = CodexTaskState.NeedsAttention;
                detail = "等待你回复";
            }
            else if (string.Equals(runtimeType, "systemError", StringComparison.Ordinal) ||
                     (log.State == TaskLogState.Failed && IsFresh(log.LogUpdatedAtUtc, FailureFreshness)))
            {
                state = CodexTaskState.NeedsAttention;
                detail = "任务执行失败，需要你查看";
            }
            else if ((log.State == TaskLogState.Running && IsFresh(log.LogUpdatedAtUtc, RunningFreshness)) ||
                     string.Equals(runtimeType, "active", StringComparison.Ordinal))
            {
                state = CodexTaskState.Running;
                detail = "Codex 正在处理";
            }
            else if (log.State == TaskLogState.Completed && log.ActivityAtUtc != DateTimeOffset.MinValue)
            {
                state = CodexTaskState.Completed;
                detail = "已完成";
            }
            else
            {
                // notLoaded、日志缺失或过期不是完成证据；保留兼容枚举，但明确标记状态未知。
                state = CodexTaskState.Completed;
                detail = "暂不可用";
                stateKnown = false;
            }

            DateTimeOffset activityAt = log.ActivityAtUtc != DateTimeOffset.MinValue
                ? log.ActivityAtUtc
                : updatedAt;
            return new TaskSnapshot(
                id,
                BuildTitle(name, preview),
                cwd,
                logPath,
                state,
                detail,
                activityAt,
                stateKnown);
        }

        private static bool IsBackgroundThread(IDictionary<string, object> item)
        {
            string threadSource = GetString(item, "threadSource");
            if (string.Equals(threadSource, "memory_consolidation", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            object ephemeralObject;
            if (item.TryGetValue("ephemeral", out ephemeralObject) && ephemeralObject != null)
            {
                try
                {
                    return Convert.ToBoolean(ephemeralObject, CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                }
            }

            return false;
        }

        private static int CompareTasks(TaskSnapshot left, TaskSnapshot right)
        {
            int leftPriority = left.State == CodexTaskState.NeedsAttention ? 0 :
                (left.State == CodexTaskState.Running ? 1 : 2);
            int rightPriority = right.State == CodexTaskState.NeedsAttention ? 0 :
                (right.State == CodexTaskState.Running ? 1 : 2);
            int priority = leftPriority.CompareTo(rightPriority);
            if (priority != 0)
            {
                return priority;
            }

            return right.ActivityAtUtc.CompareTo(left.ActivityAtUtc);
        }

        private static string BuildTitle(string name, string preview)
        {
            string candidate = string.IsNullOrWhiteSpace(name) ? ExtractPreviewTitle(preview) : name.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                candidate = "未命名 Codex 任务";
            }

            candidate = candidate.Replace('\t', ' ');
            while (candidate.IndexOf("  ", StringComparison.Ordinal) >= 0)
            {
                candidate = candidate.Replace("  ", " ");
            }

            return candidate.Length <= 52 ? candidate : candidate.Substring(0, 51).TrimEnd() + "…";
        }

        private static string ExtractPreviewTitle(string preview)
        {
            if (string.IsNullOrWhiteSpace(preview))
            {
                return string.Empty;
            }

            string[] lines = preview.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 ||
                    line.StartsWith("# Files mentioned", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("<in-app-", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Codex could not read", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                while (line.StartsWith("#", StringComparison.Ordinal))
                {
                    line = line.Substring(1).TrimStart();
                }

                if (line.Length > 0)
                {
                    return line;
                }
            }

            return string.Empty;
        }

        private static string GetString(IDictionary<string, object> dictionary, string key)
        {
            object value;
            if (!dictionary.TryGetValue(key, out value) || value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static long GetLong(IDictionary<string, object> dictionary, string key)
        {
            object value;
            if (!dictionary.TryGetValue(key, out value) || value == null)
            {
                return 0;
            }

            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static DateTimeOffset FromUnixSeconds(long seconds)
        {
            if (seconds <= 0)
            {
                return DateTimeOffset.MinValue;
            }

            try
            {
                return new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTimeOffset.MinValue;
            }
        }

        private static bool IsFresh(DateTimeOffset value, TimeSpan maximumAge)
        {
            if (value == DateTimeOffset.MinValue)
            {
                return false;
            }

            TimeSpan age = DateTimeOffset.UtcNow - value;
            return age <= maximumAge && age >= TimeSpan.FromMinutes(-5);
        }

        public static bool RunSelfTest(out string result)
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "codex-tasklight-selftest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(testRoot);
                string runningPath = Path.Combine(testRoot, "running.jsonl");
                string failedPath = Path.Combine(testRoot, "failed.jsonl");
                // 样例随测试时间生成，仍经过生产时效判断，不放宽真实任务的新鲜度限制。
                DateTimeOffset fixtureTime = DateTimeOffset.UtcNow.AddSeconds(-5);
                string timestamp = fixtureTime.ToString("o");
                string unix = fixtureTime.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
                File.WriteAllText(
                    runningPath,
                    "{\"timestamp\":\"" + timestamp + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"started_at\":" + unix + "}}\n",
                    new UTF8Encoding(false));
                File.WriteAllText(
                    failedPath,
                    "{\"timestamp\":\"" + timestamp + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"started_at\":" + unix + "}}\n" +
                    "{\"timestamp\":\"" + timestamp + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"last_agent_message\":null,\"error\":{\"message\":\"fixture\"},\"completed_at\":" + unix + "}}\n",
                    new UTF8Encoding(false));

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string fixture = "{\"data\":[" +
                    "{\"id\":\"running\",\"name\":\"运行任务\",\"preview\":\"\",\"cwd\":\"C:\\\\work\",\"path\":" + serializer.Serialize(runningPath) + ",\"updatedAt\":" + unix + ",\"ephemeral\":false,\"status\":{\"type\":\"notLoaded\"}}," +
                    "{\"id\":\"failed\",\"name\":\"失败任务\",\"preview\":\"\",\"cwd\":\"C:\\\\work\",\"path\":" + serializer.Serialize(failedPath) + ",\"updatedAt\":" + unix + ",\"ephemeral\":false,\"status\":{\"type\":\"notLoaded\"}}]}";
                TaskListSnapshot snapshot = ParseResult(serializer.DeserializeObject(fixture), new TaskLogReader());
                bool success = snapshot != null &&
                               snapshot.RunningCount == 1 &&
                               snapshot.AttentionCount == 1 &&
                               snapshot.Tasks.Count == 2;
                result = success
                    ? "{\"ok\":true,\"test\":\"task-log-lifecycle\",\"running\":1,\"attention\":1}"
                    : "{\"ok\":false,\"test\":\"task-log-lifecycle\"}";
                return success;
            }
            catch (Exception exception)
            {
                result = "{\"ok\":false,\"test\":\"task-log-lifecycle\",\"status\":" +
                         new JavaScriptSerializer().Serialize(exception.Message) + "}";
                return false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(testRoot))
                    {
                        Directory.Delete(testRoot, true);
                    }
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
