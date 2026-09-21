using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace CodexCompanion.PulseWebPreview {
  internal static class Program {
    [STAThread] public static int Main(string[] args) {
      System.Windows.Forms.Application.EnableVisualStyles();
      if(args.Length==2&&args[0]=="--live-self-test")return PulseLiveVerification.SelfTest(args[1]);
      bool verifyLive=args.Length==2&&args[0]=="--verify-live";
      bool live=verifyLive||Array.IndexOf(args,"--live")>=0;
      bool background=live&&!verifyLive&&Array.IndexOf(args,"--background")>=0;
      bool created;
      using(var mutex=new Mutex(true,live&&!verifyLive?"Local\\CodexQuotaOverlay.SingleInstance":"Local\\CodexCompanion.PulseWebPreview",out created)) {
        if(!created) return 2;
        var app=new Application { ShutdownMode=ShutdownMode.OnMainWindowClose };
        var directory=AppDomain.CurrentDomain.BaseDirectory;
        string verification=args.Length==2 && args[0]=="--verify" ? Path.GetFullPath(args[1]) : null;
        var widget=new WidgetWindow(Path.Combine(directory,"renderer"),live);
        // 仅视觉检查时提供独立任务栏入口，方便系统截图工具选中无边框窗口。
        widget.ShowInTaskbar=!background&&Array.IndexOf(args,"--inspect-window")>=0;
        if(background)widget.Opacity=0; // WebView Loaded 完成前不闪出空壳窗口。
        if(live) {
          using(var runtime=new PulseLiveRuntime(widget,verifyLive,background||verifyLive)) {
            app.MainWindow=widget;
            widget.Failed+=delegate(string error) {if(verifyLive) {PulseLiveVerification.Write(args[1],new{passed=false,error=error});app.Shutdown(1);}};
            if(verifyLive)widget.Ready+=async delegate {await PulseLiveVerification.VerifyReadOnly(widget,runtime,args[1]);};
            widget.Show();return app.Run();
          }
        }
        var controls=new ControlsWindow(widget,verification);
        widget.Failed += delegate(string error) { controls.Report(error); if(verification!=null) { Directory.CreateDirectory(verification);File.WriteAllText(Path.Combine(verification,"error.txt"),DateTime.UtcNow.ToString("o")+"\n"+error);app.Shutdown(1); } };
        app.MainWindow=controls;
        controls.Show();
        widget.Show();
        return app.Run();
      }
    }
  }
}
