using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace CodexCompanion.PulseWebPreview {
  // 托盘小图独立于桌面机器人/品牌偏好；图像内嵌，不依赖部署目录中的外部路径。
  internal sealed class PulseTrayIcon : IDisposable {
    internal const string ResourceName="PulseCompanion.TrayIcon";
    readonly Forms.NotifyIcon tray;
    readonly Dispatcher dispatcher;
    readonly bool observeDisplay;
    Icon current;
    int currentSize;
    bool disposed;

    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string windowName);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index,uint dpi);

    internal PulseTrayIcon(Forms.NotifyIcon target,Dispatcher owner,bool observe=true) {
      tray=target;dispatcher=owner;observeDisplay=observe;
      Refresh();
      if(observeDisplay) {
        SystemEvents.DisplaySettingsChanged+=DisplayChanged;
        SystemEvents.UserPreferenceChanged+=PreferenceChanged;
      }
    }

    internal static Icon Load(int pixels) {
      if(pixels<16||pixels>64)throw new ArgumentOutOfRangeException("pixels");
      using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)) {
        if(stream==null)throw new InvalidOperationException("缺少 Pulse 托盘图标资源，请重新构建。");
        using(var icon=new Icon(stream,new Size(pixels,pixels)))return (Icon)icon.Clone();
      }
    }

    static int ShellIconSize() {
      // 不使用浮条窗口 DPI：浮条可在另一显示器，托盘仍由 Explorer 主任务栏承载。
      try {
        IntPtr taskbar=FindWindow("Shell_TrayWnd",null);
        uint dpi=taskbar==IntPtr.Zero?96:GetDpiForWindow(taskbar);
        int size=GetSystemMetricsForDpi(49,dpi==0?96:dpi); // SM_CXSMICON
        if(size>=16&&size<=64)return size;
      } catch(EntryPointNotFoundException) { }
      return Math.Max(16,Math.Min(64,Forms.SystemInformation.SmallIconSize.Width));
    }

    void Refresh() {
      if(disposed)return;
      int size=ShellIconSize();
      if(current!=null&&currentSize==size)return;
      Icon next=Load(size),previous=current;
      tray.Icon=next;current=next;currentSize=size;
      if(previous!=null)previous.Dispose();
    }
    void DisplayChanged(object sender,EventArgs args) {QueueRefresh();}
    void PreferenceChanged(object sender,UserPreferenceChangedEventArgs args) {QueueRefresh();}
    void QueueRefresh() {
      if(!disposed&&!dispatcher.HasShutdownStarted)dispatcher.BeginInvoke(new Action(Refresh));
    }
    public void Dispose() {
      if(disposed)return;disposed=true;
      if(observeDisplay) {
        SystemEvents.DisplaySettingsChanged-=DisplayChanged;
        SystemEvents.UserPreferenceChanged-=PreferenceChanged;
      }
      if(current!=null) {current.Dispose();current=null;}
    }
  }
}
