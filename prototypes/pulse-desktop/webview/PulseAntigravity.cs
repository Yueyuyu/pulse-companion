// 协议参考 Pulse e5b7d921（Apache-2.0）；Windows 进程/回环发现为本项目适配。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexCompanion.PulseWebPreview {
  // 只读当前用户、当前会话内 Antigravity 自带的语言服务；不读取账户库或刷新令牌。
  internal static class PulseAntigravity {
    internal const string QuotaPath="/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary";
    const string TokenVariable="ANTIGRAVITY_SERVER_CSRF_TOKEN";
    internal static bool ValidToken(string value) {return value!=null&&Regex.IsMatch(value,"^[A-Za-z0-9_-]{16,256}$");}
    internal static string EnvironmentToken(string block) {
      var values=block.Split('\0').Where(value=>value.StartsWith(TokenVariable+"=",StringComparison.Ordinal)).ToArray();
      if(values.Length!=1)return null;
      string token=values[0].Substring(TokenVariable.Length+1);return ValidToken(token)?token:null;
    }
    internal static bool ServerPath(string server,string app) {
      if(String.IsNullOrEmpty(server)||String.IsNullOrEmpty(app)||!String.Equals(Path.GetFileName(app),"Antigravity.exe",StringComparison.OrdinalIgnoreCase))return false;
      string root=Path.GetDirectoryName(Path.GetFullPath(app));
      return new[]{"resources/bin/language_server.exe","resources/app/extensions/antigravity/bin/language_server_windows_x64.exe"}
        .Any(relative=>String.Equals(Path.GetFullPath(server),Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)),StringComparison.OrdinalIgnoreCase));
    }
    internal static bool AllowedLocal(Uri uri) {
      return uri.Scheme=="http"&&uri.Host=="127.0.0.1"&&uri.Port>0&&uri.UserInfo==""&&uri.Query==""&&uri.Fragment==""&&uri.AbsolutePath==QuotaPath;
    }
    internal static async Task<PulseDesktopReading> Read(CancellationToken cancellation) {
      int session;using(var current=Process.GetCurrentProcess())session=current.SessionId;
      var apps=new List<string>();
      foreach(var app in Process.GetProcessesByName("Antigravity"))using(app)try {
        if(app.SessionId==session&&SameUser(app.Handle))apps.Add(app.MainModule.FileName);
      }catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
      if(apps.Count==0)throw new PulseAccountException("local-service");
      bool answered=false;
      using(var handler=new HttpClientHandler{UseProxy=false,UseCookies=false,AllowAutoRedirect=false})
      using(var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(3)}) {
        foreach(string name in new[]{"language_server","language_server_windows_x64"})
        foreach(var process in Process.GetProcessesByName(name))using(process) {
          cancellation.ThrowIfCancellationRequested();
          IntPtr handle=IntPtr.Zero;
          try {
            if(process.SessionId!=session||!apps.Any(app=>ServerPath(process.MainModule.FileName,app)))continue;
            handle=OpenProcess(0x410,false,process.Id); // QUERY_INFORMATION | VM_READ，不申请写入/注入权限。
            if(handle==IntPtr.Zero||!SameUser(handle))continue;
            string token=ReadToken(handle);if(token==null)continue;
            foreach(int port in ListeningPorts(process.Id).Take(16)) {
              cancellation.ThrowIfCancellationRequested();
              var uri=new Uri("http://127.0.0.1:"+port+QuotaPath);
              // 发送前复核监听者；不扫描任意端口，不向代理、远端或其他应用发送临时 CSRF。
              if(!AllowedLocal(uri)||process.HasExited||!ListeningPorts(process.Id).Contains(port))continue;
              try {
                using(var request=new HttpRequestMessage(HttpMethod.Post,uri)) {
                  request.Headers.Add("x-codeium-csrf-token",token);
                  request.Content=new StringContent("{}",Encoding.UTF8,"application/json");
                  using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation)) {
                    if((int)response.StatusCode==429)throw new PulseAccountException("rate-limit",180);
                    if(!response.IsSuccessStatusCode)continue; // 错端口、401、重定向均不跟随。
                    if(response.Content.Headers.ContentLength>1048576)continue;
                    using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation)) {
                      deadline.CancelAfter(TimeSpan.FromSeconds(3));
                      using(var stream=await response.Content.ReadAsStreamAsync())using(var buffer=new MemoryStream()) {
                        byte[] bytes=new byte[4096];int count;
                        while((count=await stream.ReadAsync(bytes,0,bytes.Length,deadline.Token))>0) {
                          if(buffer.Length+count>1048576)throw new PulseAccountException("response");
                          buffer.Write(bytes,0,count);
                        }
                        var data=new JavaScriptSerializer{MaxJsonLength=1048576}.DeserializeObject(Encoding.UTF8.GetString(buffer.ToArray()));
                        var windows=Parse(data);answered=true;
                        if(windows.Length>0)return new PulseDesktopReading{Source="desktop-local",Windows=windows,ObservedAt=DateTime.UtcNow};
                      }
                    }
                  }
                }
              }catch(HttpRequestException){}catch(OperationCanceledException){cancellation.ThrowIfCancellationRequested();}
              catch(ArgumentException){}catch(InvalidOperationException){}
            }
          }catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
          finally{if(handle!=IntPtr.Zero)CloseHandle(handle);}
        }
      }
      if(answered)return new PulseDesktopReading{Source="desktop-local",ObservedAt=DateTime.UtcNow};
      // 部分 Windows 实例的启动环境已无 CSRF。委托官方 CLI，不从堆内存找密钥。
      return await PulseAntigravityCli.Read(cancellation);
    }
    internal static PulseQuotaWindow[] Parse(object data) {
      var groups=PulseUsageParser.Get(PulseUsageParser.Get(data,"response"),"groups") as object[];
      var result=new List<PulseQuotaWindow>();
      foreach(var group in groups??new object[0]) {
        string name=PulseUsageParser.Text(group,"displayName");
        if(String.IsNullOrWhiteSpace(name)||name.Any(Char.IsControl))continue;
        name=Regex.Replace(name.Trim()," Models$","",RegexOptions.IgnoreCase);
        name=name.Substring(0,Math.Min(name.Length,32));
        var buckets=PulseUsageParser.Get(group,"buckets") as object[];
        foreach(var bucket in (buckets??new object[0]).OrderBy(value=>WindowSeconds(PulseUsageParser.Text(value,"window")))) {
          string window=PulseUsageParser.Text(bucket,"window");int seconds=WindowSeconds(window);
          if(seconds==0||String.IsNullOrEmpty(PulseUsageParser.Text(bucket,"bucketId")))continue;
          object raw=PulseUsageParser.Get(bucket,"remainingFraction");
          if(!(raw is double||raw is decimal||raw is int||raw is long))continue;
          double fraction=Convert.ToDouble(raw);
          if(Double.IsNaN(fraction)||Double.IsInfinity(fraction)||fraction<0||fraction>1)continue;
          string reset=PulseUsageParser.Text(bucket,"resetTime");DateTimeOffset at;
          if(PulseUsageParser.Get(bucket,"resetTime")!=null&&(reset==null||!DateTimeOffset.TryParse(reset,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal,out at)))continue;
          if(reset!=null) {at=DateTimeOffset.Parse(reset,System.Globalization.CultureInfo.InvariantCulture);if(at<=DateTimeOffset.UtcNow)continue;reset=at.ToUniversalTime().ToString("o");}
          string label=seconds==604800?"周":seconds==86400?"每日":seconds==2592000?"月":seconds%86400==0?(seconds/86400)+" 天":(seconds/3600)+" 小时";
          result.Add(new PulseQuotaWindow{label=name+" · "+label+"剩余",remaining=Math.Round(fraction*100,2),resetsAt=reset});
        }
      }
      return result.Take(8).ToArray();
    }
    static int WindowSeconds(string value) {
      if(value==null)return 0;value=value.ToLowerInvariant();
      if(value=="weekly")return 604800;if(value=="daily")return 86400;if(value=="monthly")return 2592000;
      var match=Regex.Match(value,"^([1-9][0-9]{0,3})([hd])$");
      return match.Success?Int32.Parse(match.Groups[1].Value)*(match.Groups[2].Value=="h"?3600:86400):0;
    }
    static bool SameUser(IntPtr process) {
      IntPtr token;if(!OpenProcessToken(process,8,out token))return false;
      try{using(var identity=new WindowsIdentity(token))using(var current=WindowsIdentity.GetCurrent())return identity.User.Equals(current.User);}
      finally{CloseHandle(token);}
    }
    static byte[] Memory(IntPtr process,long address,int size) {
      var bytes=new byte[size];IntPtr read;
      if(address<=0||!ReadProcessMemory(process,new IntPtr(address),bytes,size,out read)||read.ToInt64()!=size){Array.Clear(bytes,0,bytes.Length);throw new InvalidOperationException("本机服务不可读");}
      return bytes;
    }
    static string ReadToken(IntPtr process) {
      // 仅支持原生 x64 PEB。未知架构降级，不猜偏移，也不读取一般堆内存。
      bool wow;if(IntPtr.Size!=8||!IsWow64Process(process,out wow)||wow)return null;
      var info=new IntPtr[6];int returned;
      if(NtQueryInformationProcess(process,0,info,IntPtr.Size*info.Length,out returned)!=0)return null;
      long parameters=BitConverter.ToInt64(Memory(process,info[1].ToInt64()+0x20,8),0);
      var command=Memory(process,parameters+0x70,16);int length=BitConverter.ToUInt16(command,0);
      if(length>0&&length<=32768) {
        var bytes=Memory(process,BitConverter.ToInt64(command,8),length);
        try {
          var match=Regex.Match(Encoding.Unicode.GetString(bytes),"(?:^|\\s)--csrf_token(?:=|\\s+)([A-Za-z0-9_-]{16,256})(?:\\s|$)");
          if(match.Success)return match.Groups[1].Value;
        }finally{Array.Clear(bytes,0,bytes.Length);}
      }
      long environment=BitConverter.ToInt64(Memory(process,parameters+0x80,8),0);
      var content=new byte[131072];int offset=0;
      try {
        while(offset<content.Length) {
          int size=Math.Min(4096-(int)((environment+offset)%4096),content.Length-offset);
          var part=Memory(process,environment+offset,size);
          Array.Copy(part,0,content,offset,size);Array.Clear(part,0,part.Length);
          int end=offset+size;
          for(int index=Math.Max(0,offset-2);index+3<end;index+=2)
            if(content[index]==0&&content[index+1]==0&&content[index+2]==0&&content[index+3]==0)return EnvironmentToken(Encoding.Unicode.GetString(content,0,index+2));
          offset=end;
        }
        return null;
      }finally{Array.Clear(content,0,content.Length);}
    }
    static int[] ListeningPorts(int pid) {
      int size=0;GetExtendedTcpTable(IntPtr.Zero,ref size,false,2,3,0);
      if(size<4||size>4194304)return new int[0];
      IntPtr table=Marshal.AllocHGlobal(size);
      try {
        if(GetExtendedTcpTable(table,ref size,false,2,3,0)!=0)return new int[0];
        int count=Marshal.ReadInt32(table);if(count<0||count>(size-4)/24)return new int[0];
        var ports=new List<int>();
        for(int index=0;index<count;index++) {
          IntPtr row=IntPtr.Add(table,4+index*24);
          if(Marshal.ReadInt32(row,0)!=2||Marshal.ReadInt32(row,20)!=pid||unchecked((uint)Marshal.ReadInt32(row,4))!=0x0100007f)continue;
          int raw=Marshal.ReadInt32(row,8);ports.Add(((raw&255)<<8)|((raw>>8)&255));
        }
        return ports.Distinct().OrderBy(port=>port).ToArray();
      }finally{Marshal.FreeHGlobal(table);}
    }
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] buffer,int size,out IntPtr read);
    [DllImport("kernel32.dll")] static extern bool IsWow64Process(IntPtr process,out bool wow);
    [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr process,int kind,[Out] IntPtr[] info,int size,out int returned);
    [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table,ref int size,bool order,int family,int kind,uint reserved);
  }
}
