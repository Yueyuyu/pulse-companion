using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace PulseDesktopPreview
{
    internal sealed class PreviewTheme
    {
        private readonly Dictionary<string, object> tokens;
        private readonly ControlTemplate buttonTemplate;
        public PreviewTheme()
        {
            tokens = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tokens.json")));
            buttonTemplate = (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='10' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#EFF3F0'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='Background' Value='#E3ECE6'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#247AAB'/><Setter TargetName='surface' Property='BorderThickness' Value='2'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.55'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        }
        public Brush Color(string name)
        {
            var colors = (Dictionary<string, object>)tokens["color"];
            var token = (Dictionary<string, object>)colors[name];
            return (Brush)new BrushConverter().ConvertFromString((string)token["$value"]);
        }
        public TextBlock Text(string value, double size, string color)
        {
            return new TextBlock { Text = value, FontSize = size, Foreground = Color(color), FontFamily = new FontFamily("Segoe UI Variable, Segoe UI, Microsoft YaHei UI"), VerticalAlignment = VerticalAlignment.Center };
        }
        public Button Button(object content, string label, Action action)
        {
            var button = new Button { Content = content, Template = buttonTemplate, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(5), Foreground = Color("ink"), Cursor = System.Windows.Input.Cursors.Hand, MinHeight = 32, HorizontalContentAlignment = HorizontalAlignment.Center };
            AutomationProperties.SetName(button, label); button.ToolTip = label;
            if (action != null) button.Click += delegate { action(); };
            return button;
        }
        public Button IconButton(string name, string label, Action action, bool selected)
        {
            var button = Button(Icon(name, selected ? "greenInk" : "muted"), label, action);
            button.Width = 32; button.Height = 32;
            if (selected) button.Background = Color("greenSoft");
            return button;
        }
        public Border Surface(UIElement child, double radius)
        {
            // 阴影只作用于背景轮廓，不能把文字和图标一起栅格化后放大。
            var layers = new Grid();
            layers.Children.Add(new Border { Background = Color("surface"), CornerRadius = new CornerRadius(radius), IsHitTestVisible = false,
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 5, Opacity = .14, Color = System.Windows.Media.Color.FromRgb(27,45,36), RenderingBias = RenderingBias.Quality } });
            layers.Children.Add(new Border { Background = Color("surface"), BorderBrush = Color("line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius), Child = child });
            return new Border { Child = layers };
        }
        public UIElement Icon(string name, string color)
        {
            string data = name == "star" ? "M8,2 L9.8,5.6 13.8,6.2 10.9,9 11.6,13 8,11.1 4.4,13 5.1,9 2.2,6.2 6.2,5.6 Z" :
                name == "close" ? "M4,4 L12,12 M12,4 L4,12" : name == "pin" ? "M6,2 L12,4 10,7 11,10 8,9 5,11 4,8 7,6 Z M5,11 L2,14" :
                name == "check" ? "M3.5,8 L6.5,11 12.5,5" : name == "chevron" ? "M6,4 L10,8 6,12" :
                name == "arrow" ? "M4,12 L12,4 M5,4 L12,4 12,11" : name == "attention" ? "M8,3 L8,9 M8,12 L8,12.1" :
                name == "grip" ? "M6,4 L6,4.1 M6,8 L6,8.1 M6,12 L6,12.1 M10,4 L10,4.1 M10,8 L10,8.1 M10,12 L10,12.1" :
                name == "running" ? "M8,3 A5,5 0 1 1 3,8 M8,5 L8,8 10,9" : "M4,8 L12,8";
            var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(data), Stroke = Color(color), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 16, Height = 16 };
            return path;
        }
        public UIElement Ring(DemoState state, bool celebrating = false)
        {
            var grid = new Grid { Width = 42, Height = 42 };
            grid.Children.Add(new Ellipse { Width = 36, Height = 36, Stroke = Color("track"), StrokeThickness = 3.5 });
            if (state.DataState == "ready" && state.Remaining > 0)
            {
                double angle = Math.PI * 2 * Math.Min(99.999, state.Remaining) / 100;
                var figure = new PathFigure { StartPoint = new Point(21,3), IsClosed = false };
                figure.Segments.Add(new ArcSegment(new Point(21 + 18 * Math.Sin(angle),21 - 18 * Math.Cos(angle)),new Size(18,18),0,angle > Math.PI,SweepDirection.Clockwise,true));
                grid.Children.Add(new System.Windows.Shapes.Path { Data = new PathGeometry(new [] { figure }), Stroke = Color(state.Tone), StrokeThickness = 3.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            }
            var mark = new BotMarkView(this,state.Mood,state.MotionEnabled,celebrating) { HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
            grid.Children.Add(mark); return grid;
        }
    }
}
