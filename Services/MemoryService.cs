using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace ClearNest.Services;
public static class MemoryService
{
 [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
 {
  public uint Length, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, Extended;
 }
 [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
 [StructLayout(LayoutKind.Sequential)] private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }
 [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
 [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
 [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
 [DllImport("advapi32.dll", SetLastError=true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
 [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);
 [DllImport("advapi32.dll", SetLastError=true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges privileges, uint length, out TokenPrivileges previous, out uint returned);
 [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int informationClass, ref int information, int length);
 public static (ulong Total, ulong Available, uint Load) Read()
 {
  var m = new MemoryStatus { Length=(uint)Marshal.SizeOf<MemoryStatus>() };
  if (!GlobalMemoryStatusEx(ref m)) throw new Win32Exception(Marshal.GetLastWin32Error());
  return (m.TotalPhys,m.AvailPhys,m.Load);
 }
 // Native, undocumented API: only MemoryPurgeStandbyList (4). Never EmptyWorkingSets (2),
 // FlushModifiedList (3), EmptyWorkingSet or SetProcessWorkingSetSize.
 public static void PurgeStandby()
 {
  if (!OpenProcessToken(GetCurrentProcess(),0x28,out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
  try
  {
   if (!LookupPrivilegeValue(null,"SeProfileSingleProcessPrivilege",out var luid)) throw new Win32Exception(Marshal.GetLastWin32Error());
   var desired=new TokenPrivileges {Count=1,Luid=luid,Attributes=2};
   if (!AdjustTokenPrivileges(token,false,ref desired,(uint)Marshal.SizeOf<TokenPrivileges>(),out var previous,out _) || Marshal.GetLastWin32Error()!=0)
    throw new InvalidOperationException("回收待机缓存需要管理员权限，请退出后以管理员身份运行。");
   try { int command=4; int status=NtSetSystemInformation(80,ref command,sizeof(int));
    if (status!=0) throw new InvalidOperationException($"系统拒绝待机缓存回收：NTSTATUS 0x{status:X8}。未使用其他清理方式。");
   }
   finally { AdjustTokenPrivileges(token,false,ref previous,0,out _,out _); }
  }
  finally { CloseHandle(token); }
 }
 public static string Size(ulong bytes) => $"{bytes/1073741824d:F2} GB";
}
