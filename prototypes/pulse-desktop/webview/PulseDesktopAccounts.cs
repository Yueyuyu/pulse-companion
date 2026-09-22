using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexCompanion.PulseWebPreview {
  // 一次查询的临时对象，不可放入账户状态、凭据库、日志或 WebView DTO。
  internal sealed class PulseDesktopReading {
    internal string Source="none",AccessToken,SessionCookie,Organization;
    internal PulseQuotaWindow[] Windows=new PulseQuotaWindow[0];
    internal DateTime ObservedAt;
  }
  internal static class PulseDesktopAccounts {
    internal static Task<PulseDesktopReading> None(string id,CancellationToken token) {return Task.FromResult(new PulseDesktopReading());}
    internal static Task<PulseDesktopReading> Read(string id,CancellationToken token) {
      return Task.Run(()=> {
        token.ThrowIfCancellationRequested();
        string roaming=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        PulseDesktopReading result;
        if(id=="cursor")result=Cursor(Path.Combine(roaming,"Cursor"));
        else if(id=="grok-bot")result=Grok(Path.Combine(roaming,"Grok Bot"));
        else if(id=="claude")result=Claude(Path.Combine(roaming,"Claude"));
        else result=new PulseDesktopReading();
        token.ThrowIfCancellationRequested();return result;
      },token);
    }
    internal static string ValidCursorToken(string token) {
      if(!PulseCredential.SafeToken(token))throw new PulseAccountException("desktop-sign-in");
      object exp=PulseUsageParser.Get(PulseAccountLogin.Jwt(token),"exp");
      if(!(exp is int||exp is long||exp is double)||Convert.ToDouble(exp)<(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds+60)throw new PulseAccountException("desktop-sign-in");
      PulseAccountLogin.CursorCookie(token);return token;
    }
    static PulseDesktopReading Cursor(string profile) {
      var rows=PulseDesktopStorage.Query(Path.Combine(profile,"User","globalStorage","state.vscdb"),"SELECT value FROM ItemTable WHERE key='cursorAuth/accessToken' LIMIT 1");
      if(rows.Count==0||rows[0][0].Length==0)return new PulseDesktopReading();
      try{return new PulseDesktopReading{Source="desktop-session",AccessToken=ValidCursorToken(PulseDesktopStorage.Text(rows[0][0]))};}
      finally{Array.Clear(rows[0][0],0,rows[0][0].Length);}
    }
    internal static string GrokStoredAccess(object data) {
      string records=PulseUsageParser.Text(data,"cursor-accounts");
      if(records==null)return PulseUsageParser.Text(data,"cursor-access-token");
      var accounts=new JavaScriptSerializer{MaxJsonLength=1048576}.DeserializeObject(records);
      string active=PulseUsageParser.Text(accounts,"active");
      // 显式无当前账号就是退出；不拿另一个保存账号或旧顶层令牌顶替。
      if(active==null)return null;
      if(!System.Text.RegularExpressions.Regex.IsMatch(active,"^[0-9a-f]{64}$"))throw new PulseAccountException("local-storage");
      return PulseUsageParser.Text(PulseUsageParser.Get(PulseUsageParser.Get(accounts,"accounts"),active),"cursor-access-token");
    }
    static PulseDesktopReading Grok(string profile) {
      string stored=GrokStoredAccess(PulseDesktopStorage.Json(Path.Combine(profile,"sand-secrets.json")));
      if(String.IsNullOrEmpty(stored))return new PulseDesktopReading();
      if(stored.StartsWith("scoped:v1:")) {
        string[] parts=stored.Split(':');
        if(parts.Length!=4||!System.Text.RegularExpressions.Regex.IsMatch(parts[2],"^[0-9a-f]{64}$"))throw new PulseAccountException("local-storage");
        stored=parts[3];
      }
      byte[] key=PulseDesktopStorage.Key(profile);
      try{return new PulseDesktopReading{Source="desktop-session",AccessToken=ValidCursorToken(PulseDesktopStorage.Decrypt(Convert.FromBase64String(stored),key))};}
      finally{Array.Clear(key,0,key.Length);}
    }
    internal static string Organization(string raw) {
      Guid value;string decoded=raw==null?null:Uri.UnescapeDataString(raw).Trim('"');
      return Guid.TryParseExact(decoded,"D",out value)?value.ToString("D"):null;
    }
    internal static PulseDesktopReading ClaudeCache(object data,string organization,DateTime now) {
      var samples=PulseUsageParser.Get(data,"samples") as object[];
      if(!Equals(PulseUsageParser.Get(data,"version"),2)||samples==null||organization==null)return null;
      // 必须匹配当前组织，使用采样时间而非文件 mtime；只有两分钟内的非空读数有效。
      var latest=samples.Where(sample=>PulseUsageParser.Text(sample,"org")==organization&&PulseUsageParser.Get(sample,"t") is long)
        .OrderByDescending(sample=>(long)PulseUsageParser.Get(sample,"t")).FirstOrDefault();
      if(latest==null)return null;
      DateTime observed;try{observed=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds((long)PulseUsageParser.Get(latest,"t"));}catch{return null;}
      if(observed>now.AddSeconds(5)||now-observed>TimeSpan.FromMinutes(2))return null;
      var windows=PulseUsageParser.Parse("claude",PulseUsageParser.Get(latest,"u"));
      return windows.Length==0?null:new PulseDesktopReading{Source="desktop-cache",Windows=windows,ObservedAt=observed};
    }
    static PulseDesktopReading Claude(string profile) {
      string database=Path.Combine(profile,"Network","Cookies");if(!File.Exists(database))database=Path.Combine(profile,"Cookies");
      long now=(DateTime.UtcNow-new DateTime(1601,1,1)).Ticks/10;
      var rows=PulseDesktopStorage.Query(database,"SELECT host_key,name,value,encrypted_value FROM cookies WHERE host_key IN ('claude.ai','.claude.ai') AND path='/' AND name IN ('lastActiveOrg','sessionKey','sessionKeyV3') AND (expires_utc=0 OR expires_utc>"+now+") ORDER BY host_key DESC LIMIT 12");
      if(!rows.Any(row=>PulseDesktopStorage.Text(row[1])=="sessionKey"||PulseDesktopStorage.Text(row[1])=="sessionKeyV3"))return new PulseDesktopReading();
      var versionRows=PulseDesktopStorage.Query(database,"SELECT value FROM meta WHERE key='version' LIMIT 1");int version;
      if(versionRows.Count!=1||!Int32.TryParse(PulseDesktopStorage.Text(versionRows[0][0]),out version))throw new PulseAccountException("local-storage");
      byte[] key=PulseDesktopStorage.Key(profile);
      try {
        Func<byte[][],string> decode=row=>row[2].Length>0?PulseDesktopStorage.Text(row[2]):PulseDesktopStorage.Decrypt(row[3],key,PulseDesktopStorage.Text(row[0]),version);
        var organizationRow=rows.FirstOrDefault(row=>PulseDesktopStorage.Text(row[1])=="lastActiveOrg");
        string organization=organizationRow==null?null:Organization(decode(organizationRow));
        // 不查询账户资料来猜组织、不选择任意组织；当前组织缺失时诚实降级。
        if(organization==null)throw new PulseAccountException("desktop-organization");
        PulseDesktopReading cache=null;
        try{cache=ClaudeCache(PulseDesktopStorage.Json(Path.Combine(profile,"plan-usage-history.json")),organization,DateTime.UtcNow);}
        catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}catch(PulseAccountException){}
        if(cache!=null)return cache;
        var pairs=rows.Where(row=>PulseDesktopStorage.Text(row[1])!="lastActiveOrg").GroupBy(row=>PulseDesktopStorage.Text(row[1])).Select(group=> {
          string value=decode(group.First());
          if(!PulseCredential.SafeToken(value)||value.Contains(";")||value.Contains(","))throw new PulseAccountException("desktop-sign-in");
          return group.Key+"="+value;
        });
        return new PulseDesktopReading{Source="desktop-session",Organization=organization,SessionCookie=String.Join("; ",pairs)};
      } finally {Array.Clear(key,0,key.Length);foreach(var row in rows)foreach(var bytes in row)Array.Clear(bytes,0,bytes.Length);}
    }
  }
}
