using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexQuotaOverlay
{
    internal sealed class TaskLightSettings
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexQuotaOverlay");
        private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "task-light.json");

        public bool HasPosition { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public bool UseRightAnchor { get; set; }
        public int AnchorRight { get; set; }
        public int SavedWidth { get; set; }
        public bool AlwaysOnTop { get; set; }
        public IList<WatchedTaskRecord> WatchedTasks
        {
            get { return watchedTasks.AsReadOnly(); }
        }

        private readonly List<WatchedTaskRecord> watchedTasks = new List<WatchedTaskRecord>();
        private readonly bool persistenceEnabled;
        private string persistencePath = SettingsPath;
        internal bool LoadFailed { get; private set; }

        public TaskLightSettings()
            : this(true)
        {
        }

        private TaskLightSettings(bool shouldPersist)
        {
            persistenceEnabled = shouldPersist;
            AlwaysOnTop = true;
        }

        public static TaskLightSettings CreateTransient()
        {
            return new TaskLightSettings(false);
        }

        public static TaskLightSettings Load()
        {
            return LoadFromPath(SettingsPath);
        }

        internal static TaskLightSettings LoadFromPath(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new TaskLightSettings { persistencePath = path };
                }

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                IDictionary<string, object> values = serializer.DeserializeObject(
                    File.ReadAllText(path, Encoding.UTF8)) as IDictionary<string, object>;
                if (values == null)
                {
                    return new TaskLightSettings { persistencePath = path, LoadFailed = true };
                }

                TaskLightSettings settings = new TaskLightSettings { persistencePath = path };
                settings.HasPosition = GetBoolean(values, "hasPosition", false);
                settings.Left = GetInt32(values, "left", 0);
                settings.Top = GetInt32(values, "top", 0);
                settings.UseRightAnchor = GetBoolean(values, "useRightAnchor", false);
                settings.AnchorRight = GetInt32(values, "anchorRight", 0);
                settings.SavedWidth = GetInt32(values, "savedWidth", 0);
                settings.AlwaysOnTop = GetBoolean(values, "alwaysOnTop", true);
                settings.ReplaceWatchedTasks(ParseWatchedTasks(values));
                return settings;
            }
            catch (Exception)
            {
                return new TaskLightSettings { persistencePath = path, LoadFailed = true };
            }
        }

        public void Save()
        {
            string ignored;
            TrySave(out ignored);
        }

        public bool TrySave(out string status)
        {
            status = string.Empty;
            if (LoadFailed)
            {
                status = "原关注设置无法读取，已保留文件；请修复后重新启动";
                return false;
            }
            if (!persistenceEnabled)
            {
                return true;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(persistencePath));
                Dictionary<string, object> values = new Dictionary<string, object>();
                values["hasPosition"] = HasPosition;
                values["left"] = Left;
                values["top"] = Top;
                values["useRightAnchor"] = UseRightAnchor;
                values["anchorRight"] = AnchorRight;
                values["savedWidth"] = SavedWidth;
                values["alwaysOnTop"] = AlwaysOnTop;
                values["watchedTasks"] = SerializeWatchedTasks();
                string json = new JavaScriptSerializer().Serialize(values);
                string temporaryPath = persistencePath + ".tmp";
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                if (File.Exists(persistencePath))
                {
                    File.Replace(temporaryPath, persistencePath, null);
                }
                else
                {
                    File.Move(temporaryPath, persistencePath);
                }
                return true;
            }
            catch (Exception)
            {
                // 实时界面必须能够报告失败，不能把未写入磁盘的关注冒充已保存。
                status = "关注设置保存失败，请检查本机目录后重试";
                return false;
            }
        }

        public void ReplaceWatchedTasks(IEnumerable<WatchedTaskRecord> source)
        {
            watchedTasks.Clear();
            if (source == null)
            {
                return;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (WatchedTaskRecord record in source)
            {
                Guid ignored;
                if (record == null ||
                    !Guid.TryParse(record.Id, out ignored) ||
                    !seen.Add(record.Id) ||
                    watchedTasks.Count >= WatchedTaskCollection.MaximumCount)
                {
                    continue;
                }

                watchedTasks.Add(record.Clone());
            }
        }

        public static bool RunWatchedTaskSerializationSelfTest(out string result)
        {
            try
            {
                TaskLightSettings settings = CreateTransient();
                settings.ReplaceWatchedTasks(new[]
                {
                    new WatchedTaskRecord
                    {
                        Id = "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
                        Title = "持久化测试",
                        Detail = "等待你回复",
                        State = CodexTaskState.NeedsAttention,
                        ActivityAtUtc = new DateTimeOffset(2026, 8, 20, 10, 30, 0, TimeSpan.Zero)
                    }
                });
                Dictionary<string, object> values = new Dictionary<string, object>();
                values["watchedTasks"] = settings.SerializeWatchedTasks();
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                IDictionary<string, object> roundTrip = serializer.DeserializeObject(serializer.Serialize(values)) as IDictionary<string, object>;
                List<WatchedTaskRecord> parsed = new List<WatchedTaskRecord>(ParseWatchedTasks(roundTrip));
                bool legacyCompatible = new List<WatchedTaskRecord>(ParseWatchedTasks(new Dictionary<string, object>())).Count == 0;
                bool success = parsed.Count == 1 &&
                               string.Equals(parsed[0].Id, "cccccccc-cccc-4ccc-8ccc-cccccccccccc", StringComparison.OrdinalIgnoreCase) &&
                               parsed[0].State == CodexTaskState.NeedsAttention &&
                               parsed[0].ActivityAtUtc.Year == 2026 &&
                               legacyCompatible;
                result = success
                    ? "{\"ok\":true,\"test\":\"watched-task-settings-roundtrip\"}"
                    : "{\"ok\":false,\"test\":\"watched-task-settings-roundtrip\"}";
                return success;
            }
            catch (Exception)
            {
                result = "{\"ok\":false,\"test\":\"watched-task-settings-roundtrip\"}";
                return false;
            }
        }

        private IList<Dictionary<string, object>> SerializeWatchedTasks()
        {
            List<Dictionary<string, object>> serialized = new List<Dictionary<string, object>>();
            foreach (WatchedTaskRecord record in watchedTasks)
            {
                Dictionary<string, object> value = new Dictionary<string, object>();
                value["id"] = record.Id;
                value["title"] = record.Title;
                value["detail"] = record.Detail;
                value["state"] = record.State.ToString();
                value["activityAtUtc"] = record.ActivityAtUtc == DateTimeOffset.MinValue
                    ? string.Empty
                    : record.ActivityAtUtc.ToString("o");
                serialized.Add(value);
            }

            return serialized;
        }

        private static IEnumerable<WatchedTaskRecord> ParseWatchedTasks(IDictionary<string, object> values)
        {
            object rawValue;
            object[] items;
            if (!values.TryGetValue("watchedTasks", out rawValue) || (items = rawValue as object[]) == null)
            {
                return new WatchedTaskRecord[0];
            }

            List<WatchedTaskRecord> parsed = new List<WatchedTaskRecord>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in items)
            {
                IDictionary<string, object> recordValues = item as IDictionary<string, object>;
                if (recordValues == null)
                {
                    continue;
                }

                string id = GetString(recordValues, "id", string.Empty);
                Guid ignored;
                if (!Guid.TryParse(id, out ignored) || !seen.Add(id) || parsed.Count >= WatchedTaskCollection.MaximumCount)
                {
                    continue;
                }

                DateTimeOffset activityAtUtc;
                if (!DateTimeOffset.TryParse(GetString(recordValues, "activityAtUtc", string.Empty), out activityAtUtc))
                {
                    activityAtUtc = DateTimeOffset.MinValue;
                }

                parsed.Add(new WatchedTaskRecord
                {
                    Id = id,
                    Title = GetString(recordValues, "title", "未命名 Codex 任务"),
                    Detail = GetString(recordValues, "detail", "等待状态更新"),
                    State = ParseState(GetString(recordValues, "state", CodexTaskState.Completed.ToString())),
                    ActivityAtUtc = activityAtUtc
                });
            }

            return parsed;
        }

        private static CodexTaskState ParseState(string value)
        {
            if (string.Equals(value, CodexTaskState.NeedsAttention.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return CodexTaskState.NeedsAttention;
            }

            return string.Equals(value, CodexTaskState.Running.ToString(), StringComparison.OrdinalIgnoreCase)
                ? CodexTaskState.Running
                : CodexTaskState.Completed;
        }

        private static string GetString(IDictionary<string, object> values, string key, string fallback)
        {
            object value;
            return values.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value)
                : fallback;
        }

        private static bool GetBoolean(IDictionary<string, object> values, string key, bool fallback)
        {
            object value;
            if (!values.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }

            try
            {
                return Convert.ToBoolean(value);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static int GetInt32(IDictionary<string, object> values, string key, int fallback)
        {
            object value;
            if (!values.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }

            try
            {
                return Convert.ToInt32(value);
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
