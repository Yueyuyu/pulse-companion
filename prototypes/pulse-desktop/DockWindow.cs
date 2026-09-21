using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace PulseDesktopPreview
{
    internal sealed class DockWindow : Window
    {
        internal readonly DemoState State;
        internal readonly PreviewTheme Theme;
        internal Action<string> Notify;
        internal Func<bool> IsLabActive;
        private bool dragged;
        private Point dragStart;
        private string previousMode = "compact";
        private Dictionary<string,string> previousTasks;
        private string previousDataState;
        private bool celebrateThisRender;
        private readonly System.Windows.Threading.DispatcherTimer smileTimer = new System.Windows.Threading.DispatcherTimer();
        internal Rect WorkArea()
        {
            var source = PresentationSource.FromVisual(this);
            var transform = source == null ? Matrix.Identity : source.CompositionTarget.TransformFromDevice;
            var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
            var origin = transform.Transform(new Point(screen.Left, screen.Top));
            var size = transform.Transform(new Point(screen.Width, screen.Height));
            return new Rect(origin.X,origin.Y,size.X,size.Y);
        }
        internal DockWindow(DemoState state, PreviewTheme theme)
        {
            State = state; Theme = theme; Title = "Pulse Desktop · 示例浮层";
            smileTimer.Interval=TimeSpan.FromMilliseconds(1450);
            smileTimer.Tick+=delegate { smileTimer.Stop(); State.SmileUntil=DateTime.MinValue; if(IsVisible)Render(); };
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Loaded += delegate { Render(); };
            Deactivated += delegate { Dispatcher.BeginInvoke(new Action(delegate { if (IsVisible && !State.Pinned && State.Mode == "expanded" && (IsLabActive == null || !IsLabActive())) ChangeMode("compact", false); })); };
            PreviewKeyDown += OnKey;
            SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
            Closed += delegate { SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged; smileTimer.Stop(); };
            var menu = new ContextMenu();
            foreach (string mode in new [] { "compact", "expanded", "docked" })
            {
                string captured = mode; var item = new MenuItem { Header = mode == "compact" ? "紧凑" : mode == "expanded" ? "展开" : "贴边" };
                item.Click += delegate { ChangeMode(captured, false); }; menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var exit = new MenuItem { Header = "退出设计验证（不退出旧伴侣）" }; exit.Click += delegate { Application.Current.Shutdown(); }; menu.Items.Add(exit); ContextMenu = menu;
        }
        private void OnDisplaysChanged(object sender, EventArgs args) { Dispatcher.BeginInvoke(new Action(ClampToScreen)); }
        internal void ClampToScreen()
        {
            var area = WorkArea();
            Left = DemoState.Clamp(Left, area.Left - 12, area.Right - Width + 12);
            Top = DemoState.Clamp(Top, area.Top, area.Bottom - Height);
        }
        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { ChangeMode("compact", true); e.Handled = true; }
            if (e.Key == Key.F1 || e.Key == Key.F2 || e.Key == Key.F3) { ChangeMode(e.Key == Key.F1 ? "compact" : e.Key == Key.F2 ? "expanded" : "docked", true); e.Handled = true; }
        }
        internal void ChangeMode(string mode, bool keyboard)
        {
            State.Mode = mode; Render(!keyboard);
            if (mode == "docked") { var area = WorkArea(); Left = State.Side == "right" ? area.Right - Width + 20 : area.Left - 20; }
            if (keyboard) MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
        private void AttachDrag(Button button, bool edge)
        {
            button.PreviewMouseLeftButtonDown += delegate { dragged = false; dragStart = PointToScreen(Mouse.GetPosition(this)); };
            button.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (e.LeftButton != MouseButtonState.Pressed || dragged) return;
                Point now = PointToScreen(Mouse.GetPosition(this));
                if ((now - dragStart).Length < 5) return;
                dragged = true;
                button.ReleaseMouseCapture();
                DragMove();
                var area = WorkArea();
                if (Left <= area.Left + 28 || Left + Width >= area.Right - 28)
                {
                    State.Side = Left <= area.Left + 28 ? "left" : "right"; ChangeMode("docked", false);
                }
                else if (State.Mode == "docked") ChangeMode("compact", false);
                ClampToScreen();
                e.Handled = true;
            };
            if (edge) button.Click += delegate { if (!dragged) ChangeMode("expanded", false); };
            button.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Left) Left -= 16;
                else if (e.Key == Key.Right) Left += 16;
                else if (e.Key == Key.Up) Top -= 16;
                else if (e.Key == Key.Down) Top += 16;
                else return;
                ClampToScreen(); e.Handled = true;
            };
        }
        internal void Render(bool animate = false)
        {
            // 只在两个有效示例快照间出现执行→完成时庆祝，重绘、关注操作和重连不补播。
            celebrateThisRender=false;
            if(State.DataState=="ready"&&previousDataState=="ready"&&previousTasks!=null)
                foreach(var task in State.Tasks) { string before; if(task.State=="completed"&&previousTasks.TryGetValue(task.Id,out before)&&before=="running")celebrateThisRender=true; }
            if(celebrateThisRender) { State.SmileUntil=DateTime.UtcNow.AddMilliseconds(1400);smileTimer.Stop();smileTimer.Start(); }
            if(State.DataState!="ready") { State.SmileUntil=DateTime.MinValue;smileTimer.Stop(); }
            if(State.Mood=="attention")celebrateThisRender=false;
            previousTasks=new Dictionary<string,string>();foreach(var task in State.Tasks)previousTasks[task.Id]=task.State;
            previousDataState=State.DataState;
            double oldRight = Left + (double.IsNaN(Width) ? 0 : Width);
            var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            if (State.Mode == "docked")
            {
                var indicator = new Border { Width = 6, Height = 48, CornerRadius = new CornerRadius(3), Background = Theme.Color(State.Tone) };
                var edge = Theme.Button(indicator, "展开桌面伴侣；也可拖动", null);
                edge.Width = 28; edge.Height = 96; edge.Background = Theme.Color("surface"); AttachDrag(edge,true);
                body.Children.Add(Theme.Surface(edge,14));
            }
            else
            {
                body.Children.Add(CreateRail());
                if (State.Mode == "expanded")
                {
                    var panel = CreatePanel(); panel.Margin = new Thickness(0,10,0,0); body.Children.Add(panel);
                    if (animate && previousMode != "expanded" && SystemParameters.ClientAreaAnimation)
                    {
                        panel.RenderTransformOrigin = new Point(1,0); panel.RenderTransform = new TranslateTransform();
                        panel.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                        panel.RenderTransform.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(-5,0,TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                    }
                }
            }
            previousMode = State.Mode;
            body.LayoutTransform = new ScaleTransform(State.Scale,State.Scale);
            var margin = new Border { Padding = new Thickness(20), Child = body, Background = Brushes.Transparent };
            double width = State.Mode == "docked" ? 28 : State.Mode == "compact" ? 238 : 344;
            // 挂到旧尺寸 Window 之前测量，避免原窗口的高度把展开面板裁成一条。
            body.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            double desiredHeight = body.DesiredSize.Height + 40;
            Width = width * State.Scale + 40; Height = desiredHeight;
            Content = margin;
            if (oldRight > 0 && !double.IsNaN(Left)) Left = oldRight - Width;
            if (IsLoaded) ClampToScreen();
        }
        private Border CreateRail()
        {
            var rail = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5,8,9,8) };
            var handle = Theme.Button(Theme.Icon("grip","muted"),"拖动浮条，方向键移动",null); handle.Width=20; handle.Padding=new Thickness(0); AttachDrag(handle,false); rail.Children.Add(handle);
            var summary = new StackPanel { Orientation = Orientation.Horizontal };
            summary.Children.Add(Theme.Ring(State,celebrateThisRender));
            var value = Theme.Text(State.Value,22,State.Tone == "green" ? "greenInk" : State.Tone); value.Margin=new Thickness(8,0,8,0); summary.Children.Add(value);
            summary.Children.Add(new Border { Width=1,Height=22,Background=Theme.Color("line"),Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center });
            var identity=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
            var appName=Theme.Text("Codex",12,"ink");appName.FontWeight=FontWeights.SemiBold;identity.Children.Add(appName);identity.Children.Add(Theme.Text(State.Status,12,"muted"));summary.Children.Add(identity);
            var toggle = Theme.Button(summary,"周剩余额度 " + State.Value + "，" + State.Status + "；点击切换详情",delegate { ChangeMode(State.Mode == "expanded" ? "compact" : "expanded",false); });
            toggle.Padding=new Thickness(0); rail.Children.Add(toggle);
            var surface = Theme.Surface(rail,34); surface.Width=238; surface.Height=68; surface.HorizontalAlignment=HorizontalAlignment.Right; return surface;
        }
        private Border CreatePanel()
        {
            var panel = new StackPanel { Margin=new Thickness(16,20,16,12) };
            var header = new DockPanel { Margin=new Thickness(4,0,4,18) };
            var actions = new StackPanel { Orientation=Orientation.Horizontal };
            actions.Children.Add(Theme.IconButton("pin",State.Pinned ? "取消固定面板" : "固定面板",delegate { State.Pinned=!State.Pinned; Render(); },State.Pinned));
            actions.Children.Add(Theme.IconButton("close","收起面板",delegate { ChangeMode("compact",false); },false));
            DockPanel.SetDock(actions,Dock.Right); header.Children.Add(actions);
            var title = new StackPanel(); title.Children.Add(Theme.Text("CODEX COMPANION",12,"muted"));
            var heading = Theme.Text("一眼，心里有数。",18,"ink"); heading.FontWeight=FontWeights.SemiBold; heading.Margin=new Thickness(0,4,0,0); title.Children.Add(heading); header.Children.Add(title); panel.Children.Add(header);
            var quota = new StackPanel(); var quotaHead = new DockPanel();
            var number = Theme.Text(State.Value,22,State.Tone == "green" ? "greenInk" : State.Tone); DockPanel.SetDock(number,Dock.Right); quotaHead.Children.Add(number); quotaHead.Children.Add(Theme.Text("周剩余额度",13,State.Tone == "green" ? "greenInk" : State.Tone)); quota.Children.Add(quotaHead);
            var track = new Grid { Height=4,Margin=new Thickness(0,10,0,8),Background=Theme.Color("line") };
            track.Children.Add(new Border { Height=4,Width=State.DataState == "ready" ? 276 * State.Remaining / 100.0 : 0,Background=Theme.Color(State.Tone),CornerRadius=new CornerRadius(2),HorizontalAlignment=HorizontalAlignment.Left }); quota.Children.Add(track);
            quota.Children.Add(Theme.Text(State.DataState == "error" ? "读取失败，不能确认最新额度" : State.DataState == "loading" ? "正在读取额度与任务状态…" : "示例 · 3 天 8 小时后重置",12,State.Tone == "green" ? "greenInk" : State.Tone));
            if (State.DataState == "error") quota.Children.Add(Theme.Button(Theme.Text("重新读取",12,"greenInk"),"重新读取示例数据",delegate { State.DataState="ready"; Render(); }));
            panel.Children.Add(new Border { Background=Theme.Color(State.Tone == "green" ? "greenSoft" : State.Tone == "red" ? "redSoft" : State.Tone == "amber" ? "amberSoft" : "canvas"),CornerRadius=new CornerRadius(16),Padding=new Thickness(16,14,16,12),Child=quota });
            var list = new StackPanel();
            list.Children.Add(GroupTitle("重点关注",State.Tasks.FindAll(t=>t.Watched).Count + " / 5"));
            if (State.Tasks.FindAll(t=>t.Watched).Count == 0) list.Children.Add(Theme.Text("点击任务旁的星标，让它留在这里。",12,"muted"));
            foreach (var task in State.Tasks.FindAll(t=>t.Watched)) list.Children.Add(CreateTask(task));
            if (State.Tasks.Exists(t=>!t.Watched)) { list.Children.Add(GroupTitle("其他任务",State.Tasks.FindAll(t=>!t.Watched).Count.ToString())); foreach(var task in State.Tasks.FindAll(t=>!t.Watched)) list.Children.Add(CreateTask(task)); }
            panel.Children.Add(new ScrollViewer { Content=list,MaxHeight=Math.Min(280,Math.Max(100,(WorkArea().Height-290*State.Scale-40)/State.Scale)),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
            var footer = new DockPanel { Margin=new Thickness(4,10,4,0) };
            var collapse = Theme.Button(Theme.Text("贴边收起",12,"greenInk"),"贴边收起",delegate { ChangeMode("docked",false); }); DockPanel.SetDock(collapse,Dock.Right); footer.Children.Add(collapse); footer.Children.Add(Theme.Text("设计验证 · 示例数据",12,"muted"));
            panel.Children.Add(new Border { BorderBrush=Theme.Color("line"),BorderThickness=new Thickness(0,1,0,0),Margin=new Thickness(0,14,0,0),Child=footer });
            var surface = Theme.Surface(panel,24); surface.Width=344; return surface;
        }
        private UIElement GroupTitle(string text,string count)
        {
            var row=new DockPanel { Margin=new Thickness(4,16,4,8) }; var badge=Theme.Text(count,12,"muted"); DockPanel.SetDock(badge,Dock.Right); row.Children.Add(badge); row.Children.Add(Theme.Text(text,12,"muted")); return row;
        }
        private UIElement CreateTask(DemoTask task)
        {
            var row=new DockPanel();
            var star=Theme.IconButton("star",(task.Watched?"取消关注：":"关注：")+task.Title,delegate { task.Watched=!task.Watched; Render(); },task.Watched); DockPanel.SetDock(star,Dock.Right); row.Children.Add(star);
            var contents=new DockPanel();
            string state=State.DataState == "error" ? "unavailable" : task.State;
            var symbol=new Border { Width=30,Height=30,CornerRadius=new CornerRadius(10),Background=Theme.Color(state=="attention"?"amberSoft":"greenSoft"),Child=Theme.Icon(state=="completed"?"check":state,state=="attention"?"amber":"greenInk"),Margin=new Thickness(0,0,10,0) }; DockPanel.SetDock(symbol,Dock.Left); contents.Children.Add(symbol);
            var labels=new StackPanel(); var title=Theme.Text(task.Title,13,"ink"); title.TextTrimming=TextTrimming.CharacterEllipsis; title.MaxWidth=206; labels.Children.Add(title); labels.Children.Add(Theme.Text(state=="unavailable"?"暂不可用":task.StateLabel,12,"muted")); contents.Children.Add(labels);
            var open=Theme.Button(contents,task.Title,delegate { if(Notify!=null) Notify("示例选择："+task.Title+"。未打开真实 Codex 对话。"); }); open.Padding=new Thickness(4,9,4,9); open.HorizontalContentAlignment=HorizontalAlignment.Stretch; open.IsEnabled=State.DataState=="ready"&&state!="unavailable"; row.Children.Add(open); return row;
        }
    }
}
