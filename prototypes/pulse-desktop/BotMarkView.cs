using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace PulseDesktopPreview
{
    // 与 Lab 的原创 SVG 同构。独立矢量前景，不随阴影缓存成位图。
    internal sealed class BotMarkView : Viewbox
    {
        private readonly TranslateTransform movement = new TranslateTransform();
        private readonly RotateTransform rotation = new RotateTransform(0,20,32);
        private readonly ScaleTransform blinking = new ScaleTransform(1,1,20,22);
        private readonly TranslateTransform gazeMovement = new TranslateTransform();
        private readonly string mood;
        private readonly bool motionEnabled;
        private readonly bool celebrating;
        internal bool MotionActive { get { return movement.HasAnimatedProperties || blinking.HasAnimatedProperties || gazeMovement.HasAnimatedProperties; } }
        internal BotMarkView(PreviewTheme theme, string state, bool enabled, bool celebrate)
        {
            mood=state; motionEnabled=enabled; celebrating=celebrate;
            Width=30; Height=30; IsHitTestVisible=false; SnapsToDevicePixels=false;
            var canvas=new Canvas { Width=40,Height=40 };
            var body=new Canvas { Width=40,Height=40 };
            var transforms=new TransformGroup();transforms.Children.Add(rotation);transforms.Children.Add(movement);body.RenderTransform=transforms;
            string rim=state=="offline"?"muted":"botRim", light="botLight";
            body.Children.Add(Path(theme,"M12,31 L12,34 M28,31 L28,34",null,rim,4));
            body.Children.Add(Path(theme,"M8,13 L8,8 A3,3 0 0 1 14,8 L14,9 A26,26 0 0 1 26,9 L26,8 A3,3 0 0 1 32,8 L32,13 C34,15 35,18 35,21 C35,30 30,34 20,34 C10,34 5,30 5,21 C5,18 6,15 8,13 Z",state=="offline"?"line":"botShell",rim,1.2));
            body.Children.Add(Path(theme,"M12,12 Q20,9 28,12",null,light,1.5));
            var visor=new Rectangle { Width=22,Height=14,RadiusX=7,RadiusY=7,Fill=theme.Color(state=="offline"?"muted":"botVisor") };Canvas.SetLeft(visor,9);Canvas.SetTop(visor,15);body.Children.Add(visor);
            var gaze=new Canvas { RenderTransform=gazeMovement };
            if(state=="happy")gaze.Children.Add(Path(theme,"M13,23 Q15.5,18 18,23 M22,23 Q24.5,18 27,23",null,light,2.2));
            else if(state=="offline")gaze.Children.Add(Path(theme,"M13,22 L18,23 M22,23 L27,22",null,light,2));
            else
            {
                var eyes=new Canvas { RenderTransform=blinking };
                foreach(double x in new[]{14.0,23.0}) {var eye=new Rectangle{Width=3,Height=6,RadiusX=1.5,RadiusY=1.5,Fill=theme.Color(light)};Canvas.SetLeft(eye,x);Canvas.SetTop(eye,state=="attention"&&x==23?17:19);eyes.Children.Add(eye);}
                gaze.Children.Add(eyes);
            }
            body.Children.Add(gaze);canvas.Children.Add(body);Child=canvas;
            if(state=="attention")rotation.Angle=-8;
            Loaded+=delegate { RefreshMotion(); SystemParameters.StaticPropertyChanged+=SettingsChanged; };
            Unloaded+=delegate { SystemParameters.StaticPropertyChanged-=SettingsChanged; StopMotion(); };
            IsVisibleChanged+=delegate { RefreshMotion(); };
        }
        private static Path Path(PreviewTheme theme,string geometry,string fill,string stroke,double width)
        {
            return new Path { Data=Geometry.Parse(geometry),Fill=fill==null?null:theme.Color(fill),Stroke=stroke==null?null:theme.Color(stroke),StrokeThickness=width,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round };
        }
        private void SettingsChanged(object sender,PropertyChangedEventArgs args)
        {
            if(args.PropertyName=="ClientAreaAnimation")Dispatcher.BeginInvoke(new Action(RefreshMotion));
        }
        internal void StopMotion()
        {
            movement.BeginAnimation(TranslateTransform.YProperty,null);
            rotation.BeginAnimation(RotateTransform.AngleProperty,null);
            blinking.BeginAnimation(ScaleTransform.ScaleYProperty,null);
            gazeMovement.BeginAnimation(TranslateTransform.XProperty,null);
        }
        private static DoubleAnimationUsingKeyFrames Frames(int duration,bool loop,double[] times,double[] values)
        {
            var frames=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromMilliseconds(duration),RepeatBehavior=loop?RepeatBehavior.Forever:new RepeatBehavior(1),FillBehavior=FillBehavior.Stop };
            for(int i=0;i<times.Length;i++)frames.KeyFrames.Add(new SplineDoubleKeyFrame(values[i],KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(times[i])),new KeySpline(.23,1,.32,1)));
            return frames;
        }
        private void RefreshMotion()
        {
            StopMotion();
            if(!IsLoaded||!IsVisible||!motionEnabled||!SystemParameters.ClientAreaAnimation)return;
            if(mood=="idle"||mood=="working"||mood=="loading")blinking.BeginAnimation(ScaleTransform.ScaleYProperty,Frames(6400,true,new[]{0.0,4608,4688,4768,6400},new[]{1.0,1,.14,1,1}));
            if(mood=="working")
            {
                movement.BeginAnimation(TranslateTransform.YProperty,Frames(3200,true,new[]{0.0,1856,2048,2240,2432,3200},new[]{0.0,0,-1,-.5,0,0}));
                rotation.BeginAnimation(RotateTransform.AngleProperty,Frames(3200,true,new[]{0.0,1856,2048,2240,2432,3200},new[]{0.0,0,-4,3,0,0}));
            }
            if(mood=="loading")gazeMovement.BeginAnimation(TranslateTransform.XProperty,Frames(3200,true,new[]{0.0,1280,1536,1824,2080,3200},new[]{0.0,0,1.5,1.5,0,0}));
            if(celebrating)
            {
                movement.BeginAnimation(TranslateTransform.YProperty,Frames(240,false,new[]{0.0,108,180,240},new[]{0.0,-2,.5,0}));
                rotation.BeginAnimation(RotateTransform.AngleProperty,Frames(240,false,new[]{0.0,108,240},new[]{0.0,-6,0}));
            }
        }
    }
}
