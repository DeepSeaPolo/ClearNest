using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;
namespace ClearNest.Services;
public record StartupRow(string Name,string Command,string Source,string State,bool Managed,bool Disabled,int Kind)
{
 public string Display=>$"{Name} · {State}\n{Source}\n{Command}";
}
public static class StartupService
{
 private const string Run=@"Software\Microsoft\Windows\CurrentVersion\Run";
 private const string Disabled=@"Software\ClearNest\DisabledStartup";
 private static string BackupDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClearNest","StartupBackups");
 public static List<StartupRow> Scan(CancellationToken token=default)
 {
  var rows=new List<StartupRow>();
  foreach(var entry in new[]{(RegistryHive.CurrentUser,RegistryView.Registry64),(RegistryHive.LocalMachine,RegistryView.Registry64),(RegistryHive.LocalMachine,RegistryView.Registry32)})
  {
   token.ThrowIfCancellationRequested();
   try
   {
    using var hive=RegistryKey.OpenBaseKey(entry.Item1,entry.Item2);using var key=hive.OpenSubKey(Run);if(key==null)continue;
    using var approved=hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
    foreach(var name in key.GetValueNames())
    {
     token.ThrowIfCancellationRequested();var kind=key.GetValueKind(name);if(kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))continue;
     if(key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) is not string command)continue;
     var state=approved?.GetValue(name) is byte[] status&&status.Length>=4?status[0] switch {2 or 6=>"Windows 标记为启用",3 or 7=>"Windows 标记为禁用",_=>"Run 键存在；Windows 状态未解析"}:"Run 键存在；Windows 状态未知";
     rows.Add(new(name,command,$"{entry.Item1} / {entry.Item2} / Run",state,entry.Item1==RegistryHive.CurrentUser,false,(int)kind));
    }
   }catch(UnauthorizedAccessException){}catch(IOException){}
  }
  using(var key=Registry.CurrentUser.OpenSubKey(Disabled))if(key!=null)foreach(var name in key.GetValueNames())
  {
   token.ThrowIfCancellationRequested();if(key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) is string command)rows.Add(new(name,command,"ClearNest / 当前用户 Run 备份","已由 ClearNest 停用",true,true,(int)key.GetValueKind(name)));
  }
  foreach(var location in new[]{Environment.SpecialFolder.Startup,Environment.SpecialFolder.CommonStartup})
  {
   token.ThrowIfCancellationRequested();var folder=Environment.GetFolderPath(location);if(!Directory.Exists(folder))continue;
   try{foreach(var file in Directory.EnumerateFiles(folder)){token.ThrowIfCancellationRequested();if(Path.GetFileName(file).Equals("desktop.ini",StringComparison.OrdinalIgnoreCase))continue;rows.Add(new(Path.GetFileName(file),file,"启动文件夹","只读；请使用 Windows 启动设置",false,false,0));}}catch(UnauthorizedAccessException){}catch(IOException){}
  }
  return rows.OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
 }
 public static string SetDisabled(StartupRow item,bool disable)
 {
  if(!item.Managed||item.Disabled==disable||item.Kind is not (1 or 2))throw new InvalidOperationException("仅管理当前用户 Run 项及本工具保留的停用项。");
  using var hive=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64);
  using var source=hive.OpenSubKey(disable?Run:Disabled,true)??throw new IOException("启动项来源已不存在");
  var current=source.GetValue(item.Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
  if(current is not string command||command!=item.Command||(int)source.GetValueKind(item.Name)!=item.Kind)throw new IOException("启动项在扫描后发生变化，请重新扫描。");
  using var destination=hive.CreateSubKey(disable?Disabled:Run,true);
  if(destination.GetValueNames().Contains(item.Name,StringComparer.OrdinalIgnoreCase))throw new IOException("目标已有同名值，未覆盖任何数据。");
  Directory.CreateDirectory(BackupDirectory);var backup=Path.Combine(BackupDirectory,$"startup-{Guid.NewGuid():N}.json");
  using(var file=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(file,item);file.Flush(true);}
  destination.SetValue(item.Name,command,(RegistryValueKind)item.Kind);
  if(source.GetValue(item.Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) is not string again||again!=command)
   throw new IOException("源值发生变化，保留了备份副本，请重新检查。");
  source.DeleteValue(item.Name,false);return backup;
 }
}
