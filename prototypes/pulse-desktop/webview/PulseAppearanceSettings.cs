using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexCompanion.PulseWebPreview {
  internal sealed class PulseAppearanceSettings {
    public int Version=1;
    public Dictionary<string,string> Applications=new Dictionary<string,string>();
    string path;
    bool loadFailed;
    bool brandOnly;
    // 公开宿主绝不加载或保存本机专用版的图标偏好；两个构建使用同一套业务模型。
    internal static PulseAppearanceSettings ForBuild(string file,bool verification) {
      if(PulseDistribution.PublicBrand)return new PulseAppearanceSettings {brandOnly=true};
      return verification?new PulseAppearanceSettings():Load(file);
    }
    internal static string FilePath {get {return Path.Combine(Path.GetDirectoryName(PulseWindowSettings.FilePath),"pulse-applications.json");}}
    internal static PulseAppearanceSettings Load(string file) {
      try {
        var value=File.Exists(file)?new JavaScriptSerializer().Deserialize<PulseAppearanceSettings>(File.ReadAllText(file)):new PulseAppearanceSettings();
        if(value==null||value.Version!=1||value.Applications==null)throw new InvalidDataException();
        foreach(var pair in value.Applications)if(!ValidId(pair.Key)||!ValidMode(pair.Value))throw new InvalidDataException();
        value.path=file;return value;
      } catch {return new PulseAppearanceSettings {path=file,loadFailed=true};}
    }
    internal static bool ValidId(string id) {return !String.IsNullOrEmpty(id)&&System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-z][a-z0-9-]{0,39}$");}
    internal static bool ValidMode(string mode) {return mode=="brand"||mode=="robot";}
    internal string Get(string id) {if(brandOnly)return "brand";string mode;return Applications.TryGetValue(id,out mode)?mode:id=="codex"?"robot":"brand";}
    internal bool Set(string id,string mode) {
      if(brandOnly||loadFailed||!ValidId(id)||!ValidMode(mode))return false;
      string old;bool existed=Applications.TryGetValue(id,out old);Applications[id]=mode;
      try {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path+".tmp",new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));
        if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);
        return true;
      } catch {if(existed)Applications[id]=old;else Applications.Remove(id);return false;}
    }
  }
}
