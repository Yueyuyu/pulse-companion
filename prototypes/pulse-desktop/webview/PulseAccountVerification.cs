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
  internal static class PulseAccountVerification {
    static void Require(bool value,string label) {if(!value)throw new Exception(label);}
    static object Json(string value) {return new JavaScriptSerializer().DeserializeObject(value);}
    static PulseQuotaWindow[] Parse(string id,string value) {return PulseUsageParser.Parse(id,Json(value));}
    internal static void Run(string root,List<string> checks) {
      Require(PulseApplicationPresence.Select(true,new[]{"ChatGPT"}).SequenceEqual(new[]{"codex"}),"只打开 Codex 只显示 Codex");
      Require(PulseApplicationPresence.Select(true,new[]{"ChatGPT","CURSOR","Cursor","WorkBuddy"}).SequenceEqual(new[]{"codex","cursor","workbuddy"}),"多应用顺序稳定、去重且名称不区分大小写");
      Require(PulseApplicationPresence.Select(false,new[]{"Codex","Cursor","Claude"}).SequenceEqual(new[]{"cursor","claude"}),"CLI 不冒充桌面应用");
      Require(PulseApplicationPresence.Select(true,new string[0]).Length==0,"无桌面窗口的后台进程不显示");
      checks.Add("application-presence-per-app-window-whitelist-cli-dedup-order");
      Require(PulseAccountLogin.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")=="E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM","PKCE RFC 7636 向量");
      string code;
      Require(PulseAccountLogin.CallbackMatches("/callback?code=fixture&state=nonce","nonce",out code)&&code=="fixture","正确回调");
      Require(PulseAccountLogin.CallbackDenied("/callback?error=access_denied&state=nonce","nonce")&&!PulseAccountLogin.CallbackDenied("/callback?error=access_denied&state=bad","nonce"),"官方拒绝授权回调也校验 state");
      foreach(var target in new[]{"/callback?code=fixture&state=bad","/callback?code=fixture&state=nonce&state=bad","/other?code=fixture&state=nonce","//evil.test/callback?code=fixture&state=nonce","/callback?code=%0d%0a&state=nonce","/callback?error=denied&state=nonce"})Require(!PulseAccountLogin.CallbackMatches(target,"nonce",out code),"拒绝伪造回调");
      Require(!PulseAccountLogin.CursorLoginUri("cursor","fixture","id").Query.Contains("redirectTarget")&&PulseAccountLogin.CursorLoginUri("grok-bot","fixture","id").Query.Contains("redirectTarget=sand"),"Cursor 与 Grok Bot 登录身份分离");
      Require(PulseAccountLogin.ClaudeScope=="user:profile","Claude 最小 scope");
      foreach(string uri in new[]{"https://evil.test/api/usage-summary","http://cursor.com/api/usage-summary","https://cursor.com:444/api/usage-summary","https://cursor.com/api/usage-summary?redirect=evil","https://user@cursor.com/api/usage-summary"})Require(!PulseAccountHttp.Allowed(new Uri(uri),HttpMethod.Get),"端点白名单");
      Require(!PulseAccountHttp.Allowed(new Uri("https://cursor.com/api/usage-summary"),HttpMethod.Post),"方法白名单");
      checks.Add("accounts-pkce-state-callback-provider-scope-endpoint-whitelist");

      var vault=new PulseCredentialVault(Path.Combine(root,"accounts"));
      var credential=new PulseCredential{AccessToken="fixture-access",RefreshToken="fixture-refresh",ExpiresAt=DateTime.UtcNow.AddHours(1)};
      vault.Save("claude",credential);Require(vault.Load("claude").AccessToken==credential.AccessToken,"DPAPI 重启读取");
      string path=Path.Combine(root,"accounts","claude.dpapi");
      Require(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("fixture-access"),"磁盘不保存明文 Token");
      File.WriteAllText(path,"corrupt-fixture");bool rejected=false;
      try{vault.Save("claude",credential);}catch{rejected=true;}
      Require(rejected&&File.ReadAllText(path)=="corrupt-fixture","损坏凭据不得覆盖");
      vault.Delete("claude");Require(vault.Load("claude")==null,"仅删除本应用授权");
      checks.Add("accounts-dpapi-roundtrip-no-plaintext-corrupt-preserved-disconnect");

      Require(Parse("cursor","{}").Length==0,"缺失不是 100%");
      var pools=Parse("cursor","{\"individualUsage\":{\"plan\":{\"autoPercentUsed\":0.0267,\"apiPercentUsed\":69}}}");
      Require(pools.Length==2&&pools[0].remaining==99.97&&pools[1].remaining==31,"Cursor 独立月池百分比单位");
      Require(Parse("cursor","{\"individualUsage\":{\"plan\":{\"used\":0,\"limit\":0}}}").Length==0,"零分母不可用");
      Require(Parse("grok-bot","{\"usagePercent\":0}").Length==0,"Grok 缺少订阅标志不可用");
      Require(Parse("grok-bot","{\"hasNonZeroIncludedLimit\":true,\"includedLimitZero\":true,\"usagePercent\":0}").Length==0,"Grok 零包含额度不可用");
      Require(Parse("grok-bot","{\"hasNonZeroIncludedLimit\":true,\"usagePercent\":31}")[0].remaining==69,"Grok 明确订阅独立周额度");
      Require(Parse("claude","{\"limits\":[{\"kind\":\"session\",\"percent\":76,\"severity\":\"warning\"},{\"kind\":\"weekly_scoped\",\"percent\":10,\"scope\":{\"model\":{\"display_name\":\"fixture-model\"}}}]}").Length==2,"Claude 警告不是用尽，保留模型窗口");
      Require(Parse("claude","{\"limits\":[{\"kind\":\"session\",\"percent\":1,\"locked_reason\":\"blocked\"},{\"kind\":\"weekly_all\",\"percent\":1}]}").Length==0,"锁定不伪装健康");
      Require(Parse("claude","{\"five_hour\":{\"utilization\":10,\"resets_at\":\"2000-01-01T00:00:00Z\"}}").Length==0,"已重置窗口不得展示旧读数");
      checks.Add("accounts-provider-quota-units-windows-missing-expired-blocked");
      Task.Run(()=>NetworkCases(root)).GetAwaiter().GetResult();
      checks.Add("application-presence-close-hide-pause-poll-keep-credentials-reopen-refresh");
      checks.Add("accounts-http-redirect-429-cancel-late-refresh-no-save-disabled-verification");
    }
    sealed class FixtureHandler : HttpMessageHandler {
      internal Func<HttpRequestMessage,Task<HttpResponseMessage>> Reply;
      internal int Calls;
      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation) {Calls++;return Reply(request);}
    }
    static async Task NetworkCases(string root) {
      var redirect=new FixtureHandler {Reply=request=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect){Headers={Location=new Uri("https://outside.invalid")}})};
      using(var http=new PulseAccountHttp(redirect)) {
        bool rejected=false;try{await http.Read("https://cursor.com/api/usage-summary",HttpMethod.Get,null,null,CancellationToken.None);}catch(PulseAccountException){rejected=true;}
        Require(rejected&&redirect.Calls==1,"不跟随凭据请求重定向");
      }
      var limited=new FixtureHandler {Reply=request=>{var response=new HttpResponseMessage((HttpStatusCode)429);response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(180));return Task.FromResult(response);}};
      using(var http=new PulseAccountHttp(limited)) {
        bool rejected=false;try{await http.Read("https://cursor.com/api/usage-summary",HttpMethod.Get,null,null,CancellationToken.None);}catch(PulseAccountException error){rejected=error.Kind=="rate-limit"&&error.RetrySeconds==180;}
        Require(rejected,"429 Retry-After");
        string rateDirectory=Path.Combine(root,"rate-account");
        new PulseCredentialVault(rateDirectory).Save("claude",new PulseCredential{AccessToken="fixture-access",ExpiresAt=DateTime.UtcNow.AddHours(1)});
        using(var service=new PulseAccountService(true,rateDirectory,http,null,PulseDesktopAccounts.None)) {
          service.SetOpenApplications(new[]{"claude"});
          service.Tick();int requests=limited.Calls;
          service.Command("claude","refresh");service.Command("claude","refresh");service.Tick();
          Require(limited.Calls==requests,"刷新点击不得绕过 429 冷却");
        }
      }
      var late=new TaskCompletionSource<HttpResponseMessage>();
      var handler=new FixtureHandler{Reply=request=>late.Task};
      string directory=Path.Combine(root,"late-account");var vault=new PulseCredentialVault(directory);
      vault.Save("claude",new PulseCredential{AccessToken="fixture-old",RefreshToken="fixture-refresh",ExpiresAt=DateTime.UtcNow.AddMinutes(-1)});
      using(var http=new PulseAccountHttp(handler))
      using(var service=new PulseAccountService(true,directory,http,null,PulseDesktopAccounts.None)) {
        service.SetOpenApplications(new[]{"claude"});
        service.Tick();Require(handler.Calls==1,"过期凭据只读刷新已开始");
        service.Command("claude","disconnect-account");
        var finished=new TaskCompletionSource<bool>();service.Changed+=()=>finished.TrySetResult(true);
        late.SetResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"access_token\":\"fixture-late\",\"refresh_token\":\"fixture-new\",\"expires_in\":3600,\"scope\":\"user:profile\"}")});
        Require(await Task.WhenAny(finished.Task,Task.Delay(2000))==finished.Task,"迟到刷新已返回");
        Require(vault.Load("claude")==null,"断开后迟到刷新结果不能写盘");
        string snapshot=new JavaScriptSerializer().Serialize(service.Snapshots(new PulseAppearanceSettings()));
        Require(!snapshot.Contains("fixture-")&&!snapshot.Contains("access_token")&&!snapshot.Contains("refresh_token"),"WebView DTO 不含凭据");
      }
      foreach(string cancel in new[]{"cancel-authorization","disconnect-account"}) {
        string authDirectory=Path.Combine(root,cancel);var result=new TaskCompletionSource<PulseCredential>();
        using(var service=new PulseAccountService(true,authDirectory,null,(id,http,token)=>result.Task,PulseDesktopAccounts.None)) {
          service.SetOpenApplications(new[]{"cursor"});
          service.Command("cursor","authorize");service.Command("cursor",cancel);
          var finished=new TaskCompletionSource<bool>();service.Changed+=()=>finished.TrySetResult(true);
          result.SetResult(new PulseCredential{AccessToken="fixture-late-login",ExpiresAt=DateTime.UtcNow.AddHours(1)});
          Require(await Task.WhenAny(finished.Task,Task.Delay(2000))==finished.Task,"迟到登录已返回");
          Require(new PulseCredentialVault(authDirectory).Load("cursor")==null,"取消或断开后的登录结果不落盘");
        }
      }
      var disabledHandler=new FixtureHandler{Reply=request=>{throw new Exception("验证模式不能联网");}};
      using(var http=new PulseAccountHttp(disabledHandler))using(var service=new PulseAccountService(false,Path.Combine(root,"disabled"),http,null,PulseDesktopAccounts.None)) {
        service.SetOpenApplications(new[]{"claude"});
        service.Command("claude","authorize");service.Tick();Require(disabledHandler.Calls==0,"验证禁用授权");
      }
      string presenceDirectory=Path.Combine(root,"presence-accounts");var presenceVault=new PulseCredentialVault(presenceDirectory);
      presenceVault.Save("claude",new PulseCredential{AccessToken="fixture-preserved",ExpiresAt=DateTime.UtcNow.AddHours(1)});
      var presenceHandler=new FixtureHandler {Reply=request=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"five_hour\":{\"utilization\":31}}")})};
      using(var http=new PulseAccountHttp(presenceHandler))using(var service=new PulseAccountService(true,presenceDirectory,http,null,PulseDesktopAccounts.None)) {
        service.Tick();Require(presenceHandler.Calls==0&&!service.Snapshots(new PulseAppearanceSettings()).Any(),"关闭应用不查询、不发条目");
        service.SetOpenApplications(new[]{"claude","kimi"});service.Tick();
        Require(presenceHandler.Calls==1&&service.Snapshots(new PulseAppearanceSettings()).Count()==2,"仅已打开的独立应用条目");
        service.SetOpenApplications(new string[0]);service.Tick();service.Command("claude","refresh");
        Require(presenceHandler.Calls==1&&!service.Snapshots(new PulseAppearanceSettings()).Any(),"关闭后停止新的查询并隐藏条目");
        Require(presenceVault.Load("claude").AccessToken=="fixture-preserved","关闭应用保留授权");
        service.SetOpenApplications(new[]{"claude"});service.Tick();
        Require(presenceHandler.Calls==2&&service.Snapshots(new PulseAppearanceSettings()).Count()==1,"重开应用刷新并恢复条目");
      }
    }
  }
}
