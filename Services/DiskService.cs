using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace ClearNest.Services;
public record DiskRow(string Path,long Bytes,DateTime Written,string Category="临时文件")
{
 public string Display=>$"{Category} · {Bytes/1048576d:F2} MB  ·  {Path}";
}
public record DiskScanResult(List<DiskRow> Rows,int Skipped,int Seen,bool Cancelled,bool Limited);
public record DiskDeleteResult(long Bytes,int Deleted,int Skipped,bool Cancelled);
public static class DiskService
{
 // Only allow-listed temporary files on C:, at least seven days old. Never follow junctions.
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [StructLayout(LayoutKind.Sequential)] private struct Disposition { [MarshalAs(UnmanagedType.U1)] public bool Delete; }
 [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle,int fileClass,ref Disposition info,uint size);
 private static IEnumerable<(string Path,string Category)> Locations(bool caches)
 {
  yield return(System.IO.Path.GetTempPath(),"用户临时文件");yield return(@"C:\Windows\Temp","系统临时文件");
  if(caches)
  {
   var roaming=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
   foreach(var app in new[]{"discord","Slack","Microsoft/Teams"})
    foreach(var cache in new[]{"Cache","Code Cache","GPUCache"})
     yield return(System.IO.Path.Combine(roaming,app.Replace('/',System.IO.Path.DirectorySeparatorChar),cache),"应用缓存（"+app+"）");
  }
 }
 private static (string Path,string Category)[] Roots(bool caches)=>Locations(caches)
  .Select(x=>(Path:System.IO.Path.GetFullPath(x.Path),x.Category)).Where(x=>x.Path.StartsWith(@"C:\",StringComparison.OrdinalIgnoreCase))
  .DistinctBy(x=>x.Path,StringComparer.OrdinalIgnoreCase).ToArray();
 private static bool Safe(string path,string root)
 {
  var full=System.IO.Path.GetFullPath(path); var boundary=root.TrimEnd('\\')+"\\";
  if(!full.Equals(root.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)&&!full.StartsWith(boundary,StringComparison.OrdinalIgnoreCase)) return false;
  var current=full;
  while(true)
  {
   if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) return false;
   var parent=System.IO.Path.GetDirectoryName(current);
   if(parent==null) break;
   current=parent;
  }
  return true;
 }
 public static DiskScanResult Scan(bool includeCaches=false,CancellationToken token=default,IProgress<OperationProgress>? progress=null)
 {
  var rows=new List<DiskRow>();int skipped=0,seen=0,directories=0;var watch=Stopwatch.StartNew();long reported=0;
  var options=new EnumerationOptions {RecurseSubdirectories=false,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint|FileAttributes.System,ReturnSpecialDirectories=false};
  foreach(var root in Roots(includeCaches))
  {
   if(!Directory.Exists(root.Path))continue;
   var pending=new Stack<string>();pending.Push(root.Path);
   while(pending.Count>0)
   {
    if(token.IsCancellationRequested)return new(rows,skipped,seen,true,false);
    if(rows.Count>=20000||seen>=100000||directories>=10000||watch.Elapsed.TotalSeconds>=60)return new(rows,skipped,seen,false,true);
    var dir=pending.Pop();directories++;
    try
    {
     if(!Safe(dir,root.Path)){skipped++;continue;}
     foreach(var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*",options))
     {
      if(token.IsCancellationRequested)return new(rows,skipped,seen,true,false);
      if(rows.Count>=20000||seen>=100000||watch.Elapsed.TotalSeconds>=60)return new(rows,skipped,seen,false,true);
      if((entry.Attributes&FileAttributes.ReparsePoint)!=0){skipped++;continue;}
      if(entry is DirectoryInfo){if(pending.Count<10000)pending.Push(entry.FullName);else skipped++;continue;}
      seen++;
      try{var file=(FileInfo)entry;if(file.LastWriteTimeUtc<DateTime.UtcNow.AddDays(-7))rows.Add(new(file.FullName,file.Length,file.LastWriteTimeUtc,root.Category));}
      catch(IOException){skipped++;}catch(UnauthorizedAccessException){skipped++;}
      if(watch.ElapsedMilliseconds-reported>=200){reported=watch.ElapsedMilliseconds;progress?.Report(new("扫描 "+root.Category,seen,rows.Count));}
     }
    }catch(IOException){skipped++;}catch(UnauthorizedAccessException){skipped++;}
   }
  }
  return new(rows,skipped,seen,false,false);
 }
 public static DiskDeleteResult Delete(IEnumerable<DiskRow> rows,CancellationToken token=default,IProgress<OperationProgress>? progress=null)
 {
  long bytes=0;int deleted=0,skipped=0;
  foreach(var row in rows) try
  {
   if(token.IsCancellationRequested)return new(bytes,deleted,skipped,true);
   if(!Roots(true).Any(root=>Safe(row.Path,root.Path))) {skipped++;continue;}
   // Acquire exclusive handle, revalidate and delete through that same handle on close.
   using var handle=CreateFile(row.Path,0x80010000,0,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
   if(handle.IsInvalid) throw new IOException("文件正在使用或无权访问");
   using var stream=new FileStream(handle,FileAccess.Read);
   var info=new FileInfo(row.Path);
   if(info.Length!=row.Bytes || info.LastWriteTimeUtc!=row.Written || info.LastWriteTimeUtc>=DateTime.UtcNow.AddDays(-7))
   {

    throw new InvalidOperationException("文件在扫描后变化");
   }
   var actualBytes=stream.Length;
   var disposition=new Disposition {Delete=true};
   if(!SetFileInformationByHandle(stream.SafeFileHandle,4,ref disposition,(uint)Marshal.SizeOf<Disposition>())) throw new IOException("删除失败");
   bytes+=actualBytes;deleted++;if((deleted+skipped)%50==0)progress?.Report(new("删除所选临时文件",deleted+skipped,deleted));
  }catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException){skipped++;}
  return new(bytes,deleted,skipped,false);
 }
}
