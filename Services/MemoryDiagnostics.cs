using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace ClearNest.Services;
public record MemoryReport(ulong PhysicalTotal,ulong PhysicalAvailable,ulong CommitBytes,ulong CommitLimit,ulong KernelPaged,ulong KernelNonpaged,ulong? Standby,ulong? Modified)
{
 public string Describe()
 {
  string Size(ulong? value)=>value.HasValue?MemoryService.Size(value.Value):"无法读取（不是 0）";
  double ratio=CommitLimit>0?CommitBytes*100d/CommitLimit:0;
  var text=new StringBuilder();
  text.AppendLine($"物理内存：{MemoryService.Size(PhysicalTotal)}");text.AppendLine($"可用物理内存：{MemoryService.Size(PhysicalAvailable)}");
  text.AppendLine($"待机缓存：{Size(Standby)}");text.AppendLine($"修改页：{Size(Modified)}（本工具不清理）");
  text.AppendLine($"系统提交量：{MemoryService.Size(CommitBytes)} / {MemoryService.Size(CommitLimit)}（{ratio:F1}%）");
  text.AppendLine($"内核分页池：{MemoryService.Size(KernelPaged)}；非分页池：{MemoryService.Size(KernelNonpaged)}");
  text.AppendLine();text.AppendLine("提交限制不是分页文件大小。待机缓存已包含在可用物理内存内，回收不会保证占用率下降。");
  text.AppendLine(ratio>=85?"提交压力偏高：建议先查看进程的私有提交量，并检查 Windows 是否采用合适的分页文件设置。":"当前提交压力没有达到 85% 提示线。若仍卡顿，请结合进程 CPU、磁盘和驱动表现排查。");
  return text.ToString();
 }
}
public static class MemoryDiagnostics
{
 [StructLayout(LayoutKind.Sequential)] private struct PerformanceInfo
 {
  public uint Size;public UIntPtr CommitTotal,CommitLimit,CommitPeak,PhysicalTotal,PhysicalAvailable,SystemCache,KernelTotal,KernelPaged,KernelNonpaged,PageSize;
  public uint HandleCount,ProcessCount,ThreadCount;
 }
 [StructLayout(LayoutKind.Sequential)] private struct CounterValue {public uint Status;public double Value;}
 [DllImport("psapi.dll",SetLastError=true)] private static extern bool GetPerformanceInfo(ref PerformanceInfo info,uint size);
 [DllImport("pdh.dll",CharSet=CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source,UIntPtr data,out IntPtr query);
 [DllImport("pdh.dll",CharSet=CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query,string path,UIntPtr data,out IntPtr counter);
 [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
 [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter,uint format,out uint type,out CounterValue value);
 [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
 // Invoked only by worker tasks, never by the dispatcher sampling timer.
 public static MemoryReport Read()
 {
  var memory=MemoryService.Read();var info=new PerformanceInfo {Size=(uint)Marshal.SizeOf<PerformanceInfo>()};
  if(!GetPerformanceInfo(ref info,info.Size))throw new Win32Exception(Marshal.GetLastWin32Error());
  ulong page=info.PageSize.ToUInt64();ulong? standby=null,modified=null;
  if(PdhOpenQuery(null,UIntPtr.Zero,out var query)==0)
  {
   try
   {
    string[] names={"Standby Cache Core Bytes","Standby Cache Normal Priority Bytes","Standby Cache Reserve Bytes","Modified Page List Bytes"};
    var handles=new IntPtr[4];
    for(int i=0;i<names.Length;i++)if(PdhAddEnglishCounter(query,"\\Memory\\"+names[i],UIntPtr.Zero,out var handle)==0)handles[i]=handle;
    if(PdhCollectQueryData(query)==0)
    {
     ulong? Value(int i)=>handles[i]!=IntPtr.Zero&&PdhGetFormattedCounterValue(handles[i],0x200,out _,out var value)==0&&value.Status<=1&&double.IsFinite(value.Value)&&value.Value>=0?(ulong)value.Value:null;
     var a=Value(0);var b=Value(1);var c=Value(2);if(a.HasValue&&b.HasValue&&c.HasValue)standby=a+b+c;
     modified=Value(3);
    }
   }finally{PdhCloseQuery(query);}
  }
  return new(memory.Total,memory.Available,info.CommitTotal.ToUInt64()*page,info.CommitLimit.ToUInt64()*page,info.KernelPaged.ToUInt64()*page,info.KernelNonpaged.ToUInt64()*page,standby,modified);
 }
}
