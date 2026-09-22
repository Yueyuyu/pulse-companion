using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexCompanion.PulseWebPreview {
  // 协议来源与许可见 docs/account-authorization.md；只在官方浏览器页面输入登录信息。
  internal static class PulseAccountLogin {
    internal const string ClaudeClient="9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    internal const string ClaudeScope="user:profile";
    internal static string Base64Url(byte[] value) {return Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');}
    internal static string RandomSecret() {var bytes=new byte[32];using(var random=RandomNumberGenerator.Create())random.GetBytes(bytes);return Base64Url(bytes);}
    internal static string Challenge(string verifier) {using(var sha=SHA256.Create())return Base64Url(sha.ComputeHash(Encoding.UTF8.GetBytes(verifier)));}
    internal static Dictionary<string,object> Jwt(string token) {
      try {
        string part=token.Split('.')[1].Replace('-','+').Replace('_','/');part=part.PadRight((part.Length+3)/4*4,'=');
        return PulseUsageParser.Object(new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(Convert.FromBase64String(part))));
      }catch {throw new PulseAccountException("credentials");}
    }
    internal static string CursorCookie(string token) {
      string subject=PulseUsageParser.Text(Jwt(token),"sub");
      if(subject==null)throw new PulseAccountException("credentials");
      string id=subject.Split('|').Last();
      if(id.Length==0||id.Length>200||!id.All(c=>Char.IsLetterOrDigit(c)||c=='_'||c=='-'))throw new PulseAccountException("credentials");
      return "WorkosCursorSessionToken="+Uri.EscapeDataString(id+"::"+token);
    }
    static void Open(Uri url) {
      if(url.Scheme!="https"||(url.Host!="cursor.com"&&url.Host!="claude.com"))throw new PulseAccountException("endpoint");
      Process.Start(new ProcessStartInfo(url.AbsoluteUri){UseShellExecute=true});
    }
    internal static Uri CursorLoginUri(string id,string verifier,string uuid) {
      if(id!="cursor"&&id!="grok-bot")throw new PulseAccountException("endpoint");
      return new Uri("https://cursor.com/loginDeepControl?challenge="+Challenge(verifier)+"&uuid="+Uri.EscapeDataString(uuid)+"&mode=login&supportsSelectedTeamLogin=true"+(id=="grok-bot"?"&redirectTarget=sand":""));
    }
    internal static async Task<PulseCredential> Login(string id,PulseAccountHttp http,CancellationToken cancellation) {
      if(id=="claude")return await Claude(http,cancellation);
      if(id!="cursor"&&id!="grok-bot")throw new PulseAccountException("unsupported");
      string verifier=RandomSecret(),uuid=Guid.NewGuid().ToString();
      Open(CursorLoginUri(id,verifier,uuid));
      while(true) {
        await Task.Delay(2000,cancellation);
        object data;
        try {data=await http.Read("https://api2.cursor.sh/auth/poll?uuid="+uuid+"&verifier="+verifier,HttpMethod.Get,null,null,cancellation);}
        catch(PulseAccountException error) {if(error.Kind!="network")throw;continue;}
        catch(HttpRequestException) {continue;}
        if(data==null)continue;
        string token=PulseUsageParser.Text(data,"accessToken");
        if(!PulseCredential.SafeToken(token))throw new PulseAccountException("response");
        CursorCookie(token);
        object exp=PulseUsageParser.Get(Jwt(token),"exp");DateTime expires;
        try {expires=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(Convert.ToDouble(exp));}
        catch {throw new PulseAccountException("response");}
        if(expires<=DateTime.UtcNow)throw new PulseAccountException("credentials");
        return new PulseCredential{AccessToken=token,ExpiresAt=expires};
      }
    }
    internal static bool CallbackMatches(string target,string state,out string code) {
      code=null;
      Uri uri;if(target.Length>8192||!target.StartsWith("/callback?",StringComparison.Ordinal)||!Uri.TryCreate("http://localhost"+target,UriKind.Absolute,out uri))return false;
      var query=System.Web.HttpUtility.ParseQueryString(uri.Query);
      // 重复参数也拒绝，不能用第一个合法 state 掩盖另一个值。
      var states=query.GetValues("state");var codes=query.GetValues("code");
      if(states==null||states.Length!=1||states[0]!=state||codes==null||codes.Length!=1||!PulseCredential.SafeToken(codes[0]))return false;
      code=codes[0];return true;
    }
    internal static bool CallbackDenied(string target,string state) {
      Uri uri;if(target.Length>8192||!target.StartsWith("/callback?",StringComparison.Ordinal)||!Uri.TryCreate("http://localhost"+target,UriKind.Absolute,out uri))return false;
      var query=System.Web.HttpUtility.ParseQueryString(uri.Query);
      var states=query.GetValues("state");var errors=query.GetValues("error");
      return states!=null&&states.Length==1&&states[0]==state&&errors!=null&&errors.Length==1&&errors[0]=="access_denied"&&query.GetValues("code")==null;
    }
    static async Task<PulseCredential> Claude(PulseAccountHttp http,CancellationToken cancellation) {
      string verifier=RandomSecret(),state=RandomSecret();
      var listener=new TcpListener(IPAddress.Loopback,0);listener.Start(4);
      try {
        int port=((IPEndPoint)listener.LocalEndpoint).Port;
        string redirect="http://localhost:"+port+"/callback";
        using(cancellation.Register(()=>listener.Stop())) {
          Open(new Uri("https://claude.com/cai/oauth/authorize?code=true&response_type=code&client_id="+ClaudeClient+"&redirect_uri="+Uri.EscapeDataString(redirect)+"&scope="+Uri.EscapeDataString(ClaudeScope)+"&code_challenge="+Challenge(verifier)+"&code_challenge_method=S256&state="+state));
          while(true) {
            cancellation.ThrowIfCancellationRequested();
            using(var socket=await listener.AcceptTcpClientAsync())
            using(var connection=CancellationTokenSource.CreateLinkedTokenSource(cancellation)) {
              connection.CancelAfter(TimeSpan.FromSeconds(5));
              using(connection.Token.Register(()=>socket.Close())) {
                try {
                  if(!IPAddress.IsLoopback(((IPEndPoint)socket.Client.RemoteEndPoint).Address))continue;
                  var stream=socket.GetStream();var bytes=new List<byte>();var buffer=new byte[512];
                  string header="";int count;
                  while(bytes.Count<8192&&(count=await stream.ReadAsync(buffer,0,buffer.Length,connection.Token))>0) {
                    bytes.AddRange(buffer.Take(count));header=Encoding.ASCII.GetString(bytes.ToArray());
                    if(header.Contains("\r\n\r\n"))break;
                  }
                  string[] lines=header.Split(new[]{"\r\n"},StringSplitOptions.None),request=lines[0].Split(' ');
                  string code=null;
                  bool envelope=bytes.Count<=8192&&header.Contains("\r\n\r\n")&&request.Length==3&&request[0]=="GET"&&request[2]=="HTTP/1.1"&&
                    lines.Count(line=>line.StartsWith("Host:",StringComparison.OrdinalIgnoreCase))==1&&lines.Any(line=>String.Equals(line,"Host: localhost:"+port,StringComparison.OrdinalIgnoreCase));
                  bool valid=envelope&&CallbackMatches(request[1],state,out code),denied=envelope&&CallbackDenied(request[1],state);
                  byte[] body=Encoding.UTF8.GetBytes(valid?"授权回调已接收，请返回 Pulse Companion 查看结果。":denied?"你已拒绝授权，未保存新的登录凭据。可以关闭本页。":"无效的授权回调。请返回 Companion 重试。");
                  byte[] prefix=Encoding.ASCII.GetBytes("HTTP/1.1 "+(valid?"200 OK":"400 Bad Request")+"\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\nContent-Length: "+body.Length+"\r\n\r\n");
                  await stream.WriteAsync(prefix,0,prefix.Length,connection.Token);await stream.WriteAsync(body,0,body.Length,connection.Token);
                  if(denied)throw new PulseAccountException("denied");
                  if(!valid)continue;
                  cancellation.ThrowIfCancellationRequested();
                  var data=await http.Read("https://platform.claude.com/v1/oauth/token",HttpMethod.Post,new{grant_type="authorization_code",code=code,redirect_uri=redirect,client_id=ClaudeClient,code_verifier=verifier,state=state},null,cancellation);
                  return ClaudeCredential(data,null);
                } catch(IOException) {cancellation.ThrowIfCancellationRequested();}
                catch(ObjectDisposedException) {cancellation.ThrowIfCancellationRequested();}
                catch(OperationCanceledException) {cancellation.ThrowIfCancellationRequested();}
              }
            }
          }
        }
      } finally {listener.Stop();}
    }
    internal static PulseCredential ClaudeCredential(object data,string previousRefresh) {
      var credential=new PulseCredential{AccessToken=PulseUsageParser.Text(data,"access_token"),RefreshToken=PulseUsageParser.Text(data,"refresh_token")??previousRefresh};
      string scope=PulseUsageParser.Text(data,"scope");
      if(scope!=null&&scope.Split(' ').Any(value=>value!=ClaudeScope))throw new PulseAccountException("scope");
      double seconds;try {seconds=Convert.ToDouble(PulseUsageParser.Get(data,"expires_in"));}catch {throw new PulseAccountException("response");}
      if(!credential.Valid||seconds<=0||seconds>31536000)throw new PulseAccountException("response");
      credential.ExpiresAt=DateTime.UtcNow.AddSeconds(seconds);return credential;
    }
  }
}
