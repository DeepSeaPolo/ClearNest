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
 private Preferences preferences=new();
 private bool initializing=true;
 public MainWindow()
 {
  InitializeComponent();Title="ClearNest · 清巢";AppWindow.Resize(new SizeInt32(1120,880));
  ExtendsContentIntoTitleBar=true;SetTitleBar(TitleRegion);
  Root.ActualThemeChanged+=(_,_)=>UpdateCaption();
  try{preferences=Preferences.Load();}catch(Exception){Message("无法读取设置，已使用默认外观。");}
  ThemeSystem.IsChecked=preferences.Theme=="System";ThemeLight.IsChecked=preferences.Theme=="Light";ThemeDark.IsChecked=preferences.Theme=="Dark";
  AccentChoice.SelectedIndex=Array.IndexOf(new[]{"Indigo","Purple","Teal","Rose","Amber"},preferences.Accent);
  Resident.IsOn=DashboardResident.IsOn=preferences.Resident;MicaChoice.IsOn=preferences.Mica;
  MiniTopChoice.IsOn=preferences.MiniOnTop;StartMiniChoice.IsOn=preferences.StartInMini;
  RefreshChoice.SelectedIndex=Array.IndexOf(new[]{2,5,10},preferences.RefreshSeconds);
  Navigation.SelectedItem=Navigation.MenuItems[0];initializing=false;ApplyAppearance();
  tray=new TrayService(ShowMain,ShowMini,Exit);
  AppWindow.Closing+=(_,args)=>{if(!exiting&&(busy||Resident.IsOn)){args.Cancel=true;if(!busy)AppWindow.Hide();else Message("正在操作，请等待完成后关闭。");}};
  Closed+=(_,_)=>{exiting=true;timer.Stop();tray.Dispose();mini?.Close();Microsoft.UI.Xaml.Application.Current.Exit();};
  timer.Interval=TimeSpan.FromSeconds(preferences.RefreshSeconds);timer.Tick+=(_,_)=>Refresh();timer.Start();Refresh();
 }
 private void Refresh()
 {
  try{var m=MemoryService.Read();MemoryPercent.Text=$"{m.Load}%";AvailableText.Text=MemoryService.Size(m.Available);MemoryText.Text=$"总内存 {MemoryService.Size(m.Total)} · 每 {preferences.RefreshSeconds} 秒更新";MemoryBar.Value=m.Load;mini?.Update(m.Load,MemoryService.Size(m.Available));}
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
  if(HomePage==null||args.SelectedItem is not NavigationViewItem item)return;
  var tag=item.Tag as string;
  HomePage.Visibility=tag=="home"?Visibility.Visible:Visibility.Collapsed;
  ProcessPage.Visibility=tag=="process"?Visibility.Visible:Visibility.Collapsed;
  DiskPage.Visibility=tag=="disk"?Visibility.Visible:Visibility.Collapsed;
  RegistryPage.Visibility=tag=="registry"?Visibility.Visible:Visibility.Collapsed;
  SettingsPage.Visibility=tag=="settings"?Visibility.Visible:Visibility.Collapsed;
  sender.Header=item.Content;
 }
 private void OpenPage_Click(object sender,RoutedEventArgs e)
 {
  if(sender is not Button button)return;
  foreach(var entry in Navigation.MenuItems)
   if(entry is NavigationViewItem item&&Equals(item.Tag,button.Tag)){Navigation.SelectedItem=item;return;}
 }
 private void Dashboard_SizeChanged(object sender,SizeChangedEventArgs e)
 {
  if(RegistryCard==null)return;
  bool narrow=e.NewSize.Width<640;
  bool medium=e.NewSize.Width<940;
  Grid.SetColumn(AvailableCard,narrow?0:1);Grid.SetRow(AvailableCard,narrow?1:0);
  Grid.SetColumnSpan(AvailableCard,narrow?2:1);
  Grid.SetColumnSpan((FrameworkElement)MemoryCards.Children[0],narrow?2:1);
  ActionCards.ColumnDefinitions[2].Width=medium?new GridLength(0):new GridLength(1,GridUnitType.Star);
  Grid.SetColumn(RegistryCard,narrow?0:1);Grid.SetRow(RegistryCard,narrow?1:0);Grid.SetColumnSpan(RegistryCard,narrow?2:1);
  Grid.SetColumnSpan((FrameworkElement)ActionCards.Children[0],narrow?2:1);
  Grid.SetRow(ProcessCard,narrow?2:medium?1:0);Grid.SetColumn(ProcessCard,medium?0:2);Grid.SetColumnSpan(ProcessCard,medium?2:1);
 }
 public void ApplyStartupMode(){if(preferences.StartInMini)ShowMini();}
 private void PersistPreferences(){try{preferences.Save();}catch(Exception ex){Message("设置未能保存："+ex.Message,true);}}
 private ElementTheme PreferredTheme => preferences.Theme switch {"Dark"=>ElementTheme.Dark,"Light"=>ElementTheme.Light,_=>ElementTheme.Default};
 private void ApplyAppearance()
 {
  Root.RequestedTheme=PreferredTheme;
  var hex=preferences.Accent switch {"Purple"=>0x7C3AEDu,"Teal"=>0x007D76u,"Rose"=>0xB82F61u,"Amber"=>0xA65C00u,_=>0x4F6BEDu};
  var brush=(Microsoft.UI.Xaml.Media.SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["AppAccentBrush"];
  brush.Color=Windows.UI.Color.FromArgb(255,(byte)(hex>>16),(byte)(hex>>8),(byte)hex);
  SystemBackdrop=preferences.Mica?new Microsoft.UI.Xaml.Media.MicaBackdrop():null;
  mini?.ApplyAppearance(PreferredTheme,preferences.Mica,preferences.MiniOnTop);
  UpdateCaption();
 }
 private void UpdateCaption()
 {
  var dark=Root.ActualTheme==ElementTheme.Dark;
  Root.Background=preferences.Mica?null:new Microsoft.UI.Xaml.Media.SolidColorBrush(dark?Windows.UI.Color.FromArgb(255,28,29,33):Windows.UI.Color.FromArgb(255,246,247,250));
  AppWindow.TitleBar.ButtonBackgroundColor=Microsoft.UI.Colors.Transparent;
  AppWindow.TitleBar.ButtonInactiveBackgroundColor=Microsoft.UI.Colors.Transparent;
  AppWindow.TitleBar.ButtonForegroundColor=dark?Microsoft.UI.Colors.White:Microsoft.UI.Colors.Black;
 }
 private void Theme_Checked(object sender,RoutedEventArgs e)
 {
  if(initializing||sender is not RadioButton radio)return;
  preferences.Theme=(string)radio.Tag;ApplyAppearance();PersistPreferences();
 }
 private void Appearance_Changed(object sender,RoutedEventArgs e)
 {
  if(initializing)return;
  if(AccentChoice.SelectedItem is ComboBoxItem item)preferences.Accent=(string)item.Tag;
  preferences.Mica=MicaChoice.IsOn;ApplyAppearance();PersistPreferences();
 }
 private void Behavior_Changed(object sender,RoutedEventArgs e)
 {
  if(initializing)return;
  preferences.MiniOnTop=MiniTopChoice.IsOn;preferences.StartInMini=StartMiniChoice.IsOn;
  if(RefreshChoice.SelectedItem is ComboBoxItem item)preferences.RefreshSeconds=int.Parse((string)item.Tag);
  timer.Interval=TimeSpan.FromSeconds(preferences.RefreshSeconds);ApplyAppearance();PersistPreferences();Refresh();
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
   mini.ApplyAppearance(PreferredTheme,preferences.Mica,preferences.MiniOnTop);
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
 private async void Processes_Click(object sender,RoutedEventArgs e)=>await Run(async()=>{Message("正在采样进程…");var rows=await ProcessService.Scan();Processes.ItemsSource=rows;ProcessSummary.Text=rows.Count==0?"未读取到可访问进程。":string.Join(" · ",rows.Take(3).Select(x=>$"{x.Name} {x.Bytes/1048576d:F0} MB"));Message("进程列表已更新；没有自动结束任何进程。");});
 private void TaskManager_Click(object sender,RoutedEventArgs e){try{Process.Start(new ProcessStartInfo("taskmgr.exe"){UseShellExecute=true});}catch(Exception ex){Message(ex.Message,true);}}
 private async void DiskScan_Click(object sender,RoutedEventArgs e)=>await Run(async()=>{Message("正在扫描临时文件…");var result=await Task.Run(DiskService.Scan);DiskList.ItemsSource=result.Rows;DiskSummary.Text=$"{result.Rows.Count} 项 · {result.Rows.Sum(x=>x.Bytes)/1048576d:F1} MB";Message($"{result.Rows.Count} 个候选文件，共 {result.Rows.Sum(x=>x.Bytes)/1048576d:F1} MB；跳过 {result.Skipped} 项。最多列出 20,000 项。");});
 private async void DiskClean_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  var selected=DiskList.SelectedItems.Cast<DiskRow>().ToList();if(selected.Count==0){Message("请先勾选文件。");return;}
  if(!await Confirm("永久删除临时文件",$"删除所选 {selected.Count} 个文件（{selected.Sum(x=>x.Bytes)/1048576d:F1} MB），不经过回收站。占用文件和扫描后变化的文件会跳过。"))return;
  var r=await Task.Run(()=>DiskService.Delete(selected));DiskList.ItemsSource=null;DiskSummary.Text="清理完成 · 请重新扫描";Message($"删除 {r.Deleted} 个文件，{r.Bytes/1048576d:F1} MB；跳过 {r.Skipped} 个。重新扫描可刷新列表。");
 });
 private async void RegistryScan_Click(object sender,RoutedEventArgs e)=>await Run(async()=>{var rows=await Task.Run(RegistryService.Scan);RegistryList.ItemsSource=rows;RegistrySummary.Text=$"{rows.Count} 项待核对";Message($"发现 {rows.Count} 个目标缺失启动项，请核对后选择。");});
 private async void RegistryClean_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  var rows=RegistryList.SelectedItems.Cast<RegistryRow>().ToList();if(rows.Count==0){Message("请先勾选注册表条目。");return;}
  if(!await Confirm("备份并删除启动项",$"删除所选 {rows.Count} 项。先保存 JSON 备份，备份失败则不删除。"))return;
  var r=await Task.Run(()=>RegistryService.Clean(rows));RegistryList.ItemsSource=null;RegistrySummary.Text="清理完成 · 请重新检查";Message($"删除 {r.Deleted} 项，跳过 {r.Skipped} 项。备份：{r.Backup}");
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
 {
  if(initializing||sender is not ToggleSwitch toggle)return;
  preferences.Resident=toggle.IsOn;initializing=true;
  Resident.IsOn=DashboardResident.IsOn=preferences.Resident;initializing=false;PersistPreferences();
 }
 private void Exit_Click(object sender,RoutedEventArgs e)=>Exit();
 private void Exit(){if(busy){Message("正在操作，请等待完成后退出。");return;}exiting=true;Close();}
}
