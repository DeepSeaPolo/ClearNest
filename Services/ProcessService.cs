using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
namespace ClearNest.Services;
public record ProcessRow(int Id,string Name,long Bytes,string Note)
{
 public string Display => $"{Name}  ·  PID {Id}  ·  {Bytes/1048576d:F0} MB\n{Note}";
}
public static class ProcessService
{
 public static async Task<List<ProcessRow>> Scan()
 {
  var samples=new List<(int Id,DateTime Start,TimeSpan Cpu,long Bytes,string Name,bool Window)>();
  foreach(var p in Process.GetProcesses()) using(p) try
  { if(p.Id!=Environment.ProcessId) samples.Add((p.Id,p.StartTime,p.TotalProcessorTime,p.WorkingSet64,p.ProcessName,p.MainWindowHandle!=IntPtr.Zero)); }
  catch(Exception e) when(e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
  await Task.Delay(1500);
  var rows=new List<ProcessRow>();
  foreach(var s in samples) try
  {
   using var p=Process.GetProcessById(s.Id); if(p.StartTime!=s.Start) continue;
   var idle=(p.TotalProcessorTime-s.Cpu).TotalMilliseconds<30;
   rows.Add(new(s.Id,s.Name,p.WorkingSet64,idle&&!s.Window&&s.Bytes>104857600 ? "短时间低 CPU、无主窗口：仅供人工检查，可能是重要后台服务" : "正在运行；不能据此判断是否有用"));
  } catch(Exception e) when(e is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or NotSupportedException) { }
  return rows.OrderByDescending(x=>x.Bytes).ToList();
 }
}
