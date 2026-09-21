using System;
using System.Collections.Generic;

namespace PulseDesktopPreview
{
    internal sealed class DemoTask
    {
        public string Id;
        public string Title;
        public string State;
        public bool Watched;
        public string StateLabel { get { return State == "running" ? "执行中" : State == "completed" ? "已完成" : State == "attention" ? "需处理" : "暂不可用"; } }
    }

    // 原型与正式数据层隔离；不得在这里接账号、日志或 App Server。
    internal sealed class DemoState
    {
        public string Mode = "compact";
        public string DataState = "ready";
        public string Side = "right";
        public bool Pinned;
        public bool MotionEnabled = true;
        public DateTime SmileUntil;
        public double Scale = 1;
        public int Remaining = 69;
        public readonly List<DemoTask> Tasks = new List<DemoTask>();
        public DemoState() { ResetTasks(); }
        public void ResetTasks()
        {
            Tasks.Clear();
            Tasks.Add(new DemoTask { Id = "demo-design", Title = "完善桌面伴侣的视觉细节", State = "running", Watched = true });
            Tasks.Add(new DemoTask { Id = "demo-tests", Title = "检查组件状态与可访问性", State = "completed", Watched = true });
            Tasks.Add(new DemoTask { Id = "demo-docs", Title = "整理新版本使用文档", State = "running", Watched = false });
        }
        public string Value { get { return DataState == "ready" ? Remaining.ToString() + "%" : "—"; } }
        public string Tone { get { return DataState != "ready" ? "muted" : Remaining <= 20 ? "red" : Remaining <= 40 ? "amber" : "green"; } }
        public string Mood
        {
            get
            {
                if(DataState=="error")return "offline";
                if(DataState=="loading")return "loading";
                if(Tasks.Exists(t=>t.State=="attention"))return "attention";
                if(SmileUntil>DateTime.UtcNow)return "happy";
                if(Tasks.Exists(t=>t.State=="running"))return "working";
                if(Tasks.Exists(t=>t.State=="unavailable"))return "offline";
                return Tasks.Count>0&&Tasks.TrueForAll(t=>t.State=="completed")?"happy":"idle";
            }
        }
        public string Status
        {
            get
            {
                if (DataState == "error") return "连接中断";
                if (DataState == "loading") return "读取中";
                int running = Tasks.FindAll(t => t.State == "running").Count;
                int attention = Tasks.FindAll(t => t.State == "attention").Count;
                return attention > 0 ? attention + " 项需处理" : running > 0 ? running + " 项执行中" : "暂无执行任务";
            }
        }
        public static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(value, Math.Max(min, max))); }
        public static void SelfTest()
        {
            DemoState state = new DemoState();
            if (state.Value != "69%" || state.Status != "2 项执行中") throw new Exception("示例初始化失败");
            state.DataState = "error";
            if (state.Value != "—" || state.Tone != "muted") throw new Exception("未知额度语义错误");
            state.DataState = "ready"; state.Remaining = 0;
            if (state.Value != "0%" || state.Tone != "red") throw new Exception("用尽额度语义错误");
            state.Tasks[0].State = "completed";
            if (!state.Tasks[0].Watched || state.Tasks.Count != 3) throw new Exception("完成后丢失关注");
            if (Clamp(-10, 0, 500) != 0 || Clamp(900, 0, 500) != 500 || Clamp(1, 0, -50) != 0) throw new Exception("位置边界错误");
        }
    }
}
