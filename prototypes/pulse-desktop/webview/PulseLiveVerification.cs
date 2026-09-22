using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using CodexQuotaOverlay;

namespace CodexCompanion.PulseWebPreview {
  internal static class PulseLiveVerification {
    static void Require(bool value,string label) {if(!value)throw new Exception(label);}
    static TaskSnapshot TaskFixture(int id,CodexTaskState state) {return new TaskSnapshot(id.ToString("00000000")+"-1111-4111-8111-111111111111","隔离验证任务 "+id,"","",state,"验证状态",DateTimeOffset.UtcNow);}
    internal static int SelfTest(string output) {
      string root=Path.Combine(Path.GetTempPath(),"pulse-live-test-"+Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(root);
      var checks=new List<string>();
      try {
        var automatic=PulseStartupOptions.Parse(new string[0]);
        Require(automatic.Live&&automatic.Background&&!automatic.SelfTest&&automatic.VisualReportPath==null,"无参数只能启动正常后台");
        var scheduled=PulseStartupOptions.Parse(new[]{"--live","--background"});
        Require(scheduled.Live&&scheduled.Background&&!scheduled.InspectWindow,"计划任务后台参数");
        var demo=PulseStartupOptions.Parse(new[]{"--demo"});
        Require(!demo.Live&&!demo.Background,"示例必须显式进入");
        Require(PulseStartupOptions.Parse(new[]{"--verify",root}).VisualReportPath==root,"显式视觉验收入口");
        Require(PulseStartupOptions.Parse(new[]{"--verify-live",Path.Combine(root,"live.json")}).VerifyLive,"显式实时验收入口");
        Require(PulseStartupOptions.Parse(new[]{"--live-self-test",Path.Combine(root,"self.json")}).SelfTest,"无窗口自测入口");
        foreach(var invalid in new[]{new[]{"--verify"},new[]{"--verify","--live"},new[]{"--verify",root,"--live"},new[]{"--live","--demo"},new[]{"--background"},new[]{"--live","--live"},new[]{"--live","--background","--inspect-window"},new[]{"--unknown"}}) {
          bool rejected=false;try{PulseStartupOptions.Parse(invalid);}catch(ArgumentException){rejected=true;}
          Require(rejected,"混合或非法入口不得回落到示例");
        }
        string pause=Path.Combine(root,"pause","background.pause");
        Require(!PulseBackgroundPause.IsPaused(pause)&&PulseBackgroundPause.TryPause(pause)&&PulseBackgroundPause.IsPaused(pause),"主动退出保存暂停标记");
        File.Delete(pause);Require(!PulseBackgroundPause.IsPaused(pause),"重新启动可清除暂停");
        string blockedPause=Path.Combine(root,"blocked-pause");File.WriteAllText(blockedPause,"fixture");
        Require(!PulseBackgroundPause.TryPause(Path.Combine(blockedPause,"pause")),"暂停写入失败不得报告成功");
        checks.Add("startup-default-background-explicit-demo-verification-pause");
        string path=Path.Combine(root,"task-light.json");
        var appearancePath=Path.Combine(root,"appearance.json");
        var appearance=PulseAppearanceSettings.Load(appearancePath);
        Require(appearance.Get("codex")=="robot","兼容原 Codex 机器人默认值");
        Require(appearance.Set("codex","brand")&&appearance.Set("test-app","robot"),"独立图标选择保存");
        appearance=PulseAppearanceSettings.Load(appearancePath);
        Require(appearance.Get("codex")=="brand"&&appearance.Get("test-app")=="robot","重启后图标按应用恢复");
        Require(!appearance.Set("../outside","robot")&&!appearance.Set("codex","url"),"拒绝非法图标设置");
        File.WriteAllText(appearancePath,"broken");
        Require(!PulseAppearanceSettings.Load(appearancePath).Set("codex","robot")&&File.ReadAllText(appearancePath)=="broken","损坏图标设置不覆盖");
        var appearanceBlocked=Path.Combine(root,"appearance-blocked");File.WriteAllText(appearanceBlocked,"fixture");
        var blockedAppearance=PulseAppearanceSettings.Load(Path.Combine(appearanceBlocked,"settings.json"));
        Require(!blockedAppearance.Set("codex","brand")&&blockedAppearance.Get("codex")=="robot","图标写入失败回滚");
        Require(!PulseBackgroundPolicy.ShouldShow(true,false)&&PulseBackgroundPolicy.ShouldShow(true,true)&&PulseBackgroundPolicy.ShouldShow(false,false),"后台显隐策略");
        Require(PulseBackgroundPolicy.AcceptsApplication("codex")&&PulseBackgroundPolicy.AcceptsApplication("cursor")&&!PulseBackgroundPolicy.AcceptsApplication("https://bad.test"),"应用命令白名单");
        PulseAccountVerification.Run(root,checks);
        PulseDesktopAccountVerification.Run(root,checks);
        checks.Add("application-appearance-isolation-restart-corrupt-rollback-background-policy");
        var model=new PulseLiveModel(TaskLightSettings.LoadFromPath(path));
        Require(model.Snapshot().remaining==null,"初始额度不能是示例值");
        var fixtures=Enumerable.Range(1,6).Select(i=>TaskFixture(i,CodexTaskState.Running)).ToArray();
        model.ApplyTasks(new TaskListSnapshot(fixtures));
        model.ApplyQuota(new QuotaSnapshot(43,57,10080,DateTimeOffset.UtcNow.AddDays(2),"codex","Codex"));
        Require(model.Snapshot().remaining==43,"真实 DTO 额度转换");checks.Add("unknown-and-quota-mapping");
        model.ApplyQuota(new QuotaSnapshot(43,57,10080,DateTimeOffset.UtcNow.AddSeconds(-1),"codex","Codex"));
        Require(model.Snapshot().remaining==null,"重置后的旧周期额度不得继续显示");checks.Add("expired-quota-hidden");
        for(int i=0;i<5;i++)Require(model.SetWatch(fixtures[i].Id,true),"添加关注");
        Require(!model.SetWatch(fixtures[5].Id,true)&&model.WatchCount==5,"关注上限");
        Require(model.SetWatch(fixtures[0].Id,true)&&model.WatchCount==5,"重复消息幂等");
        var restored=new PulseLiveModel(TaskLightSettings.LoadFromPath(path));
        Require(restored.WatchCount==5,"磁盘重启恢复");
        restored.ApplyTasks(new TaskListSnapshot(new[]{TaskFixture(1,CodexTaskState.Completed)}));
        var rows=restored.Snapshot().tasks;
        Require(rows.Length==5&&rows[0].state=="completed"&&rows[1].state=="unavailable","完成和缺失仍保留，顺序不变");
        restored.Disconnect();Require(restored.Snapshot().remaining==null&&restored.Snapshot().tasks.All(t=>t.state=="unavailable"),"断线诚实展示");
        Require(restored.SetWatch(fixtures[1].Id,false)&&restored.WatchCount==4,"离线取消关注");
        Require(new PulseLiveModel(TaskLightSettings.LoadFromPath(path)).WatchCount==4,"取消已持久化");checks.Add("watch-limit-order-restart-offline");
        var blockedPath=Path.Combine(root,"blocked");var blockedModel=new PulseLiveModel(TaskLightSettings.LoadFromPath(Path.Combine(blockedPath,"settings.json")));
        File.WriteAllText(blockedPath,"fixture");blockedModel.ApplyTasks(new TaskListSnapshot(fixtures));
        Require(!blockedModel.SetWatch(fixtures[0].Id,true)&&blockedModel.WatchCount==0,"写盘失败回滚");checks.Add("persistence-failure-rollback");
        string corruptPath=Path.Combine(root,"corrupt.json");File.WriteAllText(corruptPath,"{corrupt");
        var corruptSettings=TaskLightSettings.LoadFromPath(corruptPath);string saveStatus;
        Require(corruptSettings.LoadFailed&&!corruptSettings.TrySave(out saveStatus)&&File.ReadAllText(corruptPath)=="{corrupt","损坏设置不得覆盖");checks.Add("corrupt-settings-preserved");
        var parserFixture=new JavaScriptSerializer().DeserializeObject("{\"data\":[{\"id\":\""+fixtures[0].Id+"\",\"status\":{\"type\":\"notLoaded\"}}]}");
        var unknownTasks=TaskParser.ParseResult(parserFixture,new TaskLogReader());
        Require(unknownTasks!=null&&!unknownTasks.Tasks[0].StateKnown,"日志缺失不能推断为完成");
        var unknownModel=new PulseLiveModel(TaskLightSettings.CreateTransient());unknownModel.ApplyTasks(unknownTasks);
        Require(unknownModel.Snapshot().tasks[0].state=="unavailable","未知任务在 Pulse 中显示暂不可用");checks.Add("missing-log-is-unavailable");
        int shown=0;
        using(var notifier=new TaskLightNotifier(delegate(TaskToastKind kind,TaskSnapshot task){shown++;})) {
          var running=new TaskListSnapshot(new[]{fixtures[0]});var completed=new TaskListSnapshot(new[]{TaskFixture(1,CodexTaskState.Completed)});
          notifier.Update(completed);Require(shown==0,"首连不通知历史");
          notifier.Update(running);notifier.Update(new TaskListSnapshot(null));Require(shown==0,"消失不等于完成");
          notifier.Update(running);notifier.Update(unknownTasks);Require(shown==0,"未知不等于完成");
          var oldComplete=new TaskSnapshot(fixtures[0].Id,"旧轮完成","","",CodexTaskState.Completed,"已完成",fixtures[0].ActivityAtUtc.AddMinutes(-1));
          notifier.Update(running);notifier.Update(new TaskListSnapshot(new[]{oldComplete}));Require(shown==0,"旧轮完成不通知");
          notifier.Update(running);notifier.Update(completed);notifier.Update(completed);Require(shown==1,"明确完成只通知一次");
          notifier.Reset();notifier.Update(completed);Require(shown==1,"重连不补报");
          notifier.Update(new TaskListSnapshot(new[]{TaskFixture(1,CodexTaskState.NeedsAttention)}));Require(shown==2,"新需处理通知");
        }
        checks.Add("notification-baseline-completion-dedup-reconnect");
        Uri uri;Require(model.FindTask("file:///bad")==null&&!CodexThreadNavigator.TryBuildUri("file:///bad",out uri),"拒绝非 UUID");
        Require(model.FindTask(fixtures[0].Id)!=null&&CodexThreadNavigator.TryBuildUri(fixtures[0].Id,out uri)&&uri.Scheme=="codex","已知任务跳转");checks.Add("navigation-uuid-whitelist");
        Write(output,new{passed=true,verifiedAtUtc=DateTime.UtcNow.ToString("o"),kind="isolated fixtures; no real settings/notifications/navigation",checks=checks});return 0;
      } catch(Exception e) {Write(output,new{passed=false,error=e.Message,checks=checks});return 1;}
      finally {Directory.Delete(root,true);}
    }
    internal static async Task VerifyReadOnly(WidgetWindow widget,PulseLiveRuntime runtime,string output) {
      try {
        DateTime deadline=DateTime.UtcNow.AddSeconds(45);
        while(DateTime.UtcNow<deadline && (runtime.Model.QuotaState!="ready"||runtime.Model.TasksState!="ready"||runtime.TaskUpdates<2))await Task.Delay(250);
        Require(runtime.Model.QuotaState=="ready"&&runtime.Model.TasksState=="ready"&&runtime.TaskUpdates>=2,"真实数据未在超时前就绪");
        runtime.Publish();widget.Send("pinned",true);widget.Send("mode","expanded");await Task.Delay(900);
        var snapshot=runtime.Model.Snapshot();
        var raw=await widget.View.CoreWebView2.ExecuteScriptAsync("JSON.stringify({source:document.querySelector('.pd-native').dataset.source,sequence:Number(document.querySelector('.pd-native').dataset.sequence),percent:document.querySelector('.pd-quota-card strong').textContent,rows:document.querySelectorAll('.pd-task').length,text:document.querySelector('.pd-panel').textContent})");
        var json=new JavaScriptSerializer();var dom=json.Deserialize<Dictionary<string,object>>(json.Deserialize<string>(raw));
        Require(Convert.ToString(dom["source"])=="companion-live"&&Convert.ToInt64(dom["sequence"])>0,"非真实 desktop 通道");
        Require(Convert.ToString(dom["percent"])==snapshot.remaining+"%","额度未渲染真实快照");
        Require(Convert.ToInt32(dom["rows"])==snapshot.tasks.Length,"任务未渲染真实快照");
        Require(!Convert.ToString(dom["text"]).Contains("示例"),"真实界面混入示例标签");
        runtime.ApplyPresence(false);Require(!widget.IsVisible&&!widget.ShowInTaskbar,"无应用时后台隐藏，无任务栏");
        runtime.ApplyPresence(true);Require(widget.IsVisible&&!widget.ShowInTaskbar,"应用重开后自动恢复窗口");
        Write(output,new{passed=true,verifiedAtUtc=DateTime.UtcNow.ToString("o"),kind="real read-only RPC to WebView DOM; simulated process-presence hide/show on real WPF; no real settings writes, notification or navigation",remaining=snapshot.remaining,taskCount=snapshot.tasks.Length,taskUpdates=runtime.TaskUpdates,backgroundLifecycle=true});
        Application.Current.Shutdown(0);
      } catch(Exception e) {Write(output,new{passed=false,error=e.Message,verifiedAtUtc=DateTime.UtcNow.ToString("o")});Application.Current.Shutdown(1);}
    }
    internal static void Write(string file,object value) {Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));File.WriteAllText(file,new JavaScriptSerializer().Serialize(value));}
  }
}
