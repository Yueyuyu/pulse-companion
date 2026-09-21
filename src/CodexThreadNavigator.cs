using System;
using System.Diagnostics;

namespace CodexQuotaOverlay
{
    internal static class CodexThreadNavigator
    {
        private const string ThreadUriPrefix = "codex://threads/";

        public static bool TryOpen(TaskSnapshot task, out string status)
        {
            if (task == null)
            {
                status = "任务不存在";
                return false;
            }

            Uri uri;
            if (!TryBuildUri(task.Id, out uri))
            {
                status = "任务标识不是有效的 Codex 对话 UUID";
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                });
                status = "已打开对应的 Codex 任务";
                return true;
            }
            catch (Exception exception)
            {
                status = "无法打开 Codex：" + exception.Message;
                return false;
            }
        }

        public static bool TryBuildUri(string threadId, out Uri uri)
        {
            uri = null;
            Guid id;
            if (!Guid.TryParse(threadId, out id))
            {
                return false;
            }

            return Uri.TryCreate(ThreadUriPrefix + id.ToString("D"), UriKind.Absolute, out uri) &&
                   string.Equals(uri.Scheme, "codex", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(uri.Host, "threads", StringComparison.OrdinalIgnoreCase);
        }

        public static bool RunSelfTest(out string result)
        {
            Uri valid;
            Uri invalid;
            bool success = TryBuildUri("11111111-1111-4111-8111-111111111111", out valid) &&
                           valid != null &&
                           string.Equals(
                               valid.AbsoluteUri,
                               "codex://threads/11111111-1111-4111-8111-111111111111",
                               StringComparison.OrdinalIgnoreCase) &&
                           !TryBuildUri("../../settings", out invalid);
            result = success
                ? "{\"ok\":true,\"test\":\"codex-thread-uri\"}"
                : "{\"ok\":false,\"test\":\"codex-thread-uri\"}";
            return success;
        }
    }
}
