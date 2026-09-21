using System;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace CodexCompanion.PulseWebPreview {
  // 内容截图看不到 SetWindowRgn 的裁切；必须对照系统当前实际区域逐像素检查。
  internal sealed class WindowSurfaceAudit {
    public int VisiblePixels {get;set;}
    public int TransparentPixels {get;set;}
    public int PartialAlphaPixels {get;set;}
    public int ClippedPixels {get;set;}
    public int ClippedPartialAlphaPixels {get;set;}
    public int ExcludedTransparentPixels {get;set;}
    public int RegionType {get;set;}
    public double DpiX {get;set;}
    public double DpiY {get;set;}
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr window,IntPtr region);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr region,int x,int y);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);

    internal static WindowSurfaceAudit Inspect(BitmapSource bitmap,IntPtr window) {
      var result=new WindowSurfaceAudit {DpiX=bitmap.DpiX,DpiY=bitmap.DpiY};
      var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];
      bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
      var region=CreateRectRgn(0,0,0,0);
      if(region==IntPtr.Zero) throw new InvalidOperationException("无法读取窗口裁切区域。");
      try {
        result.RegionType=GetWindowRgn(window,region);
        if(result.RegionType==0) throw new InvalidOperationException("无法取得实际窗口区域，不能判定软边缘是否保留。");
        for(int y=0;y<bitmap.PixelHeight;y++) for(int x=0;x<bitmap.PixelWidth;x++) {
          var alpha=pixels[(y*bitmap.PixelWidth+x)*4+3];
          if(alpha==0) {
            result.TransparentPixels++;
            if(result.RegionType!=0 && !PtInRegion(region,x,y)) result.ExcludedTransparentPixels++;
            continue;
          }
          result.VisiblePixels++;
          if(alpha<255) result.PartialAlphaPixels++;
          if(result.RegionType!=0 && !PtInRegion(region,x,y)) {
            result.ClippedPixels++;
            if(alpha<255) result.ClippedPartialAlphaPixels++;
          }
        }
      } finally {DeleteObject(region);}
      return result;
    }
  }
}
