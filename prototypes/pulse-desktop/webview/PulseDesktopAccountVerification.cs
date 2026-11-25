using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexCompanion.PulseWebPreview {
  internal static class PulseDesktopAccountVerification {
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static object Json(string text){return new JavaScriptSerializer().DeserializeObject(text);}
    static byte[] Hex(string text){return Enumerable.Range(0,text.Length/2).Select(i=>Convert.ToByte(text.Substring(i*2,2),16)).ToArray();}
    static string Snapshot(PulseAccountService service){return new JavaScriptSerializer().Serialize(service.Snapshots(new PulseAppearanceSettings()));}
    internal static void Run(string root,List<string> checks) {
      var key=new byte[32];var nonce=new byte[12];var tag=Hex("d0d1c8a799996bf0265b98b5d48ab919");var cipher=Hex("cea7403d4d606b6e074ec5d3baf39d18");
      Require(PulseWindowsGcm.Open(key,nonce,cipher,tag).SequenceEqual(new byte[16]),"系统 AES-256-GCM 标准向量");
      tag[0]^=1;bool rejected=false;try{PulseWindowsGcm.Open(key,nonce,cipher,tag);}catch(PulseAccountException){rejected=true;}
      Require(rejected,"GCM 损坏认证标签拒绝解密");
      Require(PulseDesktopStorage.Text(Encoding.Unicode.GetBytes("fixture"))=="fixture","Cursor UTF-16 blob");
      Require(PulseDesktopAccounts.Organization("https://evil.invalid")==null,"组织不是 URL");
      foreach(var endpoint in new[]{"https://claude.ai/api/bootstrap","https://claude.ai/api/organizations/not-a-uuid/usage","https://claude.ai.evil.test/api/organizations/11111111-1111-4111-8111-111111111111/usage"})Require(!PulseAccountHttp.Allowed(new Uri(endpoint),HttpMethod.Get),"桌面只允许组织额度路径");
      checks.Add("desktop-storage-gcm-authentication-utf16-exact-quota-endpoint");
      string org="11111111-1111-4111-8111-111111111111";long now=(DateTime.UtcNow.Ticks-new DateTime(1970,1,1).Ticks)/10000;
      Func<long,string,object> cache=(stamp,organization)=>Json("{\"version\":2,\"samples\":[{\"t\":"+stamp+",\"org\":\""+organization+"\",\"u\":{\"five_hour\":{\"utilization\":31}}}]}");
      Require(PulseDesktopAccounts.ClaudeCache(cache(now,org),org,DateTime.UtcNow).Windows[0].remaining==69,"当前组织有效缓存优先");
      Require(PulseDesktopAccounts.ClaudeCache(cache(now-121000,org),org,DateTime.UtcNow)==null,"陈旧缓存不伪装实时");
      Require(PulseDesktopAccounts.ClaudeCache(cache(now+60000,org),org,DateTime.UtcNow)==null,"未来时间不可信");
      Require(PulseDesktopAccounts.ClaudeCache(cache(now,org),"22222222-2222-4222-8222-222222222222",DateTime.UtcNow)==null,"不跨组织复用缓存");
      Require(PulseDesktopAccounts.ClaudeCache(Json("{\"version\":2,\"samples\":[]}"),org,DateTime.UtcNow)==null,"空缓存不能生成 100%");
      var record=new Dictionary<string,object>{{"cursor-access-token","legacy-must-not-use"},{"cursor-accounts","{\"active\":null,\"accounts\":{}}"}};
      Require(PulseDesktopAccounts.GrokStoredAccess(record)==null,"Grok 当前退出不拿旧账号令牌顶替");
      checks.Add("desktop-cache-freshness-empty-current-organization-grok-active-account");
      Task.Run(()=>ServiceCases(root)).GetAwaiter().GetResult();
      checks.Add("desktop-source-priority-no-vault-no-token-dto-stop-restart-resume");
      checks.Add("desktop-close-during-discovery-cancel-late-result-no-query-no-fallback-on-error");
    }
    sealed class Handler:HttpMessageHandler {
      internal int Calls;internal Func<HttpRequestMessage,HttpResponseMessage> Reply;
      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;return Task.FromResult(Reply(request));}
    }
    static async Task WaitIdle(PulseAccountService service) {
      // fixture 的 Task.FromResult 同步完成；仅延迟案例有等待，最久两秒。
      for(int i=0;i<200;i++){if(!Snapshot(service).Contains("\"quotaState\":\"loading\""))return;await Task.Delay(10);}
      throw new Exception("自动读取 fixture 未结束");
    }
    static async Task ServiceCases(string root) {
      string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
      foreach(bool overflow in new[]{false,true}) {
        bool stopped=false;var elapsed=System.Diagnostics.Stopwatch.StartNew();
        try{await PulseAntigravityCli.Run(powershell,"-NoProfile -NonInteractive -Command \""+(overflow?"Write-Output ('x' * 10000)":"Start-Sleep -Seconds 30")+"\"",root,32,TimeSpan.FromSeconds(2),CancellationToken.None);}
        catch(PulseAccountException error){stopped=error.Kind==(overflow?"response":"cli-unavailable");}
        Require(stopped&&elapsed.Elapsed<TimeSpan.FromSeconds(10),"CLI 超量/超时终止自有子进程，不挂住后台");
      }
      using(var canceled=new CancellationTokenSource()) {
        canceled.Cancel();bool rejected=false;
        try{await PulseAntigravityCli.Run("must-not-start.exe","",root,32,TimeSpan.FromSeconds(2),canceled.Token);}catch(OperationCanceledException){rejected=true;}
        Require(rejected,"停止读取后不再启动 CLI");
      }
      foreach(string source in new[]{"desktop-local","official-cli"}) {
        string localDirectory=Path.Combine(root,"antigravity-"+source);int localReads=0,logins=0;
        Func<string,CancellationToken,Task<PulseDesktopReading>> readLocal=(id,token)=>{localReads++;return Task.FromResult(new PulseDesktopReading{Source=source,ObservedAt=DateTime.UtcNow,Windows=new[]{new PulseQuotaWindow{label="Gemini 周剩余",remaining=75}}});};
        Func<string,PulseAccountHttp,CancellationToken,Task<PulseCredential>> login=(id,http,token)=>{logins++;throw new Exception("不得为 Antigravity 发起网页登录");};
        using(var service=new PulseAccountService(true,localDirectory,null,login,readLocal)) {
          service.SetOpenApplications(new[]{"antigravity"});service.Tick();await WaitIdle(service);
          string state=Snapshot(service);Require(state.Contains("\"remaining\":75")&&state.Contains(source)&&state.Contains("\"canAuthorize\":false")&&state.Contains("\"tasksState\":\"unsupported\""),"Antigravity 真实来源 DTO，不冒充任务接通");
          service.Command("antigravity","authorize");Require(logins==0,"本机读取与网页登录能力分离");
          service.SetOpenApplications(new string[0]);service.Command("antigravity","refresh");Require(localReads==1&&!service.Snapshots(new PulseAppearanceSettings()).Any(),"应用关闭隐藏并停止查询");
          service.SetOpenApplications(new[]{"antigravity"});service.Tick();await WaitIdle(service);Require(localReads==2,"重开恢复读取");
          service.Command("antigravity","disconnect-account");service.Command("antigravity","refresh");Require(localReads==2,"停止后不刷新");
        }
        using(var service=new PulseAccountService(true,localDirectory,null,login,readLocal)) {
          service.SetOpenApplications(new[]{"antigravity"});service.Tick();Require(localReads==2,"Antigravity 停止持久化");
          service.Command("antigravity","resume-auto");await WaitIdle(service);Require(localReads==3,"Antigravity 恢复自动读取");
        }
        Require(!Directory.GetFiles(localDirectory,"*.dpapi").Any(),"Antigravity 不保存凭据");
      }
      string directory=Path.Combine(root,"desktop-source");int reads=0;
      var result=new PulseDesktopReading{Source="desktop-cache",ObservedAt=DateTime.UtcNow,Windows=new[]{new PulseQuotaWindow{label="周剩余",remaining=69}}};
      Func<string,CancellationToken,Task<PulseDesktopReading>> local=(id,token)=>{reads++;return Task.FromResult(result);};
      var handler=new Handler{Reply=request=>{throw new Exception("缓存命中不得联网");}};
      var vault=new PulseCredentialVault(directory);vault.Save("claude",new PulseCredential{AccessToken="fixture-own",ExpiresAt=DateTime.UtcNow.AddHours(1)});
      byte[] saved=File.ReadAllBytes(Path.Combine(directory,"claude.dpapi"));
      using(var http=new PulseAccountHttp(handler))using(var service=new PulseAccountService(true,directory,http,null,local)) {
        service.Tick();Require(reads==0,"未打开不接触凭据");
        service.SetOpenApplications(new[]{"claude"});service.Tick();await WaitIdle(service);
        Require(handler.Calls==0&&Snapshot(service).Contains("desktop-cache"),"原生缓存优先于备用账号");
        service.Command("claude","disconnect-account");int stopped=reads;service.Tick();service.Command("claude","refresh");
        Require(reads==stopped&&Snapshot(service).Contains("\"readMode\":\"off\""),"停止后刷新不自动重连");
      }
      using(var http=new PulseAccountHttp(handler))using(var service=new PulseAccountService(true,directory,http,null,local)) {
        service.SetOpenApplications(new[]{"claude"});int stopped=reads;service.Tick();Require(reads==stopped,"停止状态跨重启");
        service.Command("claude","resume-auto");await WaitIdle(service);Require(reads==stopped+1,"用户恢复才读取");
      }
      Require(vault.Load("claude")==null,"停止清除自有授权，自动读取不重建凭据");
      // 借来的 session 只允许去 Claude 精确的 GET usage，不做令牌刷新。
      directory=Path.Combine(root,"borrowed-session");
      handler=new Handler{Reply=request=>{
        Require(request.RequestUri.Host=="claude.ai"&&request.RequestUri.AbsolutePath.EndsWith("/usage")&&request.Method==HttpMethod.Get,"借用令牌严格匹配官方额度请求");
        Require(request.Headers.GetValues("Cookie").Single()=="sessionKey=fixture-borrowed","仅指定应用会话");
        return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"five_hour\":{\"utilization\":31}}")};}};
      local=(id,token)=>Task.FromResult(new PulseDesktopReading{Source="desktop-session",Organization="11111111-1111-4111-8111-111111111111",SessionCookie="sessionKey=fixture-borrowed"});
      using(var http=new PulseAccountHttp(handler))using(var service=new PulseAccountService(true,directory,http,null,local)) {
        service.SetOpenApplications(new[]{"claude"});service.Tick();await WaitIdle(service);
        string state=Snapshot(service);Require(state.Contains("desktop-session")&&state.Contains("\"remaining\":69")&&!state.Contains("fixture-borrowed")&&!Directory.Exists(directory),"借用会话不落盘、不进 DTO");
      }
      // 原应用拒绝访问，不使用保存的另一个账户掩盖失败。
      directory=Path.Combine(root,"desktop-rejected");vault=new PulseCredentialVault(directory);vault.Save("claude",new PulseCredential{AccessToken="fixture-own",RefreshToken="must-not-refresh",ExpiresAt=DateTime.UtcNow.AddHours(1)});
      saved=File.ReadAllBytes(Path.Combine(directory,"claude.dpapi"));
      handler=new Handler{Reply=request=>new HttpResponseMessage(HttpStatusCode.Unauthorized)};
      using(var http=new PulseAccountHttp(handler))using(var service=new PulseAccountService(true,directory,http,null,local)) {
        service.SetOpenApplications(new[]{"claude"});service.Tick();await WaitIdle(service);
        Require(handler.Calls==1&&Snapshot(service).Contains("\"remaining\":null")&&saved.SequenceEqual(File.ReadAllBytes(Path.Combine(directory,"claude.dpapi"))),"401 不跨账户回落或改写凭据");
      }
      foreach(bool stop in new[]{false,true}) {
        var delayed=new TaskCompletionSource<PulseDesktopReading>();handler=new Handler{Reply=request=>{throw new Exception("关闭或停止后不得查询");}};
        using(var http=new PulseAccountHttp(handler))using(var service=new PulseAccountService(true,Path.Combine(root,"late-desktop-"+stop),http,null,(id,token)=>delayed.Task)) {
          service.SetOpenApplications(new[]{"claude"});service.Tick();
          if(stop)service.Command("claude","disconnect-account");else service.SetOpenApplications(new string[0]);
          var finished=new TaskCompletionSource<bool>();service.Changed+=()=>finished.TrySetResult(true);
          delayed.SetResult(await local("claude",CancellationToken.None));
          Require(await Task.WhenAny(finished.Task,Task.Delay(2000))==finished.Task&&handler.Calls==0,"关闭/停止期间的发现结果不触发请求");
        }
      }
    }
  }
}
