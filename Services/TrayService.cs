using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace ClearNest.Services;
// Native Win32 notification icon; all callbacks run on the WinUI UI thread.
public sealed class TrayService : IDisposable
{
 private readonly WindowProcedure procedure;
 private readonly Action show,mini,exit;
 private readonly string className="ClearNestTray_"+Guid.NewGuid().ToString("N");
 private readonly IntPtr instance;
 private IntPtr window;
 private NotifyIconData data;
 private readonly uint taskbarCreated;
 private const uint Callback=0x8001;
 private delegate IntPtr WindowProcedure(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct WindowClass
 {
  public uint Size,Style; public WindowProcedure Procedure; public int ClassExtra,WindowExtra;
  public IntPtr Instance,Icon,Cursor,Background; public string? MenuName; public string ClassName; public IntPtr SmallIcon;
 }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct NotifyIconData
 {
  public uint Size;public IntPtr Window;public uint Id,Flags,CallbackMessage;public IntPtr Icon;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Tip;
  public uint State,StateMask;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Info;
  public uint Timeout;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string InfoTitle;
  public uint InfoFlags;public Guid Guid;public IntPtr BalloonIcon;
 }
 [StructLayout(LayoutKind.Sequential)] private struct Point {public int X,Y;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern ushort RegisterClassEx(ref WindowClass definition);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateWindowEx(uint extendedStyle,string className,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool UnregisterClass(string name,IntPtr instance);
 [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
 [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance,IntPtr resource);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint action,ref NotifyIconData data);
 [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu,uint flags,UIntPtr id,string text);
 [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu,uint flags,int x,int y,int reserved,IntPtr hwnd,IntPtr rect);
 [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
 [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
 public TrayService(Action show,Action mini,Action exit)
 {
  this.show=show;this.mini=mini;this.exit=exit;procedure=ProcessMessage;instance=GetModuleHandle(null);
  var definition=new WindowClass {Size=(uint)Marshal.SizeOf<WindowClass>(),Procedure=procedure,Instance=instance,ClassName=className};
  if(RegisterClassEx(ref definition)==0)throw new Win32Exception(Marshal.GetLastWin32Error());
  window=CreateWindowEx(0x80,className,"ClearNest Tray",0,0,0,0,0,IntPtr.Zero,IntPtr.Zero,instance,IntPtr.Zero);
  if(window==IntPtr.Zero){UnregisterClass(className,instance);throw new Win32Exception(Marshal.GetLastWin32Error());}
  data=new NotifyIconData {Size=(uint)Marshal.SizeOf<NotifyIconData>(),Window=window,Id=1,Flags=7,CallbackMessage=Callback,Icon=LoadIcon(IntPtr.Zero,new IntPtr(32512)),Tip="ClearNest · 清巢",Info="",InfoTitle=""};
  taskbarCreated=RegisterWindowMessage("TaskbarCreated");
  if(!Shell_NotifyIcon(0,ref data)){Dispose();throw new InvalidOperationException("无法创建托盘图标。");}
 }
 private IntPtr ProcessMessage(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam)
 {
  if(taskbarCreated!=0&&message==taskbarCreated)Shell_NotifyIcon(0,ref data);
  if(message==Callback)
  {
   if(lparam.ToInt64()==0x203)show();
   if(lparam.ToInt64()==0x205)
   {
    var menu=CreatePopupMenu();uint selected=0;
    try
    {
     AppendMenu(menu,0,new UIntPtr(1),"打开主窗口");AppendMenu(menu,0,new UIntPtr(2),"迷你窗口");AppendMenu(menu,0,new UIntPtr(3),"退出");
     GetCursorPos(out var point);SetForegroundWindow(hwnd);
     selected=TrackPopupMenu(menu,0x100|0x2,point.X,point.Y,0,hwnd,IntPtr.Zero);PostMessage(hwnd,0,IntPtr.Zero,IntPtr.Zero);
    }
    finally{DestroyMenu(menu);}
    if(selected==1)show();if(selected==2)mini();if(selected==3)exit();
   }
   return IntPtr.Zero;
  }
  return DefWindowProc(hwnd,message,wparam,lparam);
 }
 public void Dispose()
 {
  if(window==IntPtr.Zero)return;
  Shell_NotifyIcon(2,ref data);DestroyWindow(window);window=IntPtr.Zero;UnregisterClass(className,instance);
  GC.KeepAlive(procedure);
 }
}
