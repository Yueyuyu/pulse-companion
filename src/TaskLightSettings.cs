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

        public TaskLightSettings()
        {
            AlwaysOnTop = true;
        }

        public static TaskLightSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return new TaskLightSettings();
                }

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                IDictionary<string, object> values = serializer.DeserializeObject(
                    File.ReadAllText(SettingsPath, Encoding.UTF8)) as IDictionary<string, object>;
                if (values == null)
                {
                    return new TaskLightSettings();
                }

                TaskLightSettings settings = new TaskLightSettings();
                settings.HasPosition = GetBoolean(values, "hasPosition", false);
                settings.Left = GetInt32(values, "left", 0);
                settings.Top = GetInt32(values, "top", 0);
                settings.UseRightAnchor = GetBoolean(values, "useRightAnchor", false);
                settings.AnchorRight = GetInt32(values, "anchorRight", 0);
                settings.SavedWidth = GetInt32(values, "savedWidth", 0);
                settings.AlwaysOnTop = GetBoolean(values, "alwaysOnTop", true);
                return settings;
            }
            catch (Exception)
            {
                return new TaskLightSettings();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                Dictionary<string, object> values = new Dictionary<string, object>();
                values["hasPosition"] = HasPosition;
                values["left"] = Left;
                values["top"] = Top;
                values["useRightAnchor"] = UseRightAnchor;
                values["anchorRight"] = AnchorRight;
                values["savedWidth"] = SavedWidth;
                values["alwaysOnTop"] = AlwaysOnTop;
                string json = new JavaScriptSerializer().Serialize(values);
                string temporaryPath = SettingsPath + ".tmp";
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                if (File.Exists(SettingsPath))
                {
                    File.Replace(temporaryPath, SettingsPath, null);
                }
                else
                {
                    File.Move(temporaryPath, SettingsPath);
                }
            }
            catch (Exception)
            {
                // 设置持久化失败不应影响任务状态显示。
            }
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
