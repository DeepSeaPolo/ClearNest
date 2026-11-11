using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace ClearNest.Services;
public record DiskRow(string Path,long Bytes,DateTime Written)
{
 public string Display => $"{Bytes/1048576d:F2} MB  ·  {Path}";
}
public static class DiskService
{
 // Only allow-listed temporary files on C:, at least seven days old. Never follow junctions.
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [StructLayout(LayoutKind.Sequential)] private struct Disposition { [MarshalAs(UnmanagedType.U1)] public bool Delete; }
 [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle,int fileClass,ref Disposition info,uint size);
 private static string[] Roots => new[]{System.IO.Path.GetTempPath(),@"C:\Windows\Temp"}
  .Select(System.IO.Path.GetFullPath).Where(x=>x.StartsWith(@"C:\",StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
 private static bool Safe(string path,string root)
 {
  var full=System.IO.Path.GetFullPath(path); var boundary=root.TrimEnd('\\')+"\\";
  if(!full.StartsWith(boundary,StringComparison.OrdinalIgnoreCase)) return false;
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
 public static (List<DiskRow> Rows,int Skipped) Scan()
 {
  var rows=new List<DiskRow>(); int skipped=0;
  foreach(var root in Roots)
  {
   var queue=new Stack<string>();queue.Push(root);
   while(queue.Count>0 && rows.Count<20000)
   {
    var dir=queue.Pop();
    try
    {
     if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0) {skipped++;continue;}
     foreach(var child in Directory.GetDirectories(dir))
      if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0) queue.Push(child);
     foreach(var file in Directory.GetFiles(dir)) try
     {
      var info=new FileInfo(file);
      if(Safe(file,root)&&info.LastWriteTimeUtc<DateTime.UtcNow.AddDays(-7)) rows.Add(new(file,info.Length,info.LastWriteTimeUtc));
     }catch(IOException){skipped++;}catch(UnauthorizedAccessException){skipped++;}
    }catch(IOException){skipped++;}catch(UnauthorizedAccessException){skipped++;}
   }
  }
  return(rows,skipped);
 }
 public static (long Bytes,int Deleted,int Skipped) Delete(IEnumerable<DiskRow> rows)
 {
  long bytes=0;int deleted=0,skipped=0;
  foreach(var row in rows) try
  {
   if(!Roots.Any(root=>Safe(row.Path,root))) {skipped++;continue;}
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
   bytes+=actualBytes;deleted++;
  }catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException){skipped++;}
  return(bytes,deleted,skipped);
 }
}
