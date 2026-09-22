using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CodexCompanion.PulseWebPreview {
  internal sealed class WidgetWindow : Window {
    internal readonly WebView2CompositionControl View=new WebView2CompositionControl();
    internal readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    internal event Action Ready;
    internal event Action<string> Failed;
    internal event Action<string> Selected;
    internal event Action<Dictionary<string,object>> Command;
    internal event Action PlacementChanged;
    internal readonly bool Live;
    internal string Mode="compact", Side="right";
    internal double RenderScale=1;
    double railHeight=102,panelOffset;
    readonly string renderer;
    bool closed, attached, initializationStarted, regionApplied;
    string panelPath;
    PointerBoundaryMonitor pointerMonitor;
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
    [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr result,IntPtr first,IntPtr second,int mode);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    internal WidgetWindow(string root,bool live=false) {
      Live=live;
      renderer=root;
      Title=live?"Pulse Companion · 实时状态":"Pulse Companion · 本机示例";
      WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
      AllowsTransparency=true; Background=Brushes.Transparent;
      ShowInTaskbar=false; Topmost=true; ShowActivated=false;
      Width=354;Height=572;Left=SystemParameters.WorkArea.Right-438;Top=SystemParameters.WorkArea.Top+100;
      Content=View;View.DefaultBackgroundColor=System.Drawing.Color.Transparent;
      // 微软 CompositionControl 默认开启布局取整，会禁用合成图像的抗锯齿。
      // 矢量轮廓和小数 DPI 交给 WebView/WPF 按 alpha 合成，不再二次硬化边缘。
      UseLayoutRounding=false;View.UseLayoutRounding=false;
      Loaded+=async delegate { await Initialize(); };
      Closed+=delegate {closed=true;if(pointerMonitor!=null)pointerMonitor.Dispose();View.Dispose();};
      Deactivated+=delegate {if(Live)Send("window-deactivated",true);};
      DpiChanged+=delegate { ApplyRegion(); };
    }
    async Task Initialize() {
      if(initializationStarted)return;
      initializationStarted=true; // 后台 Hide/Show 不重复注册 WebView 事件或重置页面。
      try {
        if(!File.Exists(Path.Combine(renderer,"LOCAL-ONLY.json"))) throw new InvalidOperationException("缺少仅限本机的资源标记。");
        var profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexCompanionPulsePreview","WebView2");
        var environment=await CoreWebView2Environment.CreateAsync(null,profile,null);
        if(closed) return;
        await View.EnsureCoreWebView2Async(environment);
        var core=View.CoreWebView2;
        core.Settings.AreDevToolsEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;
        core.Settings.IsStatusBarEnabled=false;core.Settings.IsZoomControlEnabled=false;
        core.Settings.AreBrowserAcceleratorKeysEnabled=false;
        core.SetVirtualHostNameToFolderMapping("pulse-preview.local",renderer,CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e) {
          Uri uri; if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out uri) || uri.Scheme!="https" || uri.Host!="pulse-preview.local") e.Cancel=true;
        };
        core.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e) {e.Handled=true;};
        core.PermissionRequested+=delegate(object sender,CoreWebView2PermissionRequestedEventArgs e) {e.State=CoreWebView2PermissionState.Deny;};
        core.DownloadStarting+=delegate(object sender,CoreWebView2DownloadStartingEventArgs e) {e.Cancel=true;};
        await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__PULSE_HOST_MODE__="+(Live?"'live'":"'demo'")+";");
        core.WebMessageReceived+=OnMessage;
        core.NavigationCompleted+=delegate(object sender,CoreWebView2NavigationCompletedEventArgs e) {if(!e.IsSuccess && Failed!=null) Failed("Pulse Companion 界面加载失败："+e.WebErrorStatus);};
        core.Navigate("https://pulse-preview.local/index.html");
      } catch(Exception error) {if(Failed!=null) Failed(error.GetType().Name+": "+error.Message);}
    }
    void OnMessage(object sender,CoreWebView2WebMessageReceivedEventArgs e) {
      if(e.Source!="https://pulse-preview.local/index.html") return;
      try {
        var message=Json.Deserialize<Dictionary<string,object>>(e.WebMessageAsJson);
        var type=Convert.ToString(message["type"]);
        if(type=="ready") {if(Live&&pointerMonitor==null)pointerMonitor=new PointerBoundaryMonitor(this);if(Ready!=null) Ready();return;}
        if(type=="size") {
          var width=Convert.ToDouble(message["width"]);var height=Convert.ToDouble(message["height"]);
          var mode=Convert.ToString(message["mode"]);var side=Convert.ToString(message["side"]);var scale=Convert.ToDouble(message["scale"]);
          var nextRail=Convert.ToDouble(message["railHeight"]);var nextOffset=Convert.ToDouble(message["panelOffset"]);
          if(nextRail<102||nextRail>530||Double.IsNaN(nextRail)||nextOffset<0||nextOffset>380||Double.IsNaN(nextOffset))return;
          if(!Double.IsNaN(width) && width>=20 && width<=750 && height>=20 && height<=2000 &&
            (mode=="compact"||mode=="expanded"||mode=="docked")&&(side=="left"||side=="right")&&(scale==1||scale==1.25||scale==1.5||scale==2)) {
            bool changed=Width!=width||Height!=height||Mode!=mode||Side!=side||RenderScale!=scale;
            bool regionChanged=!regionApplied||changed||railHeight!=nextRail||panelOffset!=nextOffset||panelPath!=Convert.ToString(message["panelPath"])||attached!=Convert.ToBoolean(message["attached"]);
            var right=Left+Width;
            railHeight=nextRail;panelOffset=nextOffset;
            Mode=Convert.ToString(message["mode"]);Side=Convert.ToString(message["side"]);RenderScale=Convert.ToDouble(message["scale"]);attached=Convert.ToBoolean(message["attached"]);panelPath=Convert.ToString(message["panelPath"]);
            Width=width;Height=height;if(Side=="right")Left=right-Width;
            if(Mode=="docked" || attached) Dock(); else Fit();
            if(regionChanged)ApplyRegion();
            if(changed && PlacementChanged!=null)PlacementChanged();
          }
        } else if(type=="drag") {
          // 只移动这个验证窗口，不向 Codex 或其他窗口发送输入。
          ReleaseCapture();
          SendMessage(new WindowInteropHelper(this).Handle,0xA1,new IntPtr(2),IntPtr.Zero);
          var area=CurrentArea();
          var rail=RailBounds();
          if(Math.Abs(Left+(rail.Right+6)*RenderScale-area.Right)<28) {Send("side","right");Send("mode","docked");}
          else if(Math.Abs(Left+(rail.Left-6)*RenderScale-area.Left)<28) {Send("side","left");Send("mode","docked");}
          else if(Mode=="docked") Send("mode","compact");
          if(PlacementChanged!=null)PlacementChanged();
        } else if(!Live && type=="demo-open" && Selected!=null) Selected("已选择示例任务；未跳转真实 Codex 对话。");
        else if(Live && (type=="refresh"||type=="watch"||type=="open-task"||type=="pin"||type=="icon-mode"||type=="authorize"||type=="cancel-authorization"||type=="disconnect-account"||type=="resume-auto") && Command!=null)Command(message);
      } catch(Exception error) {if(Failed!=null) Failed("预览桥接失败："+error.Message);}
    }
    Rect CurrentArea() {
      // 透明预留画布可以跨屏；由用户真正看见的侧轨决定所在显示器。
      var rail=RailBounds();
      var anchor=PointToScreen(new Point((rail.Left+rail.Width/2)*RenderScale,(rail.Top+20)*RenderScale));
      var screen=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)Math.Round(anchor.X),(int)Math.Round(anchor.Y))).WorkingArea;
      var transform=PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
      return new Rect(transform.Transform(new Point(screen.Left,screen.Top)),transform.Transform(new Point(screen.Right,screen.Bottom)));
    }
    Rect RailBounds() {return Mode=="docked"?new Rect(Side=="right"?328:6,6,20,96):new Rect(Side=="right"?284:6,6,64,railHeight);}
    Rect PanelBounds() {
      if(String.IsNullOrEmpty(panelPath)||panelPath.Length>32000)throw new InvalidOperationException("无效的本地窗口轮廓。");
      var bounds=Geometry.Parse(panelPath).Bounds;bounds.Offset(Side=="right"?6:78,6+panelOffset);return bounds;
    }
    Rect VisibleBounds() {var bounds=RailBounds();if(Mode=="expanded"&&!String.IsNullOrEmpty(panelPath))bounds.Union(PanelBounds());bounds.Inflate(6,6);return bounds;}
    void Fit() {var a=CurrentArea();var b=VisibleBounds();Left=Math.Max(a.Left-b.Left*RenderScale,Math.Min(Left,a.Right-b.Right*RenderScale));FitVertical(a,b);}
    void FitVertical(Rect area,Rect visible) {Top=Math.Max(area.Top-visible.Top*RenderScale,Math.Min(Top,area.Bottom-visible.Bottom*RenderScale));}
    internal void RestorePosition(double left,double top) {Left=left;Top=top;Fit();}
    void Dock() {var a=CurrentArea();Left=Side=="right"?a.Right-Width+6*RenderScale:a.Left-6*RenderScale;FitVertical(a,VisibleBounds());}
    void ApplyRegion() {
      if(!IsLoaded) return;
      var transform=PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
      // Region 只排除大块无内容空白，不能沿可见曲线裁切：GDI 区域没有半透明，
      // 会剪掉 WebView 已抗锯齿的像素。最终造型由逐像素 alpha 决定。
      var region=IntPtr.Zero;
      try {
        region=SurfaceBounds(RailBounds(),transform);
        if(region==IntPtr.Zero) throw new InvalidOperationException("无法创建窗口区域。");
        if(Mode=="expanded") {
          var panel=SurfaceBounds(PanelBounds(),transform);
          if(panel==IntPtr.Zero) throw new InvalidOperationException("无法创建面板区域。");
          try {if(CombineRgn(region,region,panel,2)==0) throw new InvalidOperationException("无法合并窗口区域。");}
          finally {DeleteObject(panel);}
        }
        if(SetWindowRgn(new WindowInteropHelper(this).Handle,region,true)==0) throw new InvalidOperationException("无法应用窗口区域。");
        region=IntPtr.Zero; // SetWindowRgn 成功后由系统持有，不能再释放。
        regionApplied=true;
      } finally {if(region!=IntPtr.Zero)DeleteObject(region);}
    }
    IntPtr SurfaceBounds(Rect bounds,Matrix deviceTransform) {
      // 与 renderer 的 6 CSS px 安全边距一致，保留软边缘、焦点环与短暂入场位移。
      bounds.Inflate(6,6);
      double x=RenderScale*deviceTransform.M11,y=RenderScale*deviceTransform.M22;
      return CreateRectRgn((int)Math.Floor(bounds.Left*x),(int)Math.Floor(bounds.Top*y),
        (int)Math.Ceiling(bounds.Right*x),(int)Math.Ceiling(bounds.Bottom*y));
    }
    internal void Send(string type,object value) {if(View.CoreWebView2!=null) View.CoreWebView2.PostWebMessageAsJson(Json.Serialize(new{type=type,value=value}));}
    internal async Task<string> Inspect() {
      return await View.CoreWebView2.ExecuteScriptAsync("JSON.stringify({mode:document.querySelector('.pd-dock').dataset.mode,side:document.querySelector('.pd-dock').dataset.side,attached:document.querySelector('.pd-dock').dataset.attached==='true',scale:Number(document.querySelector('.pd-native').style.zoom),devicePixelRatio,bot:document.querySelector('.pd-bot-host')?.dataset.runtime,source:document.querySelector('.pd-bot')?.dataset.source,rail:document.querySelector('.pd-rail')?.getBoundingClientRect().width,viewport:{width:innerWidth,height:innerHeight},overflow:document.documentElement.scrollWidth>innerWidth})");
    }
    internal WindowSurfaceAudit CaptureHost(string file) {
      UpdateLayout();
      var dpi=VisualTreeHelper.GetDpi(this);
      var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
      bitmap.Render(this);
      var bytes=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];
      bitmap.CopyPixels(bytes,bitmap.PixelWidth*4,0);
      bool visible=false,transparent=false;
      for(int i=3;i<bytes.Length;i+=4){if(bytes[i]>0)visible=true;if(bytes[i]==0)transparent=true;}
      if(!visible || !transparent) throw new InvalidOperationException("WPF 合成层没有同时呈现内容和透明像素。");
      var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
      using(var stream=File.Create(file))encoder.Save(stream);
      return WindowSurfaceAudit.Inspect(bitmap,new WindowInteropHelper(this).Handle);
    }
    internal async Task Capture(string file) {using(var stream=File.Create(file)) await View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);}
  }
}
