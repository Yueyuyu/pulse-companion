using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexQuotaOverlay
{
    internal sealed class AppServerClient : IDisposable
    {
        private readonly object gate = new object();
        private readonly AutoResetEvent wakeEvent = new AutoResetEvent(false);
        private readonly TaskLogReader taskLogReader = new TaskLogReader();
        private Thread worker;
        private Process process;
        private StreamWriter input;
        private volatile bool stopping;
        private volatile bool initialized;
        private int nextRequestId = 10;
        private int latestRateLimitsRequestId;
        private int latestTaskListRequestId;
        private int taskListRequestPending;
        private int rateLimitsRequestPending;
        private int quotaRefreshQueued;
        private int rateLimitsRequestedThisSession;
        private DateTime lastRefreshRequestUtc = DateTime.MinValue;
        private DateTime lastTaskRefreshRequestUtc = DateTime.MinValue;

        public event EventHandler<QuotaEventArgs> QuotaUpdated;
        public event EventHandler<TaskListEventArgs> TasksUpdated;
        public event EventHandler<TaskConnectionEventArgs> TaskConnectionChanged;
        public event EventHandler<StatusEventArgs> StatusChanged;
        public event EventHandler<TaskConnectionEventArgs> QuotaConnectionChanged;

        public void Start()
        {
            lock (gate)
            {
                if (worker != null)
                {
                    return;
                }

                stopping = false;
                worker = new Thread(WorkerLoop);
                worker.IsBackground = true;
                worker.Name = "Codex quota app-server client";
                worker.Start();
            }
        }

        public void RequestRefresh()
        {
            if (!initialized)
            {
                Interlocked.Exchange(ref quotaRefreshQueued, 1);
                wakeEvent.Set();
                return;
            }

            Interlocked.Exchange(ref quotaRefreshQueued, 1);
            TrySendRateLimitsRead();
        }

        public void RequestTaskRefresh()
        {
            if (!initialized)
            {
                wakeEvent.Set();
                return;
            }

            if ((DateTime.UtcNow - lastTaskRefreshRequestUtc) < TimeSpan.FromSeconds(1))
            {
                return;
            }

            SendThreadList();
        }

        private void WorkerLoop()
        {
            while (!stopping)
            {
                try
                {
                    RunSession();
                }
                catch (Exception)
                {
                    if (!stopping)
                    {
                        RaiseStatus("额度服务暂时不可用，正在自动重试");
                        RaiseQuotaConnection(false);
                        RaiseTaskConnection(false, "Codex 状态服务暂时不可用，正在重试");
                    }
                }
                finally
                {
                    CloseProcess();
                }

                if (!stopping)
                {
                    wakeEvent.WaitOne(TimeSpan.FromSeconds(5));
                }
            }
        }

        private void RunSession()
        {
            string executable = FindCodexExecutable();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = executable;
            startInfo.Arguments = "app-server";
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = new UTF8Encoding(false);
            startInfo.StandardErrorEncoding = new UTF8Encoding(false);

            Process sessionProcess = new Process();
            sessionProcess.StartInfo = startInfo;
            sessionProcess.EnableRaisingEvents = true;
            sessionProcess.ErrorDataReceived += delegate { };
            if (!sessionProcess.Start())
            {
                throw new InvalidOperationException("无法启动 Codex App Server");
            }

            sessionProcess.BeginErrorReadLine();
            lock (gate)
            {
                process = sessionProcess;
                input = sessionProcess.StandardInput;
            }

            const int initializeId = 1;
            SendRaw("{\"method\":\"initialize\",\"id\":1,\"params\":{\"clientInfo\":{\"name\":\"codex_quota_overlay\",\"title\":\"Codex Status Overlay\",\"version\":\"2.0.0\"},\"capabilities\":{\"experimentalApi\":false,\"optOutNotificationMethods\":[\"remoteControl/status/changed\"]}}}");

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 16 * 1024 * 1024;
            serializer.RecursionLimit = 512;

            string line;
            bool handshakeComplete = false;
            while (!stopping && !sessionProcess.HasExited && (line = sessionProcess.StandardOutput.ReadLine()) != null)
            {
                IDictionary<string, object> message;
                try
                {
                    message = serializer.DeserializeObject(line) as IDictionary<string, object>;
                }
                catch (Exception)
                {
                    RaiseTaskConnection(false, "Codex 返回了暂不兼容的任务数据");
                    continue;
                }

                if (message == null)
                {
                    continue;
                }

                if (!handshakeComplete && IsResponseId(message, initializeId))
                {
                    object error;
                    if (message.TryGetValue("error", out error) && error != null)
                    {
                        throw new InvalidOperationException("Codex App Server 初始化失败");
                    }

                    SendRaw("{\"method\":\"initialized\",\"params\":{}}");
                    handshakeComplete = true;
                    initialized = true;
                    SendThreadList();
                    RaiseStatus("正在读取本周额度");
                    RaiseTaskConnection(true, "正在读取 Codex 任务");
                    continue;
                }

                if (!handshakeComplete)
                {
                    continue;
                }

                HandleMessage(message);
            }

            if (!stopping)
            {
                throw new IOException("Codex App Server 连接已结束");
            }
        }

        private void HandleMessage(IDictionary<string, object> message)
        {
            object methodObject;
            if (message.TryGetValue("method", out methodObject))
            {
                string method = Convert.ToString(methodObject, CultureInfo.InvariantCulture);
                if (string.Equals(method, "account/rateLimits/updated", StringComparison.Ordinal))
                {
                    if ((DateTime.UtcNow - lastRefreshRequestUtc) > TimeSpan.FromSeconds(2))
                    {
                        RequestRefresh();
                    }

                    return;
                }

                if (string.Equals(method, "thread/status/changed", StringComparison.Ordinal) ||
                    string.Equals(method, "thread/name/updated", StringComparison.Ordinal))
                {
                    RequestTaskRefresh();
                    return;
                }
            }

            object resultObject;
            if (message.TryGetValue("result", out resultObject))
            {
                int responseId;
                bool hasResponseId = TryGetResponseId(message, out responseId);
                if (!hasResponseId || responseId == latestRateLimitsRequestId)
                {
                    try
                    {
                        QuotaSnapshot snapshot = QuotaParser.ParseResult(resultObject);
                        if (snapshot != null)
                        {
                            RaiseQuota(snapshot);
                            RaiseQuotaConnection(true);
                            RaiseStatus("额度已更新");
                        }
                        else if (ContainsRateLimitPayload(resultObject))
                        {
                            RaiseStatus("没有找到可显示的周额度窗口");
                            RaiseQuotaConnection(false);
                        }
                    }
                    finally
                    {
                        if (hasResponseId && responseId == latestRateLimitsRequestId)
                        {
                            Interlocked.Exchange(ref rateLimitsRequestPending, 0);
                        }
                    }
                }

                if (!hasResponseId || responseId == latestTaskListRequestId)
                {
                    try
                    {
                        TaskListSnapshot taskSnapshot = TaskParser.ParseResult(resultObject, taskLogReader);
                        if (taskSnapshot != null)
                        {
                            RaiseTasks(taskSnapshot);
                            RaiseTaskConnection(true, "Codex 任务已更新");
                        }
                    }
                    finally
                    {
                        if (hasResponseId && responseId == latestTaskListRequestId)
                        {
                            Interlocked.Exchange(ref taskListRequestPending, 0);
                            EnsureInitialRateLimitsRequest();
                        }
                    }
                }
            }
            else
            {
                int errorResponseId;
                object errorObject;
                if (TryGetResponseId(message, out errorResponseId) &&
                    message.TryGetValue("error", out errorObject))
                {
                    if (errorResponseId == latestTaskListRequestId)
                    {
                        Interlocked.Exchange(ref taskListRequestPending, 0);
                        EnsureInitialRateLimitsRequest();
                        RaiseTaskConnection(false, "读取 Codex 任务失败，正在重试");
                    }
                    else if (errorResponseId == latestRateLimitsRequestId)
                    {
                        Interlocked.Exchange(ref rateLimitsRequestPending, 0);
                        RaiseStatus("额度读取失败，正在自动重试");
                        RaiseQuotaConnection(false);
                    }
                }
            }
        }

        private static bool ContainsRateLimitPayload(object resultObject)
        {
            IDictionary<string, object> result = resultObject as IDictionary<string, object>;
            return result != null &&
                   (result.ContainsKey("rateLimits") || result.ContainsKey("rateLimitsByLimitId"));
        }

        private void TrySendRateLimitsRead()
        {
            if (Interlocked.CompareExchange(ref taskListRequestPending, 0, 0) != 0 ||
                Interlocked.CompareExchange(ref rateLimitsRequestPending, 1, 0) != 0)
            {
                return;
            }

            Interlocked.Exchange(ref quotaRefreshQueued, 0);
            Interlocked.Exchange(ref rateLimitsRequestedThisSession, 1);
            int id = Interlocked.Increment(ref nextRequestId);
            latestRateLimitsRequestId = id;
            lastRefreshRequestUtc = DateTime.UtcNow;
            SendRaw("{\"method\":\"account/rateLimits/read\",\"id\":" + id.ToString(CultureInfo.InvariantCulture) + "}");
        }

        private void SendThreadList()
        {
            if (Interlocked.CompareExchange(ref rateLimitsRequestPending, 0, 0) != 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref taskListRequestPending, 1, 0) != 0)
            {
                return;
            }

            int id = Interlocked.Increment(ref nextRequestId);
            latestTaskListRequestId = id;
            lastTaskRefreshRequestUtc = DateTime.UtcNow;
            SendRaw(
                "{\"method\":\"thread/list\",\"id\":" + id.ToString(CultureInfo.InvariantCulture) +
                ",\"params\":{\"archived\":false,\"limit\":80,\"sortKey\":\"updated_at\",\"sortDirection\":\"desc\",\"useStateDbOnly\":true}}");
        }

        private void EnsureInitialRateLimitsRequest()
        {
            if (Interlocked.CompareExchange(ref rateLimitsRequestedThisSession, 0, 0) == 0)
            {
                Interlocked.Exchange(ref quotaRefreshQueued, 1);
            }

            if (Interlocked.CompareExchange(ref quotaRefreshQueued, 0, 0) != 0)
            {
                TrySendRateLimitsRead();
            }
        }

        private void SendRaw(string jsonLine)
        {
            lock (gate)
            {
                if (input == null)
                {
                    return;
                }

                input.WriteLine(jsonLine);
                input.Flush();
            }
        }

        private static bool IsResponseId(IDictionary<string, object> message, int expectedId)
        {
            object idObject;
            if (!message.TryGetValue("id", out idObject) || idObject == null)
            {
                return false;
            }

            try
            {
                return Convert.ToInt32(idObject, CultureInfo.InvariantCulture) == expectedId;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryGetResponseId(IDictionary<string, object> message, out int responseId)
        {
            responseId = 0;
            object idObject;
            if (!message.TryGetValue("id", out idObject) || idObject == null)
            {
                return false;
            }

            try
            {
                responseId = Convert.ToInt32(idObject, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static string FindCodexExecutable()
        {
            string explicitPath = Environment.GetEnvironmentVariable("CODEX_QUOTA_CODEX_PATH");
            if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            {
                return explicitPath;
            }

            string binRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenAI",
                "Codex",
                "bin");

            // 新版 Codex 会把当前 CLI 放在带哈希的子目录中；顶层入口可能仍指向旧版本。
            List<string> nestedCandidates = new List<string>();
            try
            {
                if (Directory.Exists(binRoot))
                {
                    foreach (string directory in Directory.GetDirectories(binRoot))
                    {
                        string candidate = Path.Combine(directory, "codex.exe");
                        if (File.Exists(candidate))
                        {
                            nestedCandidates.Add(candidate);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            nestedCandidates.Sort(delegate(string left, string right)
            {
                DateTime leftTime = GetLastWriteTimeUtc(left);
                DateTime rightTime = GetLastWriteTimeUtc(right);
                int timeComparison = rightTime.CompareTo(leftTime);
                return timeComparison != 0
                    ? timeComparison
                    : string.Compare(right, left, StringComparison.OrdinalIgnoreCase);
            });
            if (nestedCandidates.Count > 0)
            {
                return nestedCandidates[0];
            }

            string topLevelPath = Path.Combine(binRoot, "codex.exe");
            if (File.Exists(topLevelPath))
            {
                return topLevelPath;
            }

            return "codex.exe";
        }

        private static DateTime GetLastWriteTimeUtc(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        private void RaiseQuota(QuotaSnapshot snapshot)
        {
            EventHandler<QuotaEventArgs> handler = QuotaUpdated;
            if (handler != null)
            {
                handler(this, new QuotaEventArgs(snapshot));
            }
        }

        private void RaiseQuotaConnection(bool connected)
        {
            EventHandler<TaskConnectionEventArgs> handler = QuotaConnectionChanged;
            if (handler != null) handler(this, new TaskConnectionEventArgs(connected,
                connected ? "额度已更新" : "无法确认最新周额度"));
        }

        private void RaiseTasks(TaskListSnapshot snapshot)
        {
            EventHandler<TaskListEventArgs> handler = TasksUpdated;
            if (handler != null)
            {
                handler(this, new TaskListEventArgs(snapshot));
            }
        }

        private void RaiseTaskConnection(bool connected, string status)
        {
            EventHandler<TaskConnectionEventArgs> handler = TaskConnectionChanged;
            if (handler != null)
            {
                handler(this, new TaskConnectionEventArgs(connected, status));
            }
        }

        private void RaiseStatus(string status)
        {
            EventHandler<StatusEventArgs> handler = StatusChanged;
            if (handler != null)
            {
                handler(this, new StatusEventArgs(status));
            }
        }

        private void CloseProcess()
        {
            initialized = false;
            Interlocked.Exchange(ref taskListRequestPending, 0);
            Interlocked.Exchange(ref rateLimitsRequestPending, 0);
            Interlocked.Exchange(ref rateLimitsRequestedThisSession, 0);
            Process processToClose;
            lock (gate)
            {
                processToClose = process;
                process = null;
                input = null;
            }

            if (processToClose != null)
            {
                try
                {
                    if (!processToClose.HasExited)
                    {
                        processToClose.Kill();
                    }
                }
                catch (Exception)
                {
                }

                processToClose.Dispose();
            }
        }

        public void Dispose()
        {
            stopping = true;
            wakeEvent.Set();
            CloseProcess();

            Thread threadToJoin;
            lock (gate)
            {
                threadToJoin = worker;
                worker = null;
            }

            if (threadToJoin != null && threadToJoin != Thread.CurrentThread)
            {
                threadToJoin.Join(TimeSpan.FromSeconds(2));
            }

            wakeEvent.Dispose();
        }

        public static bool TryReadOnce(TimeSpan timeout, out QuotaSnapshot snapshot, out string status)
        {
            QuotaSnapshot localSnapshot = null;
            string localStatus = "未收到额度数据";
            using (ManualResetEvent completed = new ManualResetEvent(false))
            using (AppServerClient client = new AppServerClient())
            {
                client.QuotaUpdated += delegate(object sender, QuotaEventArgs args)
                {
                    localSnapshot = args.Snapshot;
                    localStatus = "额度已更新";
                    completed.Set();
                };
                client.StatusChanged += delegate(object sender, StatusEventArgs args)
                {
                    localStatus = args.Status;
                };
                client.Start();
                bool signaled = completed.WaitOne(timeout);
                snapshot = localSnapshot;
                status = localStatus;
                return signaled && localSnapshot != null;
            }
        }

        public static bool TryReadTasksOnce(TimeSpan timeout, out TaskListSnapshot snapshot, out string status)
        {
            TaskListSnapshot localSnapshot = null;
            string localStatus = "未收到任务数据";
            using (ManualResetEvent completed = new ManualResetEvent(false))
            using (AppServerClient client = new AppServerClient())
            {
                client.TasksUpdated += delegate(object sender, TaskListEventArgs args)
                {
                    localSnapshot = args.Snapshot;
                    localStatus = "Codex 任务已更新";
                    completed.Set();
                };
                client.TaskConnectionChanged += delegate(object sender, TaskConnectionEventArgs args)
                {
                    localStatus = args.Status;
                };
                client.Start();
                bool signaled = completed.WaitOne(timeout);
                snapshot = localSnapshot;
                status = localStatus;
                return signaled && localSnapshot != null;
            }
        }
    }
}
