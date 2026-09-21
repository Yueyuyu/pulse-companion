using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexCompanion.PulseWebPreview {
  internal sealed class PulseWindowSettings {
    public int Version=1;
    public bool Pinned,HasPosition;
    public double Left,Top,Right,Scale=1;
    public string Side="right",Mode="compact";
    internal static readonly string FilePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexQuotaOverlay","pulse-window.json");
    internal static PulseWindowSettings Load() {
      try {
        var value=File.Exists(FilePath)?new JavaScriptSerializer().Deserialize<PulseWindowSettings>(File.ReadAllText(FilePath)):new PulseWindowSettings();
        if(value==null||value.Version!=1) return new PulseWindowSettings();
        if(value.Scale!=1&&value.Scale!=1.25&&value.Scale!=1.5&&value.Scale!=2)value.Scale=1;
        if(value.Side!="left"&&value.Side!="right")value.Side="right";
        if(value.Mode!="compact"&&value.Mode!="expanded"&&value.Mode!="docked")value.Mode="compact";
        if(Double.IsNaN(value.Left)||Double.IsInfinity(value.Left)||Double.IsNaN(value.Top)||Double.IsInfinity(value.Top))value.HasPosition=false;
        return value;
      } catch {return new PulseWindowSettings();}
    }
    internal bool Save() {
      try {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        File.WriteAllText(FilePath+".tmp",new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));
        if(File.Exists(FilePath))File.Replace(FilePath+".tmp",FilePath,null);else File.Move(FilePath+".tmp",FilePath);
        return true;
      } catch {return false;}
    }
  }
}
