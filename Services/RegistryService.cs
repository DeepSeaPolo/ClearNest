using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace ClearNest.Services;
public record RegistryRow(string Name,string Command,int Kind)
{
 public string Display => $"{Name}  ·  HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\n{Command}";
}
public static class RegistryService
{
 private const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run";
 public static string BackupDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClearNest","RegistryBackups");
 private static bool Missing(string command)
 {
  var expanded=Environment.ExpandEnvironmentVariables(command.Trim());
  // Only quoted, absolute C: executable paths. Ambiguous/unquoted/UNC commands are skipped.
  var m=Regex.Match(expanded,"^\"(C:\\\\[^\"]+\\.exe)\"(?:\\s.*)?$",RegexOptions.IgnoreCase);
  if(!m.Success) return false;
  var file=m.Groups[1].Value;
  try { File.GetAttributes(file); return false; }
  catch(FileNotFoundException){return true;}
  catch(DirectoryNotFoundException){return true;}
  catch {return false;}
 }
 public static List<RegistryRow> Scan()
 {
  var rows=new List<RegistryRow>();using var key=Registry.CurrentUser.OpenSubKey(Key);
  if(key==null)return rows;
  foreach(var name in key.GetValueNames())
  {
   var kind=key.GetValueKind(name);
   if(kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) continue;
   if(key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) is string command && Missing(command)) rows.Add(new(name,command,(int)kind));
  }
  return rows;
 }
 public static (int Deleted,int Skipped,string Backup) Clean(List<RegistryRow> selected)
 {
  Directory.CreateDirectory(BackupDirectory);
  var backup=Path.Combine(BackupDirectory,$"run-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
  // Persist selected values before the first mutation. Abort if backup cannot be written.
  using(var f=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None))
  {JsonSerializer.Serialize(f,selected);f.Flush(true);}
  using var key=Registry.CurrentUser.OpenSubKey(Key,true);
  int deleted=0,skipped=0;
  foreach(var row in selected)
  {
   try
   {
    if(key==null || key.GetValue(row.Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) is not string current || current!=row.Command || (int)key.GetValueKind(row.Name)!=row.Kind || !Missing(current)) {skipped++;continue;}
    key.DeleteValue(row.Name,false);deleted++;
   }catch(UnauthorizedAccessException){skipped++;}catch(IOException){skipped++;}
  }
  return(deleted,skipped,backup);
 }
 public static (int Restored,int Skipped) Restore(string backup)
 {
  var rows=JsonSerializer.Deserialize<List<RegistryRow>>(File.ReadAllText(backup)) ?? throw new IOException("备份格式无效");
  using var key=Registry.CurrentUser.CreateSubKey(Key,true);int restored=0,skipped=0;
  foreach(var row in rows)
  {
   if(row.Kind is not (1 or 2) || Array.Exists(key.GetValueNames(),x=>string.Equals(x,row.Name,StringComparison.OrdinalIgnoreCase))) {skipped++;continue;}
   key.SetValue(row.Name,row.Command,(RegistryValueKind)row.Kind);restored++;
  }
  return(restored,skipped);
 }
}
