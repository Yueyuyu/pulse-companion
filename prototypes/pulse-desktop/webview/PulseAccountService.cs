using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodexCompanion.PulseWebPreview {
  internal sealed class PulseAccountState {
    internal readonly string Id;
    internal string AuthState="signed-out",QuotaState="signed-out",Notice="";
    internal string ReadMode="auto",QuotaSource="none";
    internal PulseCredential Credential;
    internal bool Corrupt;
    internal bool BorrowedRead;
    internal int Generation;
    internal CancellationTokenSource Operation;
    internal DateTime NextRead=DateTime.MinValue,UpdatedAt=DateTime.MinValue;
    internal DateTime RetryAfter=DateTime.MinValue;
    internal PulseQuotaWindow[] Windows=new PulseQuotaWindow[0];
    internal PulseAccountState(string id) {Id=id;}
    internal object Snapshot() {
      var fresh=Windows.Where(window=>window.resetsAt==null||DateTimeOffset.Parse(window.resetsAt)>DateTimeOffset.UtcNow).ToArray();
      bool ready=QuotaState=="ready"&&fresh.Length>0&&DateTime.UtcNow-UpdatedAt<TimeSpan.FromMinutes(QuotaSource=="desktop-cache"?2:3);
      return new {schemaVersion=1,source="companion-live",quotaState=QuotaState=="ready"&&!ready?"loading":QuotaState,tasksState="unsupported",
        remaining=ready?(double?)fresh[0].remaining:null,resetsAt=ready?fresh[0].resetsAt:null,
        quotaLabel=ready?fresh[0].label:"账户额度",quotaWindows=ready?fresh:new PulseQuotaWindow[0],
        quotaUpdatedAt=UpdatedAt==DateTime.MinValue?null:UpdatedAt.ToString("o"),tasks=new object[0],notice=Notice,
        authState=AuthState,canAuthorize=PulseApplications.CanAuthorize(Id),canDisconnect=PulseApplications.CanAuthorize(Id)&&(ReadMode!="off"||Credential!=null||Corrupt),authorizationBlocked=Corrupt,
        readMode=ReadMode,quotaSource=QuotaSource};
    }
  }
  // 此类由 WPF UI 线程拥有；异步结果先检查 generation，取消或断开后的迟到结果不能写盘。
  internal sealed class PulseAccountService : IDisposable {
    readonly Dictionary<string,PulseAccountState> accounts=new Dictionary<string,PulseAccountState>();
    readonly PulseCredentialVault vault;
    readonly PulseAccountHttp http;
    readonly Func<string,PulseAccountHttp,CancellationToken,Task<PulseCredential>> login;
    readonly Func<string,CancellationToken,Task<PulseDesktopReading>> desktopRead;
    readonly string directory;
    readonly bool enabled;
    HashSet<string> openApplications=new HashSet<string>();
    bool disposed;
    internal event Action Changed;
    internal PulseAccountService(bool enabled,string directory=null,PulseAccountHttp transport=null,Func<string,PulseAccountHttp,CancellationToken,Task<PulseCredential>> login=null,Func<string,CancellationToken,Task<PulseDesktopReading>> desktopRead=null) {
      this.enabled=enabled;
      this.login=login??PulseAccountLogin.Login;
      this.directory=directory??Path.Combine(Path.GetDirectoryName(PulseWindowSettings.FilePath),"accounts");
      this.desktopRead=desktopRead??PulseDesktopAccounts.Read;
      vault=new PulseCredentialVault(this.directory);
      http=transport??new PulseAccountHttp();
      foreach(string id in PulseApplications.Ids.Where(value=>value!="codex")) {
        var account=new PulseAccountState(id);accounts.Add(id,account);
        if(!PulseApplications.CanAuthorize(id)) {account.AuthState=account.QuotaState="unsupported";account.Notice="此应用的只读额度与实时任务接口尚未接通；不会使用其他产品的额度替代。";continue;}
        if(!enabled)continue;
        try {string mode=Path.Combine(this.directory,id+".mode");if(File.Exists(mode)){string saved=File.ReadAllText(mode);account.ReadMode=new[]{"auto","web","off"}.Contains(saved)?saved:"off";}}
        catch {account.ReadMode="off";account.Notice="读取偏好失败，已停止自动查询；可手动恢复。";}
        try {account.Credential=vault.Load(id);if(account.Credential!=null){account.AuthState="connected";account.QuotaState="loading";}}
        catch {account.Corrupt=true;account.QuotaState="error";account.AuthState="error";account.Notice="备用授权文件无法解密，已保留原文件；桌面自动读取仍可用。";}
        if(account.ReadMode=="off"){account.AuthState="signed-out";account.QuotaState="unavailable";account.Notice="已停止读取；恢复前不会访问应用登录状态。";}
      }
    }
    internal IEnumerable<object> Snapshots(PulseAppearanceSettings appearance) {
      foreach(string id in PulseApplications.Ids) {
        PulseAccountState account;if(openApplications.Contains(id)&&accounts.TryGetValue(id,out account))yield return new{id=account.Id,iconMode=appearance.Get(account.Id),state=account.Snapshot()};
      }
    }
    internal void SetOpenApplications(IEnumerable<string> ids) {
      var next=new HashSet<string>(ids.Where(PulseBackgroundPolicy.AcceptsApplication));
      foreach(var account in accounts.Values)if(!next.Contains(account.Id)&&account.BorrowedRead)Cancel(account);
      foreach(var account in accounts.Values)if(next.Contains(account.Id)&&!openApplications.Contains(account.Id)) {
        account.Windows=new PulseQuotaWindow[0];account.QuotaSource="none";
        if(account.QuotaState!="rate-limited")account.NextRead=DateTime.MinValue;
        if(PulseApplications.CanAuthorize(account.Id)&&account.ReadMode!="off"&&account.QuotaState!="rate-limited")account.QuotaState="loading";
      }
      // 已发出的授权/令牌刷新允许完成，避免丢弃服务端已轮换的令牌；关闭后不再发起周期查询。
      // 隐藏不是断开，不清除授权、图标或已有账户状态。
      openApplications=next;
    }
    void Notify() {if(!disposed&&Changed!=null)Changed();}
    void SetMode(PulseAccountState account,string mode) {
      Directory.CreateDirectory(directory);string path=Path.Combine(directory,account.Id+".mode"),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
      try{File.WriteAllText(temporary,mode);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);account.ReadMode=mode;}
      finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    internal void Tick() {
      if(!enabled||disposed)return;
      foreach(var account in accounts.Values)if(openApplications.Contains(account.Id)&&PulseApplications.CanAuthorize(account.Id)&&account.ReadMode!="off"&&account.AuthState!="authorizing"&&account.Operation==null&&DateTime.UtcNow>=account.NextRead)Read(account);
    }
    internal void Command(string id,string command) {
      PulseAccountState account;if(!enabled||disposed||!accounts.TryGetValue(id,out account)||!PulseApplications.CanAuthorize(id))return;
      if((command=="authorize"||command=="refresh"||command=="resume-auto")&&!openApplications.Contains(id))return;
      if(command=="authorize"&&account.Operation==null&&!account.Corrupt)Authorize(account);
      else if(command=="cancel-authorization"&&account.AuthState=="authorizing") {
        Cancel(account);account.AuthState=account.Credential==null?"signed-out":"connected";account.QuotaState=account.Credential==null?"signed-out":"loading";account.Notice="已取消登录，晚到的授权不会保存。";Notify();
      } else if(command=="disconnect-account") {
        Cancel(account);account.Credential=null;account.Windows=new PulseQuotaWindow[0];account.ReadMode="off";account.QuotaSource="none";
        try {SetMode(account,"off");vault.Delete(id);account.Corrupt=false;account.AuthState="signed-out";account.QuotaState="unavailable";account.Notice="已停止自动读取并删除备用授权；不影响原应用登录，重启后仍保持停止。";}
        catch {account.Corrupt=true;account.AuthState=account.QuotaState="error";account.Notice="本机授权文件删除失败，已停止查询，请重试断开。";}
        Notify();
      } else if(command=="resume-auto") {
        Cancel(account);account.Windows=new PulseQuotaWindow[0];account.QuotaSource="none";
        try{SetMode(account,"auto");account.AuthState="signed-out";account.QuotaState="loading";account.NextRead=DateTime.MinValue;account.Notice="正在自动读取桌面来源。";}
        catch{account.ReadMode="off";account.QuotaState="error";account.Notice="无法保存读取偏好，保持停止。";}
        Notify();Tick();
      } else if(command=="refresh"&&account.Operation==null&&account.ReadMode!="off") {
        // 429 的冷却不能被连续点击绕过。
        if(DateTime.UtcNow<account.NextRead&&account.QuotaState=="rate-limited")return;
        Read(account);
      }
    }
    static void Cancel(PulseAccountState account) {account.Generation++;account.BorrowedRead=false;var previous=account.Operation;account.Operation=null;if(previous!=null)previous.Cancel();}
    bool Current(PulseAccountState account,int generation,CancellationTokenSource operation) {return !disposed&&account.Generation==generation&&account.Operation==operation&&!operation.IsCancellationRequested;}
    async void Authorize(PulseAccountState account) {
      var operation=new CancellationTokenSource(TimeSpan.FromMinutes(5));account.Operation=operation;int generation=++account.Generation;
      account.AuthState="authorizing";account.QuotaState="authorizing";account.Windows=new PulseQuotaWindow[0];
      account.Notice="请在刚打开的官方页面完成登录；5 分钟内有效，可随时取消。";Notify();
      try {
        var credential=await login(account.Id,http,operation.Token);
        if(!Current(account,generation,operation))return;
        vault.Save(account.Id,credential);account.Credential=credential;SetMode(account,"web");account.AuthState="connected";account.QuotaState="loading";account.QuotaSource="web-login";account.Notice="当前使用你选择的备用网页登录账户，可切回桌面自动读取。";account.NextRead=DateTime.MinValue;
      } catch(Exception error) {
        if(!disposed&&account.Generation==generation&&account.Operation==operation) {
          account.AuthState="error";account.QuotaState="error";
          account.Notice=operation.IsCancellationRequested?"登录已超时，请重新打开官方页面。":error is PulseAccountException&&((PulseAccountException)error).Kind=="scope"?"授权范围不是只读 profile，未保存凭据。":"登录未完成或授权保存失败，请重试；原应用登录不受影响。";
          if(error is PulseAccountException&&((PulseAccountException)error).Kind=="denied")account.Notice="你已在官方页面拒绝授权，未保存新的登录凭据。";
        }
      } finally {
        if(account.Operation==operation)account.Operation=null;operation.Dispose();Notify();
        if(!disposed&&account.Generation==generation&&account.AuthState=="connected")Tick();
      }
    }
    async void Read(PulseAccountState account) {
      if(DateTime.UtcNow<account.RetryAfter)return;
      var operation=new CancellationTokenSource(TimeSpan.FromSeconds(50));account.Operation=operation;int generation=++account.Generation;
      account.BorrowedRead=account.ReadMode=="auto";
      account.NextRead=DateTime.UtcNow.AddSeconds(60);
      if(account.QuotaState!="ready")account.QuotaState="loading";Notify();
      try {
        PulseDesktopReading desktop=account.ReadMode=="auto"?await desktopRead(account.Id,operation.Token):new PulseDesktopReading();
        if(!Current(account,generation,operation)||!openApplications.Contains(account.Id))return;
        PulseQuotaWindow[] windows;DateTime observed=DateTime.UtcNow;
        if(desktop.Source=="desktop-cache") {windows=desktop.Windows;observed=desktop.ObservedAt;account.QuotaSource="desktop-cache";}
        else if(desktop.Source=="desktop-session") {
          object data;
          if(account.Id=="claude")data=await http.ReadClaudeDesktop(desktop.Organization,desktop.SessionCookie,operation.Token);
          else data=await ReadUsage(account.Id,desktop.AccessToken,operation.Token);
          if(!Current(account,generation,operation))return;
          windows=PulseUsageParser.Parse(account.Id,data);account.QuotaSource="desktop-session";
        } else {
        account.BorrowedRead=false;
        // 桌面凭据存在但失效/读取失败时抛错，不悄悄换成可能不同的备用账户。
        var credential=account.Credential;if(credential==null)throw new PulseAccountException("no-source");
        if(credential.ExpiresAt<=DateTime.UtcNow.AddSeconds(30)) {
          if(account.Id!="claude"||String.IsNullOrEmpty(credential.RefreshToken))throw new PulseAccountException("credentials");
          var tokenData=await http.Read("https://platform.claude.com/v1/oauth/token",HttpMethod.Post,new{grant_type="refresh_token",refresh_token=credential.RefreshToken,client_id=PulseAccountLogin.ClaudeClient,scope=PulseAccountLogin.ClaudeScope},null,operation.Token);
          credential=PulseAccountLogin.ClaudeCredential(tokenData,credential.RefreshToken);
          if(!Current(account,generation,operation))return;
          vault.Save(account.Id,credential);account.Credential=credential;
        }
        if(!openApplications.Contains(account.Id))return;
        var data=await ReadUsage(account.Id,credential.AccessToken,operation.Token);
        if(!Current(account,generation,operation))return;
        windows=PulseUsageParser.Parse(account.Id,data);account.QuotaSource="web-login";
        }
        account.Windows=windows;account.AuthState="connected";account.UpdatedAt=observed;account.QuotaState=windows.Length>0?"ready":"unavailable";
        account.Notice=windows.Length==0?"此账户未返回可用的额度窗口；不代表剩余 100%，也不代表已用尽。":account.QuotaSource=="web-login"?"备用网页登录账户，可能与桌面账号不同。":"";
      } catch(Exception error) {
        if(!disposed&&account.Generation==generation&&account.Operation==operation) {
          var known=error as PulseAccountException;account.Windows=new PulseQuotaWindow[0];account.QuotaState="error";
          account.QuotaSource="none";
          if(known!=null&&(known.Kind=="credentials"||known.Kind=="desktop-sign-in")) {account.AuthState="signed-out";account.QuotaState="signed-out";account.Notice=account.ReadMode=="auto"?"桌面登录已过期或接口拒绝访问；请先在原应用刷新登录，或手动选择备用网页登录。":"备用授权已过期或被拒绝；可切回自动读取。";}
          else if(known!=null&&known.Kind=="no-source"){account.AuthState="signed-out";account.QuotaState="unavailable";account.Notice="未找到有效桌面额度来源；请先在原应用登录。独立网页登录仅为备用方式。";}
          else if(known!=null&&known.Kind=="desktop-organization"){account.QuotaState="unavailable";account.Notice="无法确认 Claude 当前组织；请在原应用打开用量页面后重试，不会代选其他组织。";}
          else if(known!=null&&known.Kind=="local-storage"){account.QuotaState="unavailable";account.Notice="本机来源暂不可读或格式不支持，稍后重试；未改动原应用登录状态。";}
          else if(known!=null&&known.Kind=="rate-limit") {account.QuotaState="rate-limited";account.RetryAfter=account.NextRead=DateTime.UtcNow.AddSeconds(known.RetrySeconds);account.Notice="服务暂时限流，稍后自动重试；不会连续发送请求。";}
          else account.Notice="额度读取失败，暂不展示旧读数；稍后自动重试。";
        }
      } finally {if(account.Operation==operation){account.Operation=null;account.BorrowedRead=false;}operation.Dispose();Notify();}
    }
    Task<object> ReadUsage(string id,string token,CancellationToken cancellation) {
      string endpoint=id=="claude"?"https://api.anthropic.com/api/oauth/usage":id=="cursor"?"https://cursor.com/api/usage-summary":"https://cursor.com/api/dashboard/get-sand-usage-status";
      return http.Read(endpoint,id=="grok-bot"?HttpMethod.Post:HttpMethod.Get,id=="grok-bot"?new object():null,token,cancellation);
    }
    public void Dispose() {if(disposed)return;disposed=true;foreach(var account in accounts.Values)Cancel(account);http.Dispose();}
  }
}
