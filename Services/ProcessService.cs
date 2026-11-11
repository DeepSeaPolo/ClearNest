using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace ClearNest.Services;
public record ProcessRow(int Id,string Name,long Bytes,long PrivateBytes,double Cpu,string Path,string Note)
{
 public string Display=>$"{Name} · PID {Id} · CPU {Cpu:F1}%\n工作集 {Bytes/1048576d:F0} MB · 私有提交 {PrivateBytes/1048576d:F0} MB\n{Note}\n{Path}";
}
public static class ProcessService
{
 [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder name,ref uint size);
 [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
 private record Sample(int Id,DateTime Start,TimeSpan Cpu,string Name,bool Window);
 private static bool Expected(Exception e)=>e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException or ArgumentException;
 public static Task<List<ProcessRow>> Scan(CancellationToken token=default,IProgress<OperationProgress>? progress=null)=>Task.Run(async()=>
 {
  var samples=new List<Sample>();var watch=Stopwatch.StartNew();
  foreach(var p in Process.GetProcesses())using(p)
  {
   token.ThrowIfCancellationRequested();
   try{if(p.Id!=Environment.ProcessId)samples.Add(new(p.Id,p.StartTime,p.TotalProcessorTime,p.ProcessName,p.MainWindowHandle!=IntPtr.Zero));}catch(Exception e)when(Expected(e)){}
  }
  progress?.Report(new("采样进程 CPU",samples.Count,0));
  await Task.Delay(1200,token).ConfigureAwait(false);
  double elapsed=watch.Elapsed.TotalMilliseconds;var rows=new List<ProcessRow>();
  foreach(var sample in samples)
  {
   token.ThrowIfCancellationRequested();
   try
   {
    using var p=Process.GetProcessById(sample.Id);if(p.StartTime!=sample.Start)continue;
    var delta=Math.Max(0,(p.TotalProcessorTime-sample.Cpu).TotalMilliseconds);
    double cpu=Math.Clamp(delta/elapsed/Environment.ProcessorCount*100,0,100);
    string path="路径不可访问";var handle=OpenProcess(0x1000,false,p.Id);
    if(handle!=IntPtr.Zero)try{var buffer=new StringBuilder(1024);uint size=(uint)buffer.Capacity;if(QueryFullProcessImageName(handle,0,buffer,ref size))path=buffer.ToString();}finally{CloseHandle(handle);}
    long bytes=p.WorkingSet64;
    rows.Add(new(p.Id,sample.Name,bytes,p.PrivateMemorySize64,cpu,path,cpu<0.5&&!sample.Window&&bytes>104857600?"短时低 CPU、无主窗口；仅供检查，不能判定为无用进程":"不会自动结束进程或修改优先级"));
   }catch(Exception e)when(Expected(e)){}
  }
  return rows.OrderByDescending(x=>x.Bytes).ToList();
 });
}
