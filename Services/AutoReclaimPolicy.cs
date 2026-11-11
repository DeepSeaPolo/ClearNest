using System;
namespace ClearNest.Services;
// Decides when to request standby-only reclamation. This class never mutates memory itself.
public sealed class AutoReclaimPolicy
{
 private DateTimeOffset nextScheduled=DateTimeOffset.MaxValue,lastAttempt=DateTimeOffset.MinValue;
 private bool thresholdArmed=true;
 public void Configure(Preferences p,DateTimeOffset now)
 {nextScheduled=now.AddMinutes(p.AutoIntervalMinutes);thresholdArmed=true;}
 public void RecordAttempt(Preferences p,DateTimeOffset now,double load)
 {
  lastAttempt=now;nextScheduled=now.AddMinutes(p.AutoIntervalMinutes);
  if(p.AutoThresholdEnabled&&load>=p.AutoThresholdPercent)thresholdArmed=false;
 }
 public string? GetDue(Preferences p,DateTimeOffset now,double load)
 {
  if(load<=Math.Max(0,p.AutoThresholdPercent-5))thresholdArmed=true;
  if(now-lastAttempt<TimeSpan.FromMinutes(5))return null;
  bool threshold=p.AutoThresholdEnabled&&thresholdArmed&&load>=p.AutoThresholdPercent;
  bool scheduled=p.AutoIntervalEnabled&&now>=nextScheduled;
  if(!threshold&&!scheduled)return null;
  RecordAttempt(p,now,load);
  return threshold?$"占用达到 {p.AutoThresholdPercent}%":$"每 {p.AutoIntervalMinutes} 分钟的定时计划";
 }
}
