using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CodexCompanion.PulseWebPreview {
  // CompositionControl 在透明区域/跨 HWND 时可能漏发 pointerleave。
  // 仅只读查询本窗口区域；不安装鼠标钩子、不拦截其它应用输入。
  internal sealed class PointerBoundaryMonitor : IDisposable {
    readonly WidgetWindow widget;
    readonly DispatcherTimer timer;
    [StructLayout(LayoutKind.Sequential)] struct Point {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr hwnd,IntPtr region);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr region,int x,int y);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr region);
    internal PointerBoundaryMonitor(WidgetWindow window) {
      widget=window;
      timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
      timer.Tick+=Tick;timer.Start();
    }
    void Tick(object sender,EventArgs args) {
      if(widget.Mode!="expanded"||!widget.IsLoaded||!widget.IsVisible)return;
      Point cursor;Rect rect;var hwnd=new WindowInteropHelper(widget).Handle;
      if(!GetCursorPos(out cursor)||!GetWindowRect(hwnd,out rect))return;
      bool inside=cursor.X>=rect.Left&&cursor.X<rect.Right&&cursor.Y>=rect.Top&&cursor.Y<rect.Bottom;
      if(inside) {
        var region=CreateRectRgn(0,0,0,0);
        if(region!=IntPtr.Zero)try {if(GetWindowRgn(hwnd,region)>0)inside=PtInRegion(region,cursor.X-rect.Left,cursor.Y-rect.Top);}finally {DeleteObject(region);}
      }
      // 周期心跳也覆盖重启恢复、取消固定以及 WebView 丢失最后一次 leave 的情况。
      widget.Send("pointer-inside",inside);
    }
    public void Dispose() {timer.Stop();timer.Tick-=Tick;}
  }
}
