using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

namespace CodexCompanion.PulseWebPreview {
  internal static class PulseIconVerification {
    internal static void Run(List<string> checks) {
      // 直接读取已编译资源，不创建托盘，不依赖当前机器缩放，不写真实设置。
      foreach(int size in new[]{16,20,24,28,32,40,48,64}) {
        using(var icon=PulseTrayIcon.Load(size))
        using(var bitmap=icon.ToBitmap()) {
          if(icon.Width!=size||icon.Height!=size)throw new Exception("托盘图标缺少精确尺寸："+size);
          if(bitmap.GetPixel(0,0).A!=0)throw new Exception("图标透明边界丢失");
          int colored=0,light=0,antialias=0;
          for(int y=0;y<size;y++)for(int x=0;x<size;x++) {
            Color pixel=bitmap.GetPixel(x,y);
            if(pixel.A>128&&pixel.R>100&&pixel.G>40&&pixel.B<80)colored++;
            if(pixel.A>128&&pixel.R>130&&pixel.G>130&&pixel.B>130)light++;
            if(pixel.A>0&&pixel.A<255)antialias++;
          }
          if(colored<8||light<3||antialias<4)throw new Exception("图标圆环、脉冲线或透明抗锯齿丢失："+size);
        }
      }
      using(var icon=Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location)) {
        if(icon==null)throw new Exception("EXE 缺少原生应用图标");
        using(var bitmap=icon.ToBitmap()) {
          int colored=0;
          for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++) {
            Color pixel=bitmap.GetPixel(x,y);
            if(pixel.A>128&&pixel.R>100&&pixel.G>40&&pixel.B<80)colored++;
          }
          if(colored<8)throw new Exception("EXE 图标仍是系统占位图标");
        }
      }
      checks.Add("pulse-icons-native-executable-exact-tray-sizes-alpha-color-trace");
    }
  }
}
