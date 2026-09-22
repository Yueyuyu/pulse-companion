using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CodexQuotaOverlay;

namespace CodexCompanion.PulseWebPreview {
  // 是否打开只用于显示和轮询门控，不代表任务是否执行或额度是否可用。
  internal static class PulseApplicationPresence {
    static readonly Dictionary<string,string[]> ProcessNames=new Dictionary<string,string[]> {
      {"codex",new[]{"ChatGPT","Codex"}}, {"cursor",new[]{"Cursor"}}, {"claude",new[]{"Claude"}},
      {"grok-bot",new[]{"Grok Bot"}}, {"zcode",new[]{"ZCode"}}, {"kimi",new[]{"Kimi"}},
      {"doubao-work",new[]{"DoubaoWork"}}, {"workbuddy",new[]{"WorkBuddy"}}
    };
    internal static string[] Select(bool codexDesktopRunning,IEnumerable<string> windowProcessNames) {
      var names=new HashSet<string>(windowProcessNames,StringComparer.OrdinalIgnoreCase);
      return PulseApplications.Ids.Where(id=>(id!="codex"||codexDesktopRunning)&&ProcessNames[id].Any(names.Contains)).ToArray();
    }
    internal static string[] Read() {
      var windowProcesses=new HashSet<uint>();
      // 只枚举顶层窗口的 PID/可见标记；不读标题、会话或窗口内容。
      NativeMethods.EnumWindows(delegate(IntPtr window,IntPtr parameter) {
        if(NativeMethods.IsWindowVisible(window)&&(NativeMethods.GetWindowLong(window,NativeMethods.GwlExStyle)&NativeMethods.WsExToolWindow)==0) {
          uint pid;NativeMethods.GetWindowThreadProcessId(window,out pid);windowProcesses.Add(pid);
        }
        // 最小化窗口仍有 WS_VISIBLE，不按前台焦点或 IsIconic 过滤。
        return true;
      },IntPtr.Zero);
      var openedNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      int session;using(var current=Process.GetCurrentProcess())session=current.SessionId;
      foreach(string name in ProcessNames.Values.SelectMany(names=>names).Distinct(StringComparer.OrdinalIgnoreCase)) {
        Process[] processes;try{processes=Process.GetProcessesByName(name);}catch{continue;}
        foreach(var process in processes)using(process) {
          try {if(process.SessionId==session&&windowProcesses.Contains((uint)process.Id))openedNames.Add(name);}catch { }
        }
      }
      // 保留 Codex 桌面身份校验，不能把后台 codex.exe CLI 算成已打开桌面应用。
      return Select(CodexWindowTracker.IsCodexDesktopRunning(),openedNames);
    }
  }
}
