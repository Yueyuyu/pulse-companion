using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace CodexCompanion.PulseWebPreview {
  internal static class PulsePlacementVerification {
    static void Require(bool value,string label) {if(!value)throw new Exception(label);}
    internal static void Run(string root,List<string> checks) {
      string path=Path.Combine(root,"window.json");
      var settings=PulseWindowSettings.Load(path);
      Require(!settings.AutoDock,"新配置默认自由移动");
      File.WriteAllText(path,"{\"Version\":1,\"Pinned\":true,\"HasPosition\":true,\"Left\":100,\"Top\":50,\"Right\":454}");
      settings=PulseWindowSettings.Load(path);
      Require(!settings.AutoDock&&settings.Pinned&&settings.HasPosition&&settings.Left==100,"兼容旧配置，不丢位置和固定");
      Require(settings.SetAutoDock(true,path)&&PulseWindowSettings.Load(path).AutoDock,"贴边开启持久化");
      Require(settings.SetAutoDock(false,path)&&!PulseWindowSettings.Load(path).AutoDock,"贴边关闭持久化");
      string blocked=Path.Combine(root,"window-blocked");File.WriteAllText(blocked,"fixture");
      Require(!settings.SetAutoDock(true,Path.Combine(blocked,"settings.json"))&&!settings.AutoDock,"写盘失败回滚");
      checks.Add("window-auto-dock-default-legacy-restart-rollback");
      foreach(double scale in new[]{1.0,1.25,1.5,2.0})foreach(double origin in new[]{-1920.0,0,2560})foreach(double x in new[]{6.0,284}) {
        var area=new Rect(origin,-200,1920,1040);var rail=new Rect(x,6,64,254);
        var partlyOutside=new Point(area.Left-rail.Left*scale-20*scale,area.Top+100);
        Require(PulseWindowPlacement.KeepReachable(partlyOutside,area,rail,scale)==partlyOutside,"允许部分越界");
        foreach(var far in new[]{new Point(-20000,-20000),new Point(20000,20000)}) {
          var safe=PulseWindowPlacement.KeepReachable(far,area,rail,scale);
          var visible=new Rect(safe.X+rail.X*scale,safe.Y+rail.Y*scale,rail.Width*scale,rail.Height*scale);visible.Intersect(area);
          Require(!visible.IsEmpty&&visible.Width>=16*scale&&visible.Height>=24*scale,"跨屏和缩放保留可抓区域");
        }
        var right=new Point(area.Right-(rail.Right+6)*scale,100);
        Require(PulseWindowPlacement.SnapSide(false,right,area,rail,scale)==null,"开关关闭不吸附");
        Require(PulseWindowPlacement.SnapSide(true,right,area,rail,scale)=="right","右侧吸附");
        var left=new Point(area.Left-(rail.Left-6)*scale,100);
        Require(PulseWindowPlacement.SnapSide(true,left,area,rail,scale)=="left","左侧吸附");
        Require(PulseWindowPlacement.SnapSide(true,new Point(left.X+100,100),area,rail,scale)==null,"远离边缘不吸附");
      }
      checks.Add("window-reachable-rail-partial-offscreen-multiple-origins-scales-snap-toggle");
    }
  }
}
