using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Web.Script.Serialization;

namespace CodexCompanion.PulseWebPreview {
  // 下载版 DTO 只有额度来源及周期。没有桌面身份信息字段，也不引用任何提取器。
  internal sealed class PulseDesktopReading {
    internal string Source="none";
    internal PulseQuotaWindow[] Windows=new PulseQuotaWindow[0];
    internal DateTime ObservedAt=DateTime.UtcNow;
  }
  internal static class PulsePublicQuotaSource {
    internal static Task<PulseDesktopReading> Read(string id,CancellationToken token) {
      return id=="antigravity"?PulseAntigravityCli.Read(token):Task.FromResult(new PulseDesktopReading());
    }
    internal static void VerifyDistribution() {
      Assembly binary=typeof(PulsePublicQuotaSource).Assembly;
      const string prefix="CodexCompanion.PulseWebPreview.";
      foreach(string name in new[]{"PulseDesktopAccounts","PulseDesktopStorage","PulseWindowsGcm","PulseAntigravity","PulseDesktopAccountVerification","PulseAccountVerification"})
        if(binary.GetType(prefix+name,false)!=null)throw new Exception("公开宿主包含未授权的本机读取实现");
      foreach(var method in binary.GetTypes().SelectMany(type=>type.GetMethods(BindingFlags.DeclaredOnly|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance))) {
        if(method.Name=="ReadClaudeDesktop")throw new Exception("公开宿主不允许桌面会话入口");
        foreach(DllImportAttribute import in method.GetCustomAttributes(typeof(DllImportAttribute),false))
          if(new[]{"ReadProcessMemory","NtQueryInformationProcess","BCryptDecrypt"}.Contains(import.EntryPoint)||import.Value.Equals("winsqlite3.dll",StringComparison.OrdinalIgnoreCase))throw new Exception("公开宿主不允许第三方会话提取依赖");
      }
      if(PulseAccountHttp.Allowed(new Uri("https://claude.ai/api/organizations/11111111-1111-4111-8111-111111111111/usage"),HttpMethod.Get)||
        !PulseAccountHttp.Allowed(new Uri("https://api.anthropic.com/api/oauth/usage"),HttpMethod.Get)||
        !PulseAntigravityCli.SupportsUsage("1.1.11")||PulseAntigravityCli.SupportsUsage("1.1.10"))throw new Exception("官方来源边界不匹配");
      var fixture=new{status="SUCCESS",command=new{name="usage",data=new{groups=new[]{new{name="Fixture",buckets=new[]{new{id="weekly",window="weekly",remaining_fraction=0.34}}}}}}};
      var json=new JavaScriptSerializer();var parsed=PulseAntigravityCli.Parse(json.DeserializeObject(json.Serialize(fixture)));
      if(parsed.Length!=1||parsed[0].remaining!=34)throw new Exception("CLI 额度隔离样例失败");
    }
  }
}
