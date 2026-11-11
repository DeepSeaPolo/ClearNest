using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
namespace ClearNest;
public partial class App : Microsoft.UI.Xaml.Application
{
 private MainWindow? window;
 [DllImport("user32.dll", CharSet=CharSet.Unicode)]
 private static extern int MessageBox(IntPtr owner,string text,string caption,uint type);
 private static string WriteFailure(Exception error)
 {
  string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClearNest","Logs","startup.log");
  try
  {
   Directory.CreateDirectory(Path.GetDirectoryName(path)!);
   File.AppendAllText(path,$"{DateTimeOffset.Now:O}\nVersion: {typeof(App).Assembly.GetName().Version}\nExecutable: {Environment.ProcessPath}\nOS: {Environment.OSVersion}\n{error}\n\n");
   return path;
  }
  catch { return "日志无法写入用户配置目录。"; }
 }
 private static void ReportFailure(Exception error)
 {
  var log=WriteFailure(error);
  MessageBox(IntPtr.Zero,$"ClearNest 启动失败。\n{error.Message}\n\n诊断日志：{log}","ClearNest",0x10);
 }
 public App()
 {
  AppDomain.CurrentDomain.UnhandledException+=(_,args)=>{if(args.ExceptionObject is Exception error)WriteFailure(error);};
  UnhandledException+=(_,args)=>WriteFailure(args.Exception);
  try { InitializeComponent(); }
  catch(Exception error) { ReportFailure(error);throw; }
 }
 protected override void OnLaunched(LaunchActivatedEventArgs args)
 {
  try { window=new MainWindow();window.Activate();window.ApplyStartupMode(); }
  catch(Exception error) { ReportFailure(error);Exit(); }
 }
}
