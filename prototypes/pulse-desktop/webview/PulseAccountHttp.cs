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
  internal sealed class PulseAccountException : Exception {
    internal readonly string Kind;
    internal readonly int RetrySeconds;
    internal PulseAccountException(string kind,int retrySeconds=60):base("账户操作未完成") {Kind=kind;RetrySeconds=retrySeconds;}
  }
  internal sealed class PulseAccountHttp : IDisposable {
    readonly HttpClient client;
    internal PulseAccountHttp():this(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}) {}
    internal PulseAccountHttp(HttpMessageHandler handler) {
      ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
      client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(20)};
    }
    internal static bool Allowed(Uri uri,HttpMethod method) {
      if(uri.Scheme!="https"||uri.Port!=443||uri.UserInfo!=""||uri.Fragment!="")return false;
      if(uri.Host=="api2.cursor.sh"&&uri.AbsolutePath=="/auth/poll"&&method==HttpMethod.Get)return true;
      if(uri.Query!="")return false;
      return (uri.Host=="cursor.com"&&uri.AbsolutePath=="/api/usage-summary"&&method==HttpMethod.Get)||
        (uri.Host=="claude.ai"&&System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath,"^/api/organizations/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/usage$")&&method==HttpMethod.Get)||
        (uri.Host=="cursor.com"&&uri.AbsolutePath=="/api/dashboard/get-sand-usage-status"&&method==HttpMethod.Post)||
        (uri.Host=="platform.claude.com"&&uri.AbsolutePath=="/v1/oauth/token"&&method==HttpMethod.Post)||
        (uri.Host=="api.anthropic.com"&&uri.AbsolutePath=="/api/oauth/usage"&&method==HttpMethod.Get);
    }
    internal Task<object> Read(string endpoint,HttpMethod method,object body,string accessToken,CancellationToken cancellation) {
      return Send(endpoint,method,body,accessToken,null,cancellation);
    }
    internal Task<object> ReadClaudeDesktop(string organization,string cookie,CancellationToken cancellation) {
      if(PulseDesktopAccounts.Organization(organization)!=organization||String.IsNullOrEmpty(cookie)||cookie.Length>32768||
        cookie.Split(new[]{"; "},StringSplitOptions.None).Any(part=>!(part.StartsWith("sessionKey=")||part.StartsWith("sessionKeyV3="))||!PulseCredential.SafeToken(part.Substring(part.IndexOf('=')+1))||part.Contains(",")||part.Contains(";")))throw new PulseAccountException("credentials");
      return Send("https://claude.ai/api/organizations/"+organization+"/usage",HttpMethod.Get,null,null,cookie,cancellation);
    }
    async Task<object> Send(string endpoint,HttpMethod method,object body,string accessToken,string desktopCookie,CancellationToken cancellation) {
      var uri=new Uri(endpoint);
      if(!Allowed(uri,method))throw new PulseAccountException("endpoint");
      using(var request=new HttpRequestMessage(method,uri)) {
        request.Headers.Accept.ParseAdd("application/json");
        if(desktopCookie!=null) {
          if(uri.Host!="claude.ai"||accessToken!=null||method!=HttpMethod.Get)throw new PulseAccountException("endpoint");
          request.Headers.Add("Cookie",desktopCookie);
        }
        if(body!=null)request.Content=new StringContent(new JavaScriptSerializer().Serialize(body),Encoding.UTF8,"application/json");
        if(accessToken!=null) {
          if(!PulseCredential.SafeToken(accessToken))throw new PulseAccountException("credentials");
          if(uri.Host=="cursor.com") {
            request.Headers.Add("Cookie",PulseAccountLogin.CursorCookie(accessToken));
            request.Headers.Add("Origin","https://cursor.com");
          } else if(uri.Host=="api.anthropic.com") {
            request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",accessToken);
            request.Headers.Add("anthropic-beta","oauth-2025-04-20");
            request.Headers.UserAgent.ParseAdd("claude-cli (external, cli)");
          } else throw new PulseAccountException("endpoint");
        }
        using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation)) {
          int status=(int)response.StatusCode;
          if(status==404&&uri.Host=="api2.cursor.sh")return null;
          if(status==429) {
            int wait=60;
            if(response.Headers.RetryAfter!=null) {
              var retry=response.Headers.RetryAfter;
              double seconds=retry.Delta.HasValue?retry.Delta.Value.TotalSeconds:retry.Date.HasValue?(retry.Date.Value-DateTimeOffset.UtcNow).TotalSeconds:60;
              wait=(int)Math.Max(60,Math.Min(3600,seconds));
            }
            throw new PulseAccountException("rate-limit",wait);
          }
          if(status==401||status==403||(status==400&&uri.Host=="platform.claude.com"))throw new PulseAccountException("credentials");
          // 包括所有跳转：绝不把 Cookie / Bearer / PKCE verifier 转发到 Location。
          if(status>=300)throw new PulseAccountException("network");
          if(response.Content.Headers.ContentLength>1048576)throw new PulseAccountException("response");
          using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation)) {
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            using(var stream=await response.Content.ReadAsStreamAsync())
            using(var memory=new MemoryStream()) {
              var buffer=new byte[8192];int count;
              while((count=await stream.ReadAsync(buffer,0,buffer.Length,deadline.Token))>0) {
                if(memory.Length+count>1048576)throw new PulseAccountException("response");
                memory.Write(buffer,0,count);
              }
              try {return new JavaScriptSerializer{MaxJsonLength=1048576}.DeserializeObject(Encoding.UTF8.GetString(memory.ToArray()));}
              catch {throw new PulseAccountException("response");}
            }
          }
        }
      }
    }
    public void Dispose() {client.Dispose();}
  }
}
