using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PulseDesktopPreview
{
    // 仅捕获本程序的可视树。禁止全屏截图或读取用户其它窗口。
    internal sealed class PreviewVerification
    {
        private readonly LabWindow lab;
        private readonly string output;
        private readonly Queue<Action> steps=new Queue<Action>();
        private readonly List<object> captures=new List<object>();
        private readonly List<string> interactionChecks=new List<string>();
        private readonly DispatcherTimer timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(220) };
        internal PreviewVerification(LabWindow window,string directory){lab=window;output=directory;}
        internal void Start()
        {
            Directory.CreateDirectory(output);lab.Widget.State.Pinned=true;
            steps.Enqueue(VerifyInteractions);
            QueueBotChecks();
            foreach(string background in new[]{"mist","night","terrain"})
            foreach(double scale in new[]{1.0,1.25,1.5,2.0})
            foreach(string mode in new[]{"compact","expanded","docked"})
            {
                string bg=background,m=mode;double s=scale;
                steps.Enqueue(delegate{lab.SetWallpaper(bg);lab.Widget.State.Scale=s;lab.Widget.ChangeMode(m,false);lab.PlaceWidget();});
                steps.Enqueue(delegate{Capture(bg,m,s);});
            }
            steps.Enqueue(delegate{
                lab.Widget.State.Mode="expanded";lab.Widget.State.DataState="error";lab.Widget.State.Scale=1;lab.Widget.Render();
                if(lab.Widget.State.Value!="—")throw new Exception("错误状态显示了有效额度");
            });
            steps.Enqueue(delegate{Capture("terrain","error",1);});
            timer.Tick+=delegate{
                try
                {
                    if(steps.Count>0){steps.Dequeue()();return;}
                    timer.Stop();
                    var source=PresentationSource.FromVisual(lab.Widget);
                    var report=new { passed=true,verifiedAtUtc=DateTime.UtcNow.ToString("o"),coreAssertions=5,interactionChecks=interactionChecks,nativeRenderCaptures=captures.Count,actualDpiScale=source==null?1:source.CompositionTarget.TransformToDevice.M11,
                        note="已在真实 WPF 窗口加载并捕获可视树。100/125/150/200% 是应用内渲染缩放，不是切换操作系统 DPI；跨屏混合 DPI 和真实鼠标拖动需人工验收。无账号读取、通知发送、安装或旧程序替换。",captures=captures };
                    File.WriteAllText(Path.Combine(output,"verification.json"),new JavaScriptSerializer().Serialize(report));
                    Application.Current.Shutdown();
                }
                catch(Exception error){timer.Stop();File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());Application.Current.Shutdown(1);}
            };timer.Start();
        }
        private void Capture(string background,string mode,double scale)
        {
            var widget=lab.Widget;widget.UpdateLayout();
            if(double.IsNaN(widget.Width)||widget.Width<20||widget.Height<20)throw new Exception("窗口无有效尺寸");
            if((mode=="expanded"||mode=="error") && widget.Height<400*scale) throw new Exception("展开面板被原窗口高度裁切");
            if(mode=="compact" && Math.Abs(widget.Height-(68*scale+40))>2) throw new Exception("紧凑窗口缩放尺寸漂移");
            if(mode=="docked" && Math.Abs(widget.Height-(98*scale+40))>3) throw new Exception("贴边窗口缩放尺寸漂移");
            var content=(FrameworkElement)widget.Content;
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(widget.Width),(int)Math.Ceiling(widget.Height),96,96,PixelFormats.Pbgra32);bitmap.Render(content);
            var file=background+"-"+mode+"-"+(scale*100)+".png";
            Save(bitmap,Path.Combine(output,file));
            // 单独合成同一原生窗口的背景和透明浮层，便于比较不同壁纸；不修改系统壁纸。
            var drawing=new DrawingVisual();
            using(var dc=drawing.RenderOpen())
            {
                dc.DrawRectangle(((System.Windows.Controls.Panel)lab.Scene.Child).Background,null,new Rect(0,0,widget.Width+80,widget.Height+80));
                dc.DrawImage(bitmap,new Rect(40,40,widget.Width,widget.Height));
            }
            var composite=new RenderTargetBitmap((int)Math.Ceiling(widget.Width+80),(int)Math.Ceiling(widget.Height+80),96,96,PixelFormats.Pbgra32);composite.Render(drawing);Save(composite,Path.Combine(output,"scene-"+file));
            captures.Add(new{file=file,background=background,mode=mode,renderScale=scale,width=widget.Width,height=widget.Height});
        }
        private BotMarkView FindBot(DependencyObject parent)
        {
            var bot=parent as BotMarkView;if(bot!=null)return bot;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var found=FindBot(VisualTreeHelper.GetChild(parent,i));if(found!=null)return found;}
            return null;
        }
        private void QueueBotChecks()
        {
            BotMarkView previousBot=null;
            steps.Enqueue(delegate {
                previousBot=FindBot(lab.Widget);
                if(previousBot==null||previousBot.MotionActive!=SystemParameters.ClientAreaAnimation)throw new Exception("角色动画未遵循系统设置");
                var toggle=FindButton(lab,"切换灵动表情");if(toggle==null)throw new Exception("缺少静态开关");toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            });
            steps.Enqueue(delegate {
                if(previousBot.MotionActive||FindBot(lab.Widget).MotionActive||lab.Widget.State.MotionEnabled)throw new Exception("关闭动画后残留时钟");
                interactionChecks.Add("角色动态开启、静态开关与卸载时钟清理");
                lab.Widget.State.MotionEnabled=true;lab.Widget.Render();
            });
            foreach(string mood in new[]{"working","idle","happy","attention","offline","loading"})
            {
                string captured=mood;
                steps.Enqueue(delegate {
                    var state=lab.Widget.State;state.ResetTasks();state.SmileUntil=DateTime.MinValue;state.Mode="compact";state.Scale=2;
                    state.DataState=captured=="offline"?"error":captured=="loading"?"loading":"ready";
                    if(captured=="idle")state.Tasks.Clear();
                    if(captured=="happy")foreach(var task in state.Tasks)task.State="completed";
                    if(captured=="attention")state.Tasks[0].State="attention";
                    lab.Widget.Render();
                    if(state.Mood!=captured)throw new Exception("角色语义错误："+captured);
                    lab.SetWallpaper("mist");
                });
                steps.Enqueue(delegate {Capture("mist","avatar-"+captured,2);});
            }
            steps.Enqueue(delegate {
                lab.Widget.State.ResetTasks();lab.Widget.State.SmileUntil=DateTime.MinValue;lab.Widget.State.DataState="ready";lab.Widget.State.Scale=1;lab.Widget.Render();
                interactionChecks.Add("六种表情原生渲染与任务语义");
                lab.Widget.UpdateLayout();previousBot=FindBot(lab.Widget);
                if(previousBot==null)throw new Exception("角色尚未挂载，不能测试贴边清理");
                lab.Widget.ChangeMode("docked",false);
            });
            steps.Enqueue(delegate {
                if(previousBot.MotionActive)throw new Exception("贴边后旧角色仍占用动画时钟");
                interactionChecks.Add("贴边态释放角色动画");
            });
        }
        private static void Save(BitmapSource bitmap,string path)
        {
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);
        }
        private System.Windows.Controls.Button FindButton(DependencyObject parent,string name)
        {
            var button=parent as System.Windows.Controls.Button;
            if(button!=null&&System.Windows.Automation.AutomationProperties.GetName(button)==name)return button;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) {var found=FindButton(VisualTreeHelper.GetChild(parent,i),name);if(found!=null)return found;}
            return null;
        }
        private void Invoke(string name)
        {
            lab.Widget.UpdateLayout();var button=FindButton(lab.Widget,name);
            if(button==null||!button.IsEnabled)throw new Exception("找不到可用控件："+name);
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        }
        private void VerifyInteractions()
        {
            var widget=lab.Widget;
            widget.State.Pinned=false;widget.ChangeMode("compact",false);
            Invoke("周剩余额度 69%，2 项执行中；点击切换详情");
            if(widget.State.Mode!="expanded")throw new Exception("原生展开事件失效");interactionChecks.Add("原生按钮展开详情");
            Invoke("固定面板");if(!widget.State.Pinned)throw new Exception("固定事件失效");interactionChecks.Add("固定按钮状态");
            Invoke("取消关注：完善桌面伴侣的视觉细节");if(widget.State.Tasks[0].Watched)throw new Exception("取消关注失效");
            Invoke("关注：完善桌面伴侣的视觉细节");if(!widget.State.Tasks[0].Watched)throw new Exception("恢复关注失效");interactionChecks.Add("关注和取消关注");
            widget.State.DataState="error";widget.Render();Invoke("重新读取示例数据");if(widget.State.DataState!="ready")throw new Exception("重试失效");interactionChecks.Add("错误恢复按钮");
            Invoke("贴边收起");if(widget.State.Mode!="docked")throw new Exception("贴边按钮失效");interactionChecks.Add("贴边按钮切换");
            Invoke("展开桌面伴侣；也可拖动");if(widget.State.Mode!="expanded")throw new Exception("细条点击失效");interactionChecks.Add("细条点击展开");
        }
    }
}
