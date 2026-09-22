using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexCompanion.PulseWebPreview {
  internal static class PulseApplications {
    internal static readonly string[] Ids={"codex","cursor","claude","grok-bot","zcode","kimi","doubao-work","workbuddy"};
    internal static bool CanAuthorize(string id) {return id=="cursor"||id=="claude"||id=="grok-bot";}
  }

  internal sealed class PulseCredential {
    public string AccessToken,RefreshToken;
    public DateTime ExpiresAt;
    internal bool Valid {get{return SafeToken(AccessToken)&& (String.IsNullOrEmpty(RefreshToken)||SafeToken(RefreshToken));}}
    internal static bool SafeToken(string token) {return !String.IsNullOrEmpty(token)&&token.Length<=16384&&token.All(c=>c>32&&c<127);}
  }

  // 此库只保存 Companion 自己授权获得的备用凭据；桌面借用凭据不进入此库。
  internal sealed class PulseCredentialVault {
    readonly string directory;
    static readonly byte[] entropy=Encoding.UTF8.GetBytes("PulseCompanion.Accounts.v1");
    internal PulseCredentialVault(string directory) {this.directory=directory;}
    string PathFor(string id) {
      if(!PulseApplications.CanAuthorize(id))throw new ArgumentException("不支持的授权应用");
      return Path.Combine(directory,id+".dpapi");
    }
    internal PulseCredential Load(string id) {
      string path=PathFor(id);if(!File.Exists(path))return null;
      if(new FileInfo(path).Length>131072)throw new InvalidDataException("凭据文件异常");
      var raw=ProtectedData.Unprotect(File.ReadAllBytes(path),entropy,DataProtectionScope.CurrentUser);
      try {
        var credential=new JavaScriptSerializer().Deserialize<PulseCredential>(Encoding.UTF8.GetString(raw));
        if(credential==null||!credential.Valid)throw new InvalidDataException("凭据文件异常");
        return credential;
      } finally {Array.Clear(raw,0,raw.Length);}
    }
    internal void Save(string id,PulseCredential credential) {
      if(credential==null||!credential.Valid)throw new InvalidDataException("授权响应无效");
      // 损坏或不可解密的原文件不静默覆盖；显式“断开”才可删除。
      Load(id);
      Directory.CreateDirectory(directory);
      string path=PathFor(id),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
      var raw=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(credential));
      try {
        File.WriteAllBytes(temporary,ProtectedData.Protect(raw,entropy,DataProtectionScope.CurrentUser));
        if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
      } finally {Array.Clear(raw,0,raw.Length);if(File.Exists(temporary))File.Delete(temporary);}
    }
    internal void Delete(string id) {File.Delete(PathFor(id));}
  }

  internal sealed class PulseQuotaWindow {
    public string label,resetsAt;
    public double remaining;
  }
  internal static class PulseUsageParser {
    internal static Dictionary<string,object> Object(object value) {return value as Dictionary<string,object>??new Dictionary<string,object>();}
    internal static object Get(object value,string key) {object result;return Object(value).TryGetValue(key,out result)?result:null;}
    internal static string Text(object value,string key) {return Get(value,key) as string;}
    static double? Number(object value) {
      if(!(value is double||value is decimal||value is int||value is long))return null;
      double number=Convert.ToDouble(value);return Double.IsNaN(number)||Double.IsInfinity(number)?null:(double?)number;
    }
    static string Date(object value) {
      DateTimeOffset parsed;
      var text=value as string;
      if(text!=null&&DateTimeOffset.TryParse(text,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal,out parsed))return parsed.ToUniversalTime().ToString("o");
      double? timestamp=Number(value);
      if(timestamp.HasValue&&timestamp>0&&timestamp<100000000000000)try {return new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddMilliseconds(timestamp>100000000000?timestamp.Value:timestamp.Value*1000).ToString("o");}catch {}
      return null;
    }
    static void Add(List<PulseQuotaWindow> windows,string label,object used,object reset) {
      double? number=Number(used);if(!number.HasValue||number<0)return;
      string at=Date(reset);DateTimeOffset parsed;
      if(at!=null&&DateTimeOffset.TryParse(at,out parsed)&&parsed<=DateTimeOffset.UtcNow)return;
      windows.Add(new PulseQuotaWindow{label=label,remaining=Math.Round(Math.Max(0,100-number.Value),2),resetsAt=at});
    }
    internal static PulseQuotaWindow[] Parse(string id,object data) {
      var windows=new List<PulseQuotaWindow>();
      if(id=="cursor") {
        object reset=Get(data,"billingCycleEnd");
        foreach(var pool in new[]{new{value=Get(Get(data,"individualUsage"),"plan"),label="个人"},new{value=Get(Get(data,"teamUsage"),"pooled"),label="团队"}}) {
          if(Equals(Get(pool.value,"enabled"),false))continue;
          int before=windows.Count;
          Add(windows,pool.label+" Auto 月度剩余",Get(pool.value,"autoPercentUsed"),reset);
          Add(windows,pool.label+" API 月度剩余",Get(pool.value,"apiPercentUsed"),reset);
          if(before==windows.Count) {
            var used=Number(Get(pool.value,"used"));var limit=Number(Get(pool.value,"limit"));
            if(used.HasValue&&limit>0)Add(windows,pool.label+" 月度剩余",used.Value/limit.Value*100,reset);
          }
        }
      } else if(id=="grok-bot") {
        if(!Equals(Get(data,"usesPooledEnterpriseAllowance"),true)&&!Equals(Get(data,"includedLimitZero"),true)&&Equals(Get(data,"hasNonZeroIncludedLimit"),true))
          Add(windows,"Grok Bot 周剩余",Get(data,"usagePercent"),Get(data,"nextResetTimestampUtc"));
      } else if(id=="claude") {
        var limits=Get(data,"limits") as object[];
        // 锁定或未知限制语义不能只隐藏该窗口，再把另一个窗口画成健康绿色。
        if(limits!=null&&limits.Any(limit=>Blocked(limit)))return new PulseQuotaWindow[0];
        if(limits!=null)foreach(var limit in limits) {
          string kind=Text(limit,"kind"),label=kind=="session"?"5 小时剩余":kind=="weekly_all"?"周剩余":kind=="weekly_scoped"?"模型周剩余":null;
          if(label==null||Get(limit,"locked_reason")!=null)continue;
          string model=Text(Get(Get(limit,"scope"),"model"),"display_name");
          if(kind=="weekly_scoped"&&!String.IsNullOrWhiteSpace(model))label=model.Substring(0,Math.Min(32,model.Length))+" 周剩余";
          Add(windows,label,Get(limit,"percent"),Get(limit,"resets_at"));
        }
        if(limits==null||limits.Length==0)foreach(var kind in new[]{"five_hour","seven_day"}) {
          if(Blocked(Get(data,kind)))return new PulseQuotaWindow[0];
          Add(windows,kind=="five_hour"?"5 小时剩余":"周剩余",Get(Get(data,kind),"utilization"),Get(Get(data,kind),"resets_at"));
        }
      }
      return windows.Take(8).ToArray();
    }
    static bool Blocked(object limit) {
      var severity=Text(limit,"severity");
      return Get(limit,"locked_reason")!=null||(severity!=null&&!new[]{"normal","ok","none","healthy","warning","warn"}.Contains(severity.ToLowerInvariant()));
    }
  }
}
