using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace CodexCompanion.PulseWebPreview {
  internal static class PulseDistribution {
    sealed class RendererProof {
      public int schemaVersion {get;set;}
      public string distribution {get;set;}
      public Dictionary<string,string> files {get;set;}
    }
    internal static bool PublicBrand {get {
#if PULSE_PUBLIC_BRAND
      return true;
#else
      return false;
#endif
    }}
    // 生产启动与无窗验收共用：公开宿主只加载与其一起构建的字节，绝不以“缺少私有标记”证明可分发。
    internal static bool RendererEligible(string root,bool publicBrand) {
      try {
        bool local=File.Exists(Path.Combine(root,"LOCAL-ONLY.json"));
        string marker=Path.Combine(root,"PUBLIC-BRAND.json");
        if(!publicBrand)return local&&!File.Exists(marker);
        if(local||!File.Exists(marker)||Directory.Exists(Path.Combine(root,"pulse-local")))return false;
        foreach(string item in new[]{root,marker,Path.Combine(root,"assets")})
          if((File.GetAttributes(item)&FileAttributes.ReparsePoint)!=0)return false;
        string manifest;
        using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("PulseCompanion.PublicRenderer")) {
          if(resource==null)return false;using(var reader=new StreamReader(resource))manifest=reader.ReadToEnd();
        }
        if(new FileInfo(marker).Length>16384||File.ReadAllText(marker)!=manifest)return false;
        var proof=new JavaScriptSerializer().Deserialize<RendererProof>(manifest);
        if(proof.schemaVersion!=1||proof.distribution!="public-brand"||proof.files==null||proof.files.Count==0)return false;
        var top=Directory.GetFileSystemEntries(root).Select(Path.GetFileName).OrderBy(name=>name,StringComparer.Ordinal);
        if(!top.SequenceEqual(new[]{"PUBLIC-BRAND.json","assets","index.html"}))return false;
        var assets=Directory.GetFileSystemEntries(Path.Combine(root,"assets")).Select(path=>"assets/"+Path.GetFileName(path));
        if(!new HashSet<string>(assets.Concat(new[]{"index.html"}),StringComparer.Ordinal).SetEquals(proof.files.Keys))return false;
        using(var sha=SHA256.Create())foreach(var pair in proof.files){
          string file=Path.Combine(root,pair.Key.Replace('/',Path.DirectorySeparatorChar));
          if((File.GetAttributes(file)&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0)return false;
          using(var stream=File.OpenRead(file))
            if(!String.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-",""),pair.Value,StringComparison.OrdinalIgnoreCase))return false;
        }
        return true;
      } catch {return false;}
    }
  }
  internal sealed class PulseStartupOptions {
    internal bool Live, Background, InspectWindow, VerifyLive, SelfTest;
    internal string ReportPath, VisualReportPath;

    internal static PulseStartupOptions Parse(string[] args) {
      var result=new PulseStartupOptions();
      // 双击程序也只启动正常后台；示例和可见验收必须有明确参数。
      if(args.Length==0) {result.Live=result.Background=true;return result;}
      if(args.Length==2&&(args[0]=="--verify"||args[0]=="--verify-live"||args[0]=="--live-self-test")) {
        if(String.IsNullOrWhiteSpace(args[1])||args[1].StartsWith("--",StringComparison.Ordinal))throw new ArgumentException("缺少验收输出路径");
        result.ReportPath=Path.GetFullPath(args[1]);
        if(args[0]=="--verify")result.VisualReportPath=result.ReportPath;
        else if(args[0]=="--verify-live")result.Live=result.VerifyLive=true;
        else result.SelfTest=true;
        return result;
      }
      bool demo=false;
      foreach(string arg in args) {
        if(arg=="--live"&&!result.Live)result.Live=true;
        else if(arg=="--background"&&!result.Background)result.Background=true;
        else if(arg=="--inspect-window"&&!result.InspectWindow)result.InspectWindow=true;
        else if(arg=="--demo"&&!demo)demo=true;
        else throw new ArgumentException("不支持或重复的启动参数");
      }
      if((demo&&(result.Live||result.Background))||(!demo&&!result.Live)||(result.Background&&result.InspectWindow))
        throw new ArgumentException("演示、验收与后台参数不能混用");
      return result;
    }
  }

  internal static class PulseBackgroundPause {
    internal static string FilePath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexQuotaOverlay","pulse-background.pause");}}
    internal static bool IsPaused(string path) {return File.Exists(path);}
    internal static bool TryPause(string path) {
      try {Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"paused");return true;}
      catch {return false;}
    }
  }
}
