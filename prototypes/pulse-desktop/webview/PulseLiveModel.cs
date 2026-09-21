using System;
using System.Collections.Generic;
using System.Linq;
using CodexQuotaOverlay;

namespace CodexCompanion.PulseWebPreview {
  // 业务数据只在宿主中读取；发给 WebView 的 DTO 不包含账户响应、日志路径或工作目录。
  internal sealed class PulseLiveModel {
    readonly TaskLightSettings settings;
    WatchedTaskCollection watched;
    TaskListSnapshot tasks;
    QuotaSnapshot quota;
    long sequence;
    internal string QuotaState="loading", TasksState="loading", Notice="";
    internal DateTime QuotaAt=DateTime.MinValue, TasksAt=DateTime.MinValue;
    internal int WatchCount {get {return watched.Count;}}
    internal bool AlwaysOnTop {get {return settings.AlwaysOnTop;}}
    internal PulseLiveModel(TaskLightSettings saved) {
      settings=saved;watched=new WatchedTaskCollection(saved.WatchedTasks);
      if(saved.LoadFailed)Notice="原关注设置无法读取，已保留文件；请修复后重新启动";
    }
    internal void ApplyQuota(QuotaSnapshot value) {quota=value;QuotaState=value==null?"error":"ready";QuotaAt=DateTime.UtcNow;}
    internal void ApplyTasks(TaskListSnapshot value) {
      tasks=value;TasksState=value==null?"error":"ready";TasksAt=DateTime.UtcNow;
      bool changed;watched.BuildViews(tasks,TasksState=="ready",out changed);
      if(changed) SaveWatches();
    }
    internal void Disconnect() {QuotaState=TasksState="error";}
    internal TaskSnapshot FindTask(string id) {
      Guid parsed;if(!Guid.TryParse(id,out parsed))return null;
      var task=tasks==null?null:tasks.Tasks.FirstOrDefault(t=>String.Equals(t.Id,id,StringComparison.OrdinalIgnoreCase));
      if(task!=null)return task;
      var record=watched.Records.FirstOrDefault(t=>String.Equals(t.Id,id,StringComparison.OrdinalIgnoreCase));
      return record==null?null:record.ToTaskSnapshot();
    }
    internal bool SetWatch(string id,bool selected) {
      Notice="";
      var task=FindTask(id);
      if(task==null) {Notice="任务已不在当前列表中，请刷新后重试";return false;}
      if(watched.Contains(id)==selected)return true; // 显式期望值让重复点击/消息重试保持幂等。
      var previous=watched.Records.Select(t=>t.Clone()).ToArray();
      var result=watched.Toggle(task);
      if(result==WatchToggleResult.LimitReached) {Notice="最多同时关注 5 项";return false;}
      if(result==WatchToggleResult.InvalidTask) {Notice="任务标识无效";return false;}
      if(SaveWatches())return true;
      watched=new WatchedTaskCollection(previous);settings.ReplaceWatchedTasks(previous);
      return false;
    }
    bool SaveWatches() {
      settings.ReplaceWatchedTasks(watched.Records);
      string error;if(settings.TrySave(out error))return true;
      Notice=error;return false;
    }
    internal bool SetAlwaysOnTop(bool value) {
      bool previous=settings.AlwaysOnTop;settings.AlwaysOnTop=value;
      string error;if(settings.TrySave(out error))return true;
      settings.AlwaysOnTop=previous;Notice=error;return false;
    }
    internal PulseLiveSnapshot Snapshot() {
      // 重置后旧周期的百分比不再代表剩余额度，等待下一次成功读取。
      if(QuotaState=="ready"&&quota!=null&&quota.ResetsAtUtc!=DateTimeOffset.MinValue&&quota.ResetsAtUtc<=DateTimeOffset.UtcNow)QuotaState="loading";
      bool ignored;
      var views=watched.BuildViews(tasks,TasksState=="ready",out ignored);
      var rows=new List<PulseLiveTask>();
      foreach(var view in views)rows.Add(Row(view.Task,true,view.Available));
      if(tasks!=null)foreach(var task in tasks.Tasks) {
        Guid id;if(!Guid.TryParse(task.Id,out id)||watched.Contains(task.Id))continue;
        rows.Add(Row(task,false,TasksState=="ready"));
      }
      return new PulseLiveSnapshot {
        schemaVersion=1,source="companion-live",sequence=++sequence,
        quotaState=QuotaState,tasksState=TasksState,
        remaining=QuotaState=="ready"&&quota!=null?(int?)quota.RemainingPercent:null,
        resetsAt=QuotaState=="ready"&&quota!=null&&quota.ResetsAtUtc!=DateTimeOffset.MinValue?quota.ResetsAtUtc.ToString("o"):null,
        quotaUpdatedAt=QuotaAt==DateTime.MinValue?null:QuotaAt.ToString("o"),
        tasksUpdatedAt=TasksAt==DateTime.MinValue?null:TasksAt.ToString("o"),
        tasks=rows.ToArray(),notice=Notice
      };
    }
    static PulseLiveTask Row(TaskSnapshot task,bool isWatched,bool available) {
      available=available&&task.StateKnown;
      return new PulseLiveTask {id=task.Id,title=String.IsNullOrWhiteSpace(task.Title)?"未命名 Codex 任务":task.Title,
        state=!available?"unavailable":task.State==CodexTaskState.Running?"running":task.State==CodexTaskState.NeedsAttention?"attention":"completed",
        detail=available?task.Detail:"暂不可用",watched=isWatched,canOpen=true};
    }
  }
  internal sealed class PulseLiveSnapshot {
    public int schemaVersion;
    public long sequence;
    public string source,quotaState,tasksState,resetsAt,quotaUpdatedAt,tasksUpdatedAt,notice;
    public int? remaining;
    public PulseLiveTask[] tasks;
  }
  internal sealed class PulseLiveTask {public string id,title,state,detail;public bool watched,canOpen;}
}
