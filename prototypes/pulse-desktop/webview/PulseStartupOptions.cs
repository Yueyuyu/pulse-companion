using System;
using System.IO;

namespace CodexCompanion.PulseWebPreview {
  internal sealed class PulseStartupOptions {
    internal bool Live, Background, InspectWindow, VerifyLive, SelfTest;
    internal string ReportPath, VisualReportPath;

    internal static PulseStartupOptions Parse(string[] args) {
      var result=new PulseStartupOptions();
      // 双击程序也只启动正常后台；示例和可见验收必须有明确参数。
      if(args.Length==0) {result.Live=result.Background=true;return result;}
      if(args.Length==2&&(args[0]=="--verify"||args[0]=="--verify-live"||args[0]=="--live-self-test")) {
        if(String.IsNullOrWhiteSpace(args[1])||args[1].StartsWith("--",StringComparison.Ordinal))throw new ArgumentException("缺少验收输出路径");
        result.ReportPath=Path.GetFullPath(args[1]);
        if(args[0]=="--verify")result.VisualReportPath=result.ReportPath;
        else if(args[0]=="--verify-live")result.Live=result.VerifyLive=true;
        else result.SelfTest=true;
        return result;
      }
      bool demo=false;
      foreach(string arg in args) {
        if(arg=="--live"&&!result.Live)result.Live=true;
        else if(arg=="--background"&&!result.Background)result.Background=true;
        else if(arg=="--inspect-window"&&!result.InspectWindow)result.InspectWindow=true;
        else if(arg=="--demo"&&!demo)demo=true;
        else throw new ArgumentException("不支持或重复的启动参数");
      }
      if((demo&&(result.Live||result.Background))||(!demo&&!result.Live)||(result.Background&&result.InspectWindow))
        throw new ArgumentException("演示、验收与后台参数不能混用");
      return result;
    }
  }

  internal static class PulseBackgroundPause {
    internal static string FilePath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexQuotaOverlay","pulse-background.pause");}}
    internal static bool IsPaused(string path) {return File.Exists(path);}
    internal static bool TryPause(string path) {
      try {Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"paused");return true;}
      catch {return false;}
    }
  }
}
