using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PulseDesktopPreview
{
    internal sealed class LabWindow : Window
    {
        internal readonly DockWindow Widget;
        internal readonly Border Scene;
        private readonly TextBlock message;
        internal string Wallpaper = "mist";
        internal LabWindow(DockWindow widget)
        {
            Widget=widget; Title="Pulse Desktop · 设计验证台（不替换旧伴侣）";
            Width=1060; Height=820; MinWidth=820; MinHeight=650; WindowStartupLocation=WindowStartupLocation.CenterScreen;
            Background=widget.Theme.Color("canvas"); FontFamily=new FontFamily("Segoe UI Variable, Segoe UI, Microsoft YaHei UI");
            var theme=widget.Theme;
            var root=new DockPanel { Margin=new Thickness(22) };
            var header=new StackPanel();
            var title=theme.Text("Pulse Desktop / 原生桌面验证",24,"ink"); title.Margin=new Thickness(0,0,0,8); header.Children.Add(title);
            header.Children.Add(theme.Text("真实 WPF 透明窗口 · 固定示例数据 · 与旧伴侣并行，不读取账户",13,"muted"));
            var controls=new WrapPanel { Margin=new Thickness(0,14,0,14) };
            AddControl(controls,"紧凑",delegate{widget.ChangeMode("compact",false);PlaceWidget();});
            AddControl(controls,"展开",delegate{widget.ChangeMode("expanded",false);PlaceWidget();});
            AddControl(controls,"贴边",delegate{widget.ChangeMode("docked",false);});
            foreach(string background in new[]{"mist","night","terrain"}) { string selected=background; AddControl(controls,background=="mist"?"浅色背景":background=="night"?"深色背景":"复杂背景",delegate { SetWallpaper(selected); }); }
            foreach(double scale in new[]{1.0,1.25,1.5,2.0}) { double selected=scale; AddControl(controls,(scale*100)+"%",delegate{widget.State.Scale=selected;widget.Render();PlaceWidget();}); }
            AddControl(controls,"只看桌面",delegate{WindowState=WindowState.Minimized;});
            header.Children.Add(controls); DockPanel.SetDock(header,Dock.Top); root.Children.Add(header);
            var personality=new WrapPanel { Margin=new Thickness(0,0,0,10) };
            foreach(string mood in new[]{"working","idle","happy","attention","offline"})
            {
                string selected=mood;
                string label=mood=="working"?"工作":mood=="idle"?"待命":mood=="happy"?"完成":mood=="attention"?"需处理":"离线";
                var content=new StackPanel { Orientation=Orientation.Horizontal };
                content.Children.Add(new BotMarkView(theme,mood,false,false));content.Children.Add(theme.Text(label,12,"ink"));
                var preview=theme.Button(content,"预览"+label+"表情",delegate { SetMood(selected); });preview.Margin=new Thickness(0,0,6,6);personality.Children.Add(preview);
            }
            var motion=theme.Button(theme.Text("灵动表情：开",12,"ink"),"切换灵动表情",null);
            motion.Click+=delegate { widget.State.MotionEnabled=!widget.State.MotionEnabled;((TextBlock)motion.Content).Text=widget.State.MotionEnabled?"灵动表情：开":"灵动表情：关";widget.Render(); };
            personality.Children.Add(motion);header.Children.Add(personality);
            var footer=new StackPanel { Margin=new Thickness(0,14,0,0) }; var actions=new WrapPanel();
            AddControl(actions,"模拟完成 / 继续",delegate{if(widget.State.Tasks.Count==0)widget.State.ResetTasks();var task=widget.State.Tasks[0];task.State=task.State=="completed"?"running":"completed";widget.Render();message.Text="示例状态已改变；完成项仍保留关注，没有发送系统通知。";});
            AddControl(actions,"正常",delegate{widget.State.DataState="ready";widget.Render();});
            AddControl(actions,"读取中",delegate{widget.State.DataState="loading";widget.Render();});
            AddControl(actions,"读取失败",delegate{widget.State.DataState="error";widget.Render();});
            AddControl(actions,"低额度",delegate{widget.State.DataState="ready";widget.State.Remaining=12;widget.Render();});
            AddControl(actions,"重置",delegate{widget.State.ResetTasks();widget.State.Remaining=69;widget.State.DataState="ready";widget.State.Scale=1;widget.State.Pinned=false;widget.ChangeMode("compact",false);PlaceWidget();});
            footer.Children.Add(actions);
            message=theme.Text("拖动浮条左端；点击展开；星标关注；右键菜单可退出。F1 / F2 / F3 切换状态，Escape 收起。",12,"muted");message.TextWrapping=TextWrapping.Wrap;message.Margin=new Thickness(0,10,0,0);footer.Children.Add(message);
            footer.Children.Add(theme.Text("缩放按钮是布局/渲染验证，不修改 Windows 系统缩放或壁纸。",12,"muted"));DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
            Scene=new Border { CornerRadius=new CornerRadius(18),ClipToBounds=true,BorderBrush=theme.Color("line"),BorderThickness=new Thickness(1) };
            Scene.MouseLeftButtonDown+=delegate { if(!widget.State.Pinned&&widget.State.Mode=="expanded")widget.ChangeMode("compact",false); };
            root.Children.Add(Scene);Content=root;
            SetWallpaper("mist");
            widget.Notify=delegate(string text){message.Text=text;};widget.IsLabActive=delegate{return IsActive;};
            Loaded+=delegate{widget.Show();PlaceWidget();};
            Closed+=delegate{widget.Close();};
        }
        private void AddControl(Panel panel,string text,Action action)
        {
            var button=Widget.Theme.Button(Widget.Theme.Text(text,12,"ink"),text,action);button.Background=Widget.Theme.Color("surface");button.Padding=new Thickness(10,6,10,6);button.Margin=new Thickness(0,0,6,6);panel.Children.Add(button);
        }
        private void SetMood(string mood)
        {
            Widget.State.ResetTasks();Widget.State.DataState=mood=="offline"?"error":"ready";Widget.State.SmileUntil=DateTime.MinValue;
            if(mood=="idle")Widget.State.Tasks.Clear();
            if(mood=="happy")foreach(var task in Widget.State.Tasks)task.State="completed";
            if(mood=="attention")Widget.State.Tasks[0].State="attention";
            Widget.Render();message.Text="正在预览示例表情。小搭子表示任务状态，圆环只表示额度。";
        }
        internal void PlaceWidget()
        {
            if (!Widget.IsVisible) return;
            var point=Scene.PointToScreen(new Point(Math.Max(20,Scene.ActualWidth-430),28));
            var source=PresentationSource.FromVisual(Widget);
            if(source!=null)point=source.CompositionTarget.TransformFromDevice.Transform(point);
            Widget.Left=point.X;Widget.Top=point.Y;Widget.ClampToScreen();
        }
        internal void SetWallpaper(string name)
        {
            Wallpaper=name;
            var canvas=new Grid();
            canvas.Background=new LinearGradientBrush(name=="night"?System.Windows.Media.Color.FromRgb(43,65,69):System.Windows.Media.Color.FromRgb(233,239,233),name=="night"?System.Windows.Media.Color.FromRgb(15,26,33):System.Windows.Media.Color.FromRgb(248,244,235),45);
            if(name=="terrain")
            {
                var drawing=new DrawingGroup();
                drawing.ClipGeometry=new RectangleGeometry(new Rect(0,0,1000,800));
                drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(149,172,141)),null,Geometry.Parse("M0,0 L1000,0 1000,800 0,800 Z")));
                drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(210,215,177)),null,Geometry.Parse("M0,560 L700,0 1000,0 1000,240 200,800 0,800 Z")));
                drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(91,125,110)),null,Geometry.Parse("M0,730 L880,170 1000,260 1000,800 0,800 Z")));
                drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(40,74,69)),null,Geometry.Parse("M0,800 L1000,350 1000,800 Z")));
                for(int y=0;y<800;y+=28)drawing.Children.Add(new GeometryDrawing(null,new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(28,255,255,255)),1),Geometry.Parse("M0,"+y+" L1000,"+(y-480))));
                canvas.Background=new DrawingBrush(drawing){Stretch=Stretch.Fill};
            }
            var captions=new StackPanel { Margin=new Thickness(28),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom };
            var color=name=="mist"?new SolidColorBrush(System.Windows.Media.Color.FromRgb(54,76,64)):Brushes.WhiteSmoke;
            captions.Children.Add(new TextBlock{Text="DESKTOP STUDY — 01",FontSize=12,Foreground=color});
            captions.Children.Add(new TextBlock{Text="少一点打扰。\n重要的状态，一直在。",FontSize=28,Foreground=color,Margin=new Thickness(0,12,0,16)});
            captions.Children.Add(new TextBlock{Text="设计验证 · 示例数据 · 背景不属于核心 Token",FontSize=12,Foreground=color});
            canvas.Children.Add(captions);Scene.Child=canvas;
        }
    }
}
