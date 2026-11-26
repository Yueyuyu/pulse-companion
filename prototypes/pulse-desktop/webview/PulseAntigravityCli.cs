// 协议参考 Pulse e5b7d921 AntigravityCLIUsage.swift（Apache-2.0）。
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexCompanion.PulseWebPreview {
  // 仅规范化已取得的额度字段；CLI 与本机专用版共享计算，不依赖任何桌面探测器。
  internal static class PulseAntigravityUsageParser {
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
  }
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
      var json=new JavaScriptSerializer();return PulseAntigravityUsageParser.Parse(json.DeserializeObject(json.Serialize(reply)));
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
        // 只限制本次只读调用。CreateNoWindow 不能阻止 CLI 的后台更新器另起终端。
        process.StartInfo.EnvironmentVariables["AGY_CLI_DISABLE_AUTO_UPDATE"]="true";
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
