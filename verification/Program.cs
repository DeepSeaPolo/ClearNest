using ClearNest.Services;
using Microsoft.Win32;

static void Check(bool condition,string message)
{
    if(!condition) throw new Exception(message);
    Console.WriteLine("PASS: "+message);
}
var folder=Path.Combine(@"C:\Windows\Temp","ClearNest-Test-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
DiskRow Make(string name,bool old=true)
{
    var file=Path.Combine(folder,name);File.WriteAllText(file,"test fixture");
    if(old)File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-10));
    var info=new FileInfo(file);return new(file,info.Length,info.LastWriteTimeUtc);
}
try
{
    var memory=MemoryService.Read();Check(memory.Total>0 && memory.Available<=memory.Total,"memory status is valid (no reclamation invoked)");
    var old=Make("old.tmp");var result=DiskService.Delete(new[]{old});
    Check(result.Deleted==1 && !File.Exists(old.Path),"selected old temporary file is deleted");
    var recent=Make("recent.tmp",false);result=DiskService.Delete(new[]{recent});
    Check(result.Deleted==0 && File.Exists(recent.Path),"recent file is retained");
    var changed=Make("changed.tmp");File.AppendAllText(changed.Path,"changed");result=DiskService.Delete(new[]{changed});
    Check(result.Deleted==0 && File.Exists(changed.Path),"changed file is retained");
    var locked=Make("locked.tmp");
    using(var stream=new FileStream(locked.Path,FileMode.Open,FileAccess.Read,FileShare.None))
    {
        result=DiskService.Delete(new[]{locked});
        Check(result.Deleted==0 && File.Exists(locked.Path),"file with active exclusive handle is retained");
    }
    var outside=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClearNest-Outside-"+Guid.NewGuid().ToString("N")+".tmp");
    try
    {
        File.WriteAllText(outside,"fixture");File.SetLastWriteTimeUtc(outside,DateTime.UtcNow.AddDays(-10));var info=new FileInfo(outside);
        result=DiskService.Delete(new[]{new DiskRow(outside,info.Length,info.LastWriteTimeUtc)});
        Check(result.Deleted==0 && File.Exists(outside),"file outside temporary directory allowlist is retained");
    }
    finally{File.Delete(outside);}
}
finally {Directory.Delete(folder,true);}

const string keyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
var valueName="ClearNest-Test-"+Guid.NewGuid().ToString("N");
var missingCommand="\"C:\\ClearNestMissing-"+Guid.NewGuid().ToString("N")+"\\missing.exe\"";
string? backup=null;
try
{
    using(var key=Registry.CurrentUser.CreateSubKey(keyPath,true))key.SetValue(valueName,missingCommand,RegistryValueKind.String);
    var selected=RegistryService.Scan().Where(x=>x.Name==valueName).ToList();
    Check(selected.Count==1,"missing quoted executable is a startup candidate");
    var cleaned=RegistryService.Clean(selected);backup=cleaned.Backup;
    using(var key=Registry.CurrentUser.OpenSubKey(keyPath))
        Check(cleaned.Deleted==1 && File.Exists(backup) && key?.GetValue(valueName)==null,"registry value is backed up before removal");
    var restored=RegistryService.Restore(backup);
    using(var key=Registry.CurrentUser.OpenSubKey(keyPath))
        Check(restored.Restored==1 && (string?)key?.GetValue(valueName)==missingCommand,"registry backup restores exact value");
    using(var key=Registry.CurrentUser.CreateSubKey(keyPath,true))key.SetValue(valueName,"existing value",RegistryValueKind.String);
    restored=RegistryService.Restore(backup);
    using(var key=Registry.CurrentUser.OpenSubKey(keyPath))
        Check(restored.Skipped==1 && (string?)key?.GetValue(valueName)=="existing value","restore does not overwrite existing value");
}
finally
{
    using(var key=Registry.CurrentUser.OpenSubKey(keyPath,true))key?.DeleteValue(valueName,false);
    if(backup!=null)File.Delete(backup);
}
Console.WriteLine("All cleanup checks passed. Only test fixtures were modified.");

var previousPreferences=File.Exists(Preferences.FilePath)?File.ReadAllBytes(Preferences.FilePath):null;
try
{
 var preferences=new Preferences {Theme="Dark",Accent="Teal",Mica=false,Resident=true,MiniOnTop=false,StartInMini=true,RefreshSeconds=10};
 preferences.Save();var loaded=Preferences.Load();
 Check(loaded.Theme=="Dark"&&loaded.Accent=="Teal"&&!loaded.Mica&&loaded.Resident&&!loaded.MiniOnTop&&loaded.StartInMini&&loaded.RefreshSeconds==10,"appearance and behavior settings persist");
 preferences.Theme="invalid";preferences.Accent="invalid";preferences.RefreshSeconds=-1;preferences.Save();loaded=Preferences.Load();
 Check(loaded.Theme=="System"&&loaded.Accent=="Indigo"&&loaded.RefreshSeconds==5,"invalid setting values fall back to supported defaults");
}
finally
{
 if(previousPreferences==null)File.Delete(Preferences.FilePath);else File.WriteAllBytes(Preferences.FilePath,previousPreferences);
}

var autoPreferences=new Preferences {AutoIntervalEnabled=true,AutoIntervalMinutes=15};
var policy=new AutoReclaimPolicy();var start=DateTimeOffset.UtcNow;policy.Configure(autoPreferences,start);
Check(policy.GetDue(autoPreferences,start.AddMinutes(14),40)==null,"scheduled cleanup waits for its interval");
Check(policy.GetDue(autoPreferences,start.AddMinutes(15),40)!=null,"scheduled cleanup becomes due without catch-up bursts");
Check(policy.GetDue(autoPreferences,start.AddMinutes(16),40)==null,"automatic cleanup respects the cooldown");
autoPreferences.AutoIntervalEnabled=false;autoPreferences.AutoThresholdEnabled=true;autoPreferences.AutoThresholdPercent=85;
policy.Configure(autoPreferences,start.AddMinutes(20));
Check(policy.GetDue(autoPreferences,start.AddMinutes(20),90)!=null,"high memory usage triggers threshold policy");
Check(policy.GetDue(autoPreferences,start.AddMinutes(30),90)==null,"sustained high usage does not repeatedly reclaim");
Check(policy.GetDue(autoPreferences,start.AddMinutes(31),79)==null,"threshold policy rearms after occupancy falls");
Check(policy.GetDue(autoPreferences,start.AddMinutes(32),86)!=null,"threshold policy can trigger after recovery and a new rise");
autoPreferences.AutoThresholdEnabled=false;
Check(policy.GetDue(autoPreferences,start.AddHours(2),99)==null,"disabled automatic policies never trigger");
