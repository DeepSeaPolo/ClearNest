using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using ClearNest.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
namespace ClearNest;
public sealed partial class MainWindow : Window
{
 private readonly DispatcherTimer timer=new();
 private readonly TrayService tray;
 private MiniWindow? mini; private bool exiting,busy,miniMode;
 private readonly string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClearNest","resident.txt");
 public MainWindow()
 {
  InitializeComponent();Title="ClearNest · 清巢";SystemBackdrop=new Microsoft.UI.Xaml.Media.MicaBackdrop();AppWindow.Resize(new SizeInt32(1100,820));Navigation.SelectedItem=Navigation.MenuItems[0];
  tray=new TrayService(ShowMain,ShowMini,Exit);
  try{Resident.IsOn=File.Exists(settings)&&File.ReadAllText(settings)=="true";}catch(Exception){Message("无法读取常驻设置，默认关闭。");}
  AppWindow.Closing+=(_,args)=>{if(!exiting&&(busy||Resident.IsOn)){args.Cancel=true;if(!busy)AppWindow.Hide();else Message("正在操作，请等待完成后关闭。");}};
  Closed+=(_,_)=>{exiting=true;timer.Stop();tray.Dispose();mini?.Close();Microsoft.UI.Xaml.Application.Current.Exit();};
  timer.Interval=TimeSpan.FromSeconds(5);timer.Tick+=(_,_)=>Refresh();timer.Start();Refresh();
 }
 private void Refresh()
 {
  try{var m=MemoryService.Read();MemoryPercent.Text=$"{m.Load}%";AvailableText.Text=MemoryService.Size(m.Available);MemoryText.Text=$"总内存 {MemoryService.Size(m.Total)} · 每 5 秒更新";MemoryBar.Value=m.Load;mini?.Update(m.Load,MemoryService.Size(m.Available));}
  catch(Exception e){Message(e.Message,true);}
 }
 private void Message(string text,bool error=false){mini?.Report(text,error);Status.Message=text;Status.Severity=error?InfoBarSeverity.Error:InfoBarSeverity.Informational;}
 private async Task Run(Func<Task> work)
 {
  if(busy){Message("已有操作正在执行。");return;}busy=true;
  try{await work();}catch(Exception e){Message(e.Message,true);}finally{busy=false;Refresh();}
 }
 private async Task<bool> Confirm(string title,string text)
 {
  var owner=miniMode&&mini!=null?mini.Content:Content;var dialog=new ContentDialog {Title=title,Content=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap},PrimaryButtonText="确认执行",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close,XamlRoot=owner.XamlRoot};
  return await dialog.ShowAsync()==ContentDialogResult.Primary;
 }
 private void Navigation_SelectionChanged(NavigationView sender,NavigationViewSelectionChangedEventArgs args)
 {
  if(MemoryPage==null||args.SelectedItem is not NavigationViewItem item)return;
  var tag=item.Tag as string;
  MemoryPage.Visibility=tag=="memory"?Visibility.Visible:Visibility.Collapsed;
  DiskPage.Visibility=tag=="disk"?Visibility.Visible:Visibility.Collapsed;
  RegistryPage.Visibility=tag=="registry"?Visibility.Visible:Visibility.Collapsed;
  SettingsPage.Visibility=tag=="settings"?Visibility.Visible:Visibility.Collapsed;
  sender.Header=item.Content;
 }
 private void ShowMain()
 {
  if(busy&&miniMode)return;
  mini?.AppWindow.Hide();miniMode=false;
  AppWindow.Show();
  if(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)presenter.Restore();
  Activate();
 }
 private void ShowMini()
 {
  if(busy){Message("请等待当前操作完成后再切换窗口。");return;}
  if(mini==null)
  {
   mini=new MiniWindow(ShowMain,()=>Memory_Click(this,new RoutedEventArgs()));
   mini.AppWindow.Closing+=(_,args)=>
   {
    if(exiting)return;
    args.Cancel=true;
    if(busy)return;
    if(Resident.IsOn){mini?.AppWindow.Hide();miniMode=false;}
    else ShowMain();
   };
  }
  miniMode=true;Refresh();AppWindow.Hide();mini.AppWindow.Show();mini.Activate();
 }
 private void Mini_Click(object sender,RoutedEventArgs e)=>ShowMini();
 private async void Memory_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  if(!await Confirm("回收待机缓存","仅清空已经离开工作集的未修改待机页，不触碰正在使用的进程工作集。待机缓存本就可用，清空后可能出现重新读取磁盘的开销。继续？"))return;
  await Task.Run(MemoryService.PurgeStandby);Message("已提交待机缓存回收。可用内存可能没有明显变化，未将波动计为释放成果。");
 });
 private async void Processes_Click(object sender,RoutedEventArgs e)=>await Run(async()=>{Message("正在采样进程…");Processes.ItemsSource=await ProcessService.Scan();Message("进程列表已更新；没有自动结束任何进程。");});
 private void TaskManager_Click(object sender,RoutedEventArgs e){try{Process.Start(new ProcessStartInfo("taskmgr.exe"){UseShellExecute=true});}catch(Exception ex){Message(ex.Message,true);}}
 private async void DiskScan_Click(object sender,RoutedEventArgs e)=>await Run(async()=>{Message("正在扫描临时文件…");var result=await Task.Run(DiskService.Scan);DiskList.ItemsSource=result.Rows;Message($"{result.Rows.Count} 个候选文件，共 {result.Rows.Sum(x=>x.Bytes)/1048576d:F1} MB；跳过 {result.Skipped} 项。最多列出 20,000 项。");});
 private async void DiskClean_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  var selected=DiskList.SelectedItems.Cast<DiskRow>().ToList();if(selected.Count==0){Message("请先勾选文件。");return;}
  if(!await Confirm("永久删除临时文件",$"删除所选 {selected.Count} 个文件（{selected.Sum(x=>x.Bytes)/1048576d:F1} MB），不经过回收站。占用文件和扫描后变化的文件会跳过。"))return;
  var r=await Task.Run(()=>DiskService.Delete(selected));DiskList.ItemsSource=null;Message($"删除 {r.Deleted} 个文件，{r.Bytes/1048576d:F1} MB；跳过 {r.Skipped} 个。重新扫描可刷新列表。");
 });
 private async void RegistryScan_Click(object sender,RoutedEventArgs e)=>await Run(async()=>{var rows=await Task.Run(RegistryService.Scan);RegistryList.ItemsSource=rows;Message($"发现 {rows.Count} 个目标缺失启动项，请核对后选择。");});
 private async void RegistryClean_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  var rows=RegistryList.SelectedItems.Cast<RegistryRow>().ToList();if(rows.Count==0){Message("请先勾选注册表条目。");return;}
  if(!await Confirm("备份并删除启动项",$"删除所选 {rows.Count} 项。先保存 JSON 备份，备份失败则不删除。"))return;
  var r=await Task.Run(()=>RegistryService.Clean(rows));RegistryList.ItemsSource=null;Message($"删除 {r.Deleted} 项，跳过 {r.Skipped} 项。备份：{r.Backup}");
 });
 private async void Restore_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  Directory.CreateDirectory(RegistryService.BackupDirectory);
  var picker=new Windows.Storage.Pickers.FileOpenPicker();
  WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
  picker.FileTypeFilter.Add(".json");
  var file=await picker.PickSingleFileAsync();if(file==null)return;
  if(!await Confirm("恢复启动项","仅使用你信任的 ClearNest 备份。恢复会重新启用其中的启动命令，同名现有值不会覆盖。"))return;
  var r=await Task.Run(()=>RegistryService.Restore(file.Path));Message($"恢复 {r.Restored} 项，跳过 {r.Skipped} 项。");
 });
 private void Resident_Toggled(object sender,RoutedEventArgs e)
 {if(settings==null)return;try{Directory.CreateDirectory(Path.GetDirectoryName(settings)!);File.WriteAllText(settings,Resident.IsOn?"true":"false");}catch(Exception ex){Message(ex.Message,true);}}
 private void Exit_Click(object sender,RoutedEventArgs e)=>Exit();
 private void Exit(){if(busy){Message("正在操作，请等待完成后退出。");return;}exiting=true;Close();}
}
