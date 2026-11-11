using System;
using System.IO;
using System.Text.Json;
namespace ClearNest.Services;
public sealed class Preferences
{
 public string Theme {get;set;}="System";
 public string Accent {get;set;}="Indigo";
 public bool Mica {get;set;}=true;
 public bool Resident {get;set;}
 public bool MiniOnTop {get;set;}=true;
 public bool StartInMini {get;set;}
 public int RefreshSeconds {get;set;}=5;
 public bool AutoIntervalEnabled {get;set;}
 public int AutoIntervalMinutes {get;set;}=15;
 public bool AutoThresholdEnabled {get;set;}
 public int AutoThresholdPercent {get;set;}=85;
 public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClearNest","preferences.json");
 public static Preferences Load()
 {
  if(!File.Exists(FilePath))
  {
   var old=Path.Combine(Path.GetDirectoryName(FilePath)!,"resident.txt");
   return new Preferences {Resident=File.Exists(old)&&File.ReadAllText(old)=="true"};
  }
  var result=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath))??new Preferences();
  if(result.Theme is not ("System" or "Light" or "Dark"))result.Theme="System";
  if(result.Accent is not ("Indigo" or "Purple" or "Teal" or "Rose" or "Amber"))result.Accent="Indigo";
  if(result.RefreshSeconds is not (2 or 5 or 10))result.RefreshSeconds=5;
  if(result.AutoIntervalMinutes<5||result.AutoIntervalMinutes>1440)result.AutoIntervalMinutes=15;
  if(result.AutoThresholdPercent<1||result.AutoThresholdPercent>99)result.AutoThresholdPercent=85;
  return result;
 }
 public void Save()
 {
  Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
  var temp=FilePath+".tmp";
  using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
  {JsonSerializer.Serialize(stream,this,new JsonSerializerOptions {WriteIndented=true});stream.Flush(true);}
  File.Move(temp,FilePath,true);
 }
}
