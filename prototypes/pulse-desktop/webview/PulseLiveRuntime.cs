using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Threading;
using CodexQuotaOverlay;
using Forms=System.Windows.Forms;

namespace CodexCompanion.PulseWebPreview {
  internal sealed class PulseLiveRuntime : IDisposable {
    readonly WidgetWindow widget;
    readonly DispatcherTimer timer;
    readonly TaskLightNotifier notifier;
    readonly Forms.NotifyIcon tray;
    readonly PulseTrayIcon trayArtwork;
    readonly PulseWindowSettings preferences;
    readonly bool verification;
    readonly bool background;
    readonly PulseAppearanceSettings appearance;
    readonly PulseAccountService accounts;
    internal readonly PulseLiveModel Model;
    AppServerClient client;
    HashSet<string> openApplications=new HashSet<string>();
    DateTime connectedAt,lastQuotaRequest;
    bool disposed,ready;
    internal int TaskUpdates {get;private set;}
    internal PulseLiveRuntime(WidgetWindow window,bool verify=false,bool runInBackground=false) {
      widget=window;verification=verify;
      background=runInBackground;
      appearance=verify?new PulseAppearanceSettings():PulseAppearanceSettings.Load(PulseAppearanceSettings.FilePath);
      accounts=new PulseAccountService(!verify);accounts.Changed+=Publish;
      Model=new PulseLiveModel(verify?TaskLightSettings.CreateTransient():TaskLightSettings.Load());
      preferences=verify?new PulseWindowSettings():PulseWindowSettings.Load();
      notifier=new TaskLightNotifier(verify?(Action<TaskToastKind,TaskSnapshot>)delegate {}:null);
      notifier.TaskActivated+=delegate(object sender,TaskActivatedEventArgs e){OpenTask(e.Task);};
      widget.Title="Pulse Companion · 应用状态";widget.Topmost=Model.AlwaysOnTop;
      widget.Ready+=Start;
      widget.Command+=Command;
      widget.PlacementChanged+=SavePlacement;
      widget.Failed+=delegate {Model.Disconnect();Model.Notice="桌面呈现连接异常，请从托盘刷新或重新启动";};
      tray=new Forms.NotifyIcon {Text="Pulse Companion · 应用状态"};
      trayArtwork=new PulseTrayIcon(tray,widget.Dispatcher,!verify);
      tray.Visible=!verify;
      var menu=new Forms.ContextMenuStrip();
      menu.Items.Add("展开 Pulse Companion",null,delegate {ShowFromTray();});
      menu.Items.Add("立即刷新",null,delegate {Refresh();});
      menu.Items.Add("贴边收起",null,delegate {widget.Send("mode","docked");});
      var icons=new Forms.ToolStripMenuItem("Codex 图标");
      icons.DropDownItems.Add("品牌图标",null,delegate {SetIcon("codex","brand");});
      icons.DropDownItems.Add("机器人",null,delegate {SetIcon("codex","robot");});menu.Items.Add(icons);
      var topmost=new Forms.ToolStripMenuItem("始终置顶") {Checked=Model.AlwaysOnTop,CheckOnClick=true};
      topmost.Click+=delegate {if(Model.SetAlwaysOnTop(topmost.Checked))widget.Topmost=topmost.Checked;else topmost.Checked=Model.AlwaysOnTop;Publish();};menu.Items.Add(topmost);
      menu.Items.Add("退出并暂停自动恢复",null,delegate {
        if(!verification&&!PulseBackgroundPause.TryPause(PulseBackgroundPause.FilePath)) {
          Model.Notice="无法保存暂停状态，未退出；请检查本机设置目录";Publish();return;
        }
        Application.Current.Shutdown();
      });tray.ContextMenuStrip=menu;
      tray.DoubleClick+=delegate {ShowFromTray();};
      timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(2)};timer.Tick+=Tick;
    }
    void Start() {
      if(ready)return;ready=true;
      if(preferences.HasPosition)widget.RestorePosition(preferences.Side=="right"?preferences.Right-widget.Width:preferences.Left,preferences.Top);
      widget.Send("side",preferences.Side);widget.Send("scale",preferences.Scale);
      widget.Send("pinned",preferences.Pinned);widget.Send("mode",preferences.Mode);
      timer.Start();Tick(null,EventArgs.Empty);
    }
    void Dispatch(object sender,Action action) {
      if(disposed||sender!=client)return;
      widget.Dispatcher.BeginInvoke(new Action(delegate {if(!disposed&&sender==client) {action();Publish();}}));
    }
    void Connect() {
      var next=new AppServerClient();client=next;connectedAt=DateTime.UtcNow;lastQuotaRequest=connectedAt;
      next.QuotaUpdated+=delegate(object sender,QuotaEventArgs e) {Dispatch(sender,()=>Model.ApplyQuota(e.Snapshot));};
      next.QuotaConnectionChanged+=delegate(object sender,TaskConnectionEventArgs e) {if(!e.Connected)Dispatch(sender,()=>Model.QuotaState="error");};
      next.TasksUpdated+=delegate(object sender,TaskListEventArgs e) {Dispatch(sender,delegate {Model.ApplyTasks(e.Snapshot);TaskUpdates++;notifier.Update(e.Snapshot);});};
      next.TaskConnectionChanged+=delegate(object sender,TaskConnectionEventArgs e) {if(!e.Connected)Dispatch(sender,delegate {Model.TasksState="error";notifier.Reset();});};
      next.Start();
    }
    void Tick(object sender,EventArgs e) {
      if(disposed||!ready)return;
      openApplications=new HashSet<string>(verification?(CodexWindowTracker.IsCodexDesktopRunning()?new[]{"codex"}:new string[0]):PulseApplicationPresence.Read());
      accounts.SetOpenApplications(openApplications);
      bool running=openApplications.Contains("codex");
      ApplyPresence(openApplications.Count>0);
      accounts.Tick();
      if(!running) {StopClient();Model.Disconnect();Publish();return;}
      if(client==null) {
        Model.QuotaState=Model.TasksState="loading";
        try {Connect();}catch {StopClient();Model.Disconnect();}
        Publish();return;
      }
      var now=DateTime.UtcNow;
      if(now-(Model.TasksAt>connectedAt?Model.TasksAt:connectedAt)>TimeSpan.FromSeconds(30)) {
        StopClient();Model.Disconnect();Publish();return;
      }
      if(now-(Model.TasksAt>connectedAt?Model.TasksAt:connectedAt)>TimeSpan.FromSeconds(12)) {Model.TasksState="error";notifier.Reset();}
      if(now-(Model.QuotaAt>connectedAt?Model.QuotaAt:connectedAt)>TimeSpan.FromMinutes(2))Model.QuotaState="error";
      // 异步读取与请求合并由现有客户端负责；UI 线程不等待账户 RPC。
      try {
        client.RequestTaskRefresh();
        if(now-lastQuotaRequest>TimeSpan.FromSeconds(60)) {lastQuotaRequest=now;client.RequestRefresh();}
      } catch {StopClient();Model.Disconnect();}
      Publish();
    }
    void Refresh() {
      Model.Notice="";
      try {if(client==null)Tick(null,EventArgs.Empty);else {lastQuotaRequest=DateTime.UtcNow;client.RequestRefresh();client.RequestTaskRefresh();}}
      catch {StopClient();Model.Disconnect();Model.Notice="连接已中断，正在重试";}
      Publish();
    }
    void Command(Dictionary<string,object> message) {
      if(disposed)return;
      var type=Convert.ToString(message["type"]);
      if(type!="pin") {
        object appId;if(!message.TryGetValue("applicationId",out appId)||!(appId is string)||!PulseBackgroundPolicy.AcceptsApplication((string)appId))return;
        if(!openApplications.Contains((string)appId))return;
      }
      if(type!="pin"&&(string)message["applicationId"]!="codex") {
        string applicationId=(string)message["applicationId"];
        if(type=="icon-mode") {object value;if(message.TryGetValue("value",out value)&&value is string)SetIcon(applicationId,(string)value);}
        else accounts.Command(applicationId,type);
        return;
      }
      if(type=="refresh")Refresh();
      else if(type=="watch") {
        object id,value;if(message.TryGetValue("id",out id)&&id is string&&message.TryGetValue("watched",out value)&&value is bool)Model.SetWatch((string)id,(bool)value);
        Publish();
      } else if(type=="open-task") {
        object id;if(message.TryGetValue("id",out id)&&id is string)OpenTask(Model.FindTask((string)id));
      } else if(type=="icon-mode") {
        object value;if(message.TryGetValue("value",out value)&&value is string)SetIcon((string)message["applicationId"],(string)value);
      } else if(type=="pin") {
        object value;if(!message.TryGetValue("value",out value)||!(value is bool))return;
        bool old=preferences.Pinned;preferences.Pinned=(bool)value;
        if(!verification&&!preferences.Save()) {preferences.Pinned=old;Model.Notice="固定状态保存失败，请重试";}
        widget.Send("pinned",preferences.Pinned);Publish();
      }
    }
    void OpenTask(TaskSnapshot task) {
      if(verification)return;
      if(task==null) {Model.Notice="任务已不在当前列表中，请刷新后重试";Publish();return;}
      string status;
      if(!CodexThreadNavigator.TryOpen(task,out status)) {Model.Notice=status;notifier.ShowNavigationError(task,status);}
      else Model.Notice="已请求 Codex 打开对应任务";
      Publish();
    }
    void SavePlacement() {
      if(!ready||disposed||verification)return;
      preferences.HasPosition=true;preferences.Left=widget.Left;preferences.Top=widget.Top;preferences.Right=widget.Left+widget.Width;
      preferences.Mode=widget.Mode;preferences.Side=widget.Side;preferences.Scale=widget.RenderScale;
      if(!preferences.Save()) {Model.Notice="窗口位置保存失败";Publish();}
    }
    void ShowFromTray() {Tick(null,EventArgs.Empty);if(openApplications.Count>0){widget.Show();widget.Send("mode","expanded");}else tray.ShowBalloonTip(3000,"Pulse Companion 后台运行中","打开已登记的 AI 应用后会自动显示桌面浮条。",Forms.ToolTipIcon.Info);}
    internal void ApplyPresence(bool running) {
      bool visible=PulseBackgroundPolicy.ShouldShow(background,running);
      if(visible&&!widget.IsVisible)widget.Show();else if(!visible&&widget.IsVisible)widget.Hide();
      widget.Opacity=1;
    }
    void SetIcon(string id,string mode) {
      if(!PulseBackgroundPolicy.AcceptsApplication(id)||!PulseAppearanceSettings.ValidMode(mode))return;
      if(verification)return;
      Model.Notice=appearance.Set(id,mode)?"":"图标选择保存失败，请重试；原设置未更改";Publish();
    }
    internal void Publish() {
      if(!ready||disposed)return;
      var state=Model.Snapshot();
      var applications=new List<object>();if(openApplications.Contains("codex"))applications.Add(new {id="codex",iconMode=appearance.Get("codex"),state=state});
      if(!verification)applications.AddRange(accounts.Snapshots(appearance));
      widget.Send("snapshot",new {schemaVersion=2,source="companion-live",sequence=state.sequence,applications=applications.ToArray()});
      if(!verification) {
        // 仅运维元数据；不记录账户响应、任务标题或凭据。
        try {File.WriteAllText(Path.Combine(Path.GetDirectoryName(PulseWindowSettings.FilePath),"pulse-runtime.json"),new JavaScriptSerializer().Serialize(new {pid=Process.GetCurrentProcess().Id,updatedAtUtc=DateTime.UtcNow.ToString("o"),background=background,visible=widget.IsVisible,showInTaskbar=widget.ShowInTaskbar,quotaState=state.quotaState,tasksState=state.tasksState,applications=PulseApplications.Ids.Where(openApplications.Contains).ToArray(),iconMode=appearance.Get("codex")}));} catch { }
      }
    }
    void StopClient() {var old=client;client=null;if(old!=null)old.Dispose();notifier.Reset();}
    public void Dispose() {
      if(disposed)return;disposed=true;timer.Stop();accounts.Changed-=Publish;accounts.Dispose();StopClient();notifier.Dispose();tray.Visible=false;tray.Dispose();trayArtwork.Dispose();
      widget.Command-=Command;widget.Ready-=Start;widget.PlacementChanged-=SavePlacement;
    }
  }
}
