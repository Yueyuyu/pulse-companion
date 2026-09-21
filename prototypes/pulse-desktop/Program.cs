using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace PulseDesktopPreview
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string output=args.Length>1&&args[0]=="--verify"?Path.GetFullPath(args[1]):null;
            try
            {
                DemoState.SelfTest();
                bool created;
                using(var mutex=new Mutex(true,"Local\\CodexCompanion.PulseDesktopPreview",out created))
                {
                    if(!created)
                    {
                        if(output!=null)throw new InvalidOperationException("已有设计验证窗口运行。请先关闭预览再验收，不能使用旧验收报告。");
                        return 0;
                    }
                    var app=new Application { ShutdownMode=ShutdownMode.OnMainWindowClose };
                    var widget=new DockWindow(new DemoState(),new PreviewTheme());
                    var lab=new LabWindow(widget);app.MainWindow=lab;
                    if(output!=null)lab.Loaded+=delegate{new PreviewVerification(lab,output).Start();};
                    int exitCode=app.Run(lab);GC.KeepAlive(mutex);return exitCode;
                }
            }
            catch(Exception exception)
            {
                if(output!=null){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"error.txt"),exception.ToString());}
                else MessageBox.Show(exception.Message,"Pulse Desktop 设计验证未能启动");
                return 1;
            }
        }
    }
}
