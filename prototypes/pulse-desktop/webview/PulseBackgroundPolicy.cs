namespace CodexCompanion.PulseWebPreview {
  // 与进程探测分离，离线/重开生命周期可在不关闭真实 Codex 的条件下验证。
  internal static class PulseBackgroundPolicy {
    internal static bool ShouldShow(bool background,bool applicationRunning) {return !background||applicationRunning;}
    internal static bool AcceptsApplication(string id) {return id=="codex";}
  }
}
