using System;
using System.Windows;

namespace CodexCompanion.PulseWebPreview {
  internal static class PulseWindowPlacement {
    // 保留可重新抓取的侧轨，不把整个详情面板强行塞回工作区。
    internal static Point KeepReachable(Point position,Rect area,Rect rail,double scale) {
      double gripX=Math.Min(16,rail.Width)*scale,gripY=Math.Min(24,rail.Height)*scale;
      return new Point(
        Math.Max(area.Left+gripX-rail.Right*scale,Math.Min(position.X,area.Right-gripX-rail.Left*scale)),
        Math.Max(area.Top+gripY-rail.Bottom*scale,Math.Min(position.Y,area.Bottom-gripY-rail.Top*scale)));
    }
    internal static string SnapSide(bool enabled,Point position,Rect area,Rect rail,double scale) {
      if(!enabled)return null;
      if(Math.Abs(position.X+(rail.Right+6)*scale-area.Right)<28)return "right";
      if(Math.Abs(position.X+(rail.Left-6)*scale-area.Left)<28)return "left";
      return null;
    }
  }
}
