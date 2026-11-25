// 协议参考 Pulse e5b7d921 AntigravityCLIUsage.swift（Apache-2.0）。
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexCompanion.PulseWebPreview {
  internal static class PulseAntigravityCli {
    internal const string UsageArguments="--log-file NUL -p /usage --output-format json";
    internal static bool SupportsUsage(string text) {
      // 旧版会把 /usage 当模型提示词，必须先验证版本，未知版本也禁止执行。
      var match=Regex.Match(text.Trim(),"^(\\d+)\\.(\\d+)\\.(\\d+)$");Version version;
      return match.Success&&Version.TryParse(match.Value,out version)&&version>=new Version(1,1,11);
    }
    internal static PulseQuotaWindow[] Parse(object data) {
      object command=PulseUsageParser.Get(data,"command");
      if(PulseUsageParser.Text(data,"status")!="SUCCESS"||PulseUsageParser.Text(command,"name")!="usage")throw new PulseAccountException("cli-unavailable");
      var groups=PulseUsageParser.Get(PulseUsageParser.Get(command,"data"),"groups") as object[];
      if(groups==null)throw new PulseAccountException("response");
      // 只映射允许的额度字段，忽略文本答复、账户信息、计费与 token usage。
      var reply=new {response=new {groups=groups.Select(group=>new {displayName=PulseUsageParser.Text(group,"name"),buckets=(PulseUsageParser.Get(group,"buckets") as object[]??new object[0]).Select(bucket=>new {
        bucketId=PulseUsageParser.Text(bucket,"id"),window=PulseUsageParser.Text(bucket,"window"),remainingFraction=PulseUsageParser.Get(bucket,"remaining_fraction"),resetTime=PulseUsageParser.Get(bucket,"reset_time")
      }).ToArray()}).ToArray()}};
      var json=new JavaScriptSerializer();return PulseAntigravity.Parse(json.DeserializeObject(json.Serialize(reply)));
    }
    internal static async Task<PulseDesktopReading> Read(CancellationToken cancellation) {
      string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      string binary=new[]{Path.Combine(home,".local","bin","agy.exe"),Path.Combine(home,".gemini","bin","agy.exe")}.FirstOrDefault(File.Exists);
      if(binary==null)throw new PulseAccountException("local-service");
      // 单次查询使用独立空 cwd，不让 CLI 发现用户当前项目；绝不把凭据复制进此目录。
      string directory=Path.Combine(Path.GetTempPath(),"pulse-antigravity-"+Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      try {
        string version=await Run(binary,"--version",directory,4096,TimeSpan.FromSeconds(8),cancellation);
        if(!SupportsUsage(version))throw new PulseAccountException("cli-version");
        string output=await Run(binary,UsageArguments,directory,524288,TimeSpan.FromSeconds(30),cancellation);
        object data;try{data=new JavaScriptSerializer{MaxJsonLength=524288}.DeserializeObject(output);}catch{throw new PulseAccountException("response");}
        return new PulseDesktopReading{Source="official-cli",Windows=Parse(data),ObservedAt=DateTime.UtcNow};
      }finally {
        // 只删自己创建的空目录，不递归删除未知文件。
        try{Directory.Delete(directory,false);}catch(IOException){}catch(UnauthorizedAccessException){}
      }
    }
    internal static async Task<string> Run(string binary,string arguments,string directory,int ceiling,TimeSpan timeout,CancellationToken cancellation) {
      cancellation.ThrowIfCancellationRequested();
      using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation))
      using(var process=new Process{StartInfo=new ProcessStartInfo{FileName=binary,Arguments=arguments,WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true},EnableRaisingEvents=true}) {
        deadline.CancelAfter(timeout);
        var exited=new TaskCompletionSource<bool>();process.Exited+=(sender,args)=>exited.TrySetResult(true);
        cancellation.ThrowIfCancellationRequested();
        try{process.Start();}catch{throw new PulseAccountException("cli-unavailable");}
        process.StandardInput.Close();
        using(deadline.Token.Register(()=>{try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){} })) {
          var output=BoundedText(process.StandardOutput,ceiling,deadline);
          var errors=BoundedText(process.StandardError,16384,deadline);
          if(process.HasExited)exited.TrySetResult(true);
          await Task.WhenAll(exited.Task,output,errors);
          cancellation.ThrowIfCancellationRequested();
          if(deadline.IsCancellationRequested||process.ExitCode!=0)throw new PulseAccountException("cli-unavailable");
          return output.Result;
        }
      }
    }
    static async Task<string> BoundedText(StreamReader reader,int maximum,CancellationTokenSource deadline) {
      var result=new StringBuilder();var buffer=new char[2048];int count;
      while((count=await reader.ReadAsync(buffer,0,buffer.Length))>0) {
        if(result.Length+count>maximum){deadline.Cancel();throw new PulseAccountException("response");}
        result.Append(buffer,0,count);
      }
      return result.ToString();
    }
  }
}
