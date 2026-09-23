using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CodexCompanion.PulseWebPreview {
  internal sealed class ControlsWindow : Window {
    readonly WidgetWindow widget;
    readonly TextBlock status=new TextBlock {Text="加载真实 Windows 窗口…",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
    readonly string verification;
    internal ControlsWindow(WidgetWindow window,string output) {
      widget=window;verification=output;
      Title="Pulse Companion · 设计验证（示例数据）";Width=380;Height=320;ResizeMode=ResizeMode.NoResize;
      var stack=new StackPanel {Margin=new Thickness(18)};
      stack.Children.Add(new TextBlock {Text="同一份 Pulse 界面 · WebView2",FontSize=17,FontWeight=FontWeights.SemiBold});
      stack.Children.Add(new TextBlock {Text="本窗口仅用示例，不影响已安装的实时版。",Margin=new Thickness(0,8,0,12)});
      var modes=new WrapPanel();foreach(var pair in new[]{"compact:紧凑","expanded:展开","docked:贴边"}) {var a=pair.Split(':');Add(modes,a[1],()=>widget.Send("mode",a[0]));}stack.Children.Add(modes);
      var scales=new WrapPanel();foreach(var value in new[]{1.0,1.25,1.5,2.0}) {var scale=value;Add(scales,(scale*100)+"%",()=>widget.Send("scale",scale));}stack.Children.Add(scales);
      var actions=new WrapPanel();Add(actions,"模拟完成",()=>widget.Send("complete",true));Add(actions,"停止动作",()=>widget.Send("motion",false));Add(actions,"开启动作",()=>widget.Send("motion",true));stack.Children.Add(actions);
      var last=new WrapPanel();Add(last,"左侧",()=>widget.Send("side","left"));Add(last,"右侧",()=>widget.Send("side","right"));Add(last,"退出预览",()=>Close());stack.Children.Add(last);
      stack.Children.Add(status);Content=stack;
      widget.Selected+=Report;
      widget.Ready+=async delegate {Report("原版资源已加载；拖动黑色轨道移动。");if(verification!=null) await Verify();};
      Closed+=delegate {widget.Close();};
    }
    void Add(Panel panel,string title,Action action) {var b=new Button {Content=title,Margin=new Thickness(0,0,6,6),Padding=new Thickness(9,5,9,5)};b.Click+=delegate {action();};panel.Children.Add(b);}
    internal void Report(string text) {status.Text=text;}
    async Task Verify() {
      try {
        Directory.CreateDirectory(verification);
        var captures=new List<object>();
        var compactBounds=new Dictionary<string,Rect>();
        await Task.Delay(1500);
        if(widget.View.UseLayoutRounding) throw new Exception("WebView 合成层的抗锯齿被布局取整禁用了。");
        await VerifyFreePlacement();
        foreach(var side in new[]{"right","left"}) foreach(var scale in new[]{1.0,1.25,1.5,2.0}) foreach(var mode in new[]{"compact","expanded","docked"}) {
          widget.Send("side",side);widget.Send("scale",scale);widget.Send("mode",mode);await Task.Delay(750);
          var raw=await widget.Inspect();var unwrapped=widget.Json.Deserialize<string>(raw);
          var state=widget.Json.Deserialize<Dictionary<string,object>>(unwrapped);
          if(Convert.ToString(state["mode"])!=mode || Convert.ToString(state["side"])!=side || Convert.ToDouble(state["scale"])!=scale || Convert.ToBoolean(state["overflow"])) throw new Exception("窗口布局未完成："+unwrapped);
          if(mode!="docked" && Convert.ToString(state["source"])!="pulse-upstream") throw new Exception("未加载原版机器人："+unwrapped);
          var key=side+"-"+scale;
          var bounds=new Rect(widget.Left,widget.Top,widget.Width,widget.Height);
          if(mode=="compact")compactBounds[key]=bounds;
          else {
            var before=compactBounds[key];
            if(before.Width!=bounds.Width||before.Height!=bounds.Height)throw new Exception("开合改变原生合成画布尺寸："+key);
            if(mode=="expanded"&&(Math.Abs(before.Left-bounds.Left)>.5||Math.Abs(before.Top-bounds.Top)>.5))throw new Exception("展开移动了侧轨所在的原生窗口："+key);
          }
          var file=side+"-"+mode+"-"+(int)(scale*100)+".png";await widget.Capture(Path.Combine(verification,file));
          var surface=widget.CaptureHost(Path.Combine(verification,"host-"+file));
          if(surface.ClippedPixels>0) throw new Exception("系统窗口区域切掉了可见像素："+file+" "+widget.Json.Serialize(surface));
          if(surface.PartialAlphaPixels==0) throw new Exception("窗口缺少抗锯齿半透明像素："+file);
          if(surface.ExcludedTransparentPixels==0) throw new Exception("透明预留画布未从桌面命中区域排除："+file);
          captures.Add(new{side=side,mode=mode,scale=scale,state=state,file=file,hostFile="host-"+file,surface=surface});
        }
        widget.Send("scale",1);widget.Send("side","right");widget.Send("mode","compact");await Task.Delay(250);
        widget.AutoDock=true;widget.FinishDrag();await Task.Delay(250);
        if(widget.Mode!="docked"||widget.Side!="right")throw new Exception("开启自动贴边后右侧未收起。");
        File.WriteAllText(Path.Combine(verification,"verification.json"),widget.Json.Serialize(new{passed=true,verifiedAtUtc=DateTime.UtcNow.ToString("o"),stableHoverCanvas=true,freePlacement=true,kind="WebView2/WPF content + actual OS region pixel audit + fixed hover canvas + release placement policy; not manual native mouse drag or OS desktop screenshot",captures=captures}));
        Application.Current.Shutdown(0);
      }catch(Exception error) {
        File.WriteAllText(Path.Combine(verification,"error.txt"),error.ToString());Report(error.Message);Application.Current.Shutdown(1);
      }
    }
    async Task VerifyFreePlacement() {
      var origin=new Point(widget.Left,widget.Top);
      var area=SystemParameters.WorkArea;
      widget.AutoDock=false;
      widget.RestorePosition(area.Left-284-20,area.Top+80);
      var position=new Point(widget.Left,widget.Top);
      widget.FinishDrag();await Task.Delay(120);
      if(widget.Mode!="compact"||new Point(widget.Left,widget.Top)!=position)throw new Exception("关闭自动贴边后不能擅自吸附或禁止部分越界。");
      widget.Send("mode","expanded");await Task.Delay(250);
      if(new Point(widget.Left,widget.Top)!=position)throw new Exception("展开面板把侧轨推回屏幕。");
      widget.Send("mode","compact");await Task.Delay(150);
      if(new Point(widget.Left,widget.Top)!=position)throw new Exception("收起面板移动侧轨。");
      widget.RestorePosition(area.Right-354,area.Top+100);
      position=new Point(widget.Left,widget.Top);widget.FinishDrag();await Task.Delay(120);
      if(widget.Mode!="compact"||new Point(widget.Left,widget.Top)!=position)throw new Exception("关闭自动贴边后右侧仍吸附。");
      widget.RestorePosition(origin.X,origin.Y);
    }
  }
}
