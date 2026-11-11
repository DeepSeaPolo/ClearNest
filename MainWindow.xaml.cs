using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Text.Json;
using ClearNest.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
namespace ClearNest;
public sealed partial class MainWindow : Window
{
 private readonly DispatcherTimer timer=new();
 private readonly MemoryTimeline memoryTimeline=new();
 private readonly TrayService tray;
 private MiniWindow? mini; private bool exiting,busy,miniMode;
 private Preferences preferences=new();
 private readonly AutoReclaimPolicy autoPolicy=new();
 private bool autoPaused;
 private double latestLoad;
 private static bool IsAdministrator
 {
  get{using var identity=System.Security.Principal.WindowsIdentity.GetCurrent();return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);}
 }
 private bool initializing=true,dialogOpen,exitAfterWork;
 private CancellationTokenSource? operationCancellation;
 private ContentDialog? activeDialog;
 private readonly SemaphoreSlim preferencesGate=new(1,1);
 private long preferencesRevision;
 private Task pendingPreferences=Task.CompletedTask;
 private int operationId,diskPage;
 private List<DiskRow> diskRows=new();
 private List<ProcessRow> processRows=new();
 private CancellationToken CurrentToken=>operationCancellation?.Token??CancellationToken.None;
 private IProgress<OperationProgress> ProgressForOperation()
 {
  int id=operationId;
  return new Progress<OperationProgress>(p=>{if(id==operationId&&busy&&!CurrentToken.IsCancellationRequested)OperationText.Text=$"{p.Phase} · 已检查 {p.Seen} · 候选 {p.Candidates}";});
 }

 public MainWindow()
 {
  InitializeComponent();MemoryPanel.Bind(memoryTimeline);Title="ClearNest · 清巢";AppWindow.Resize(new SizeInt32(1120,880));
  ExtendsContentIntoTitleBar=true;SetTitleBar(TitleRegion);AppIconService.ApplyTo(this);TitleAppIcon.Source=AppIconService.CreateImage();
  Root.ActualThemeChanged+=(_,_)=>UpdateCaption();
  Root.Loaded+=(_,_)=>{UpdateWindowMinimum();Root.XamlRoot.Changed+=(_,_)=>UpdateWindowMinimum();};
  try{preferences=Preferences.Load();}catch(Exception){Message("无法读取设置，已使用默认外观。");}
  ThemeSystem.IsChecked=preferences.Theme=="System";ThemeLight.IsChecked=preferences.Theme=="Light";ThemeDark.IsChecked=preferences.Theme=="Dark";
  AccentChoice.SelectedIndex=Array.IndexOf(new[]{"Indigo","Purple","Teal","Rose","Amber"},preferences.Accent);
  Resident.IsOn=DashboardResident.IsOn=preferences.Resident;MicaChoice.IsOn=preferences.Mica;
  MiniTopChoice.IsOn=preferences.MiniOnTop;StartMiniChoice.IsOn=preferences.StartInMini;
  RefreshChoice.SelectedIndex=Array.IndexOf(new[]{2,5,10},preferences.RefreshSeconds);
  AutoInterval.IsOn=preferences.AutoIntervalEnabled;AutoThreshold.IsOn=preferences.AutoThresholdEnabled;
  AutoMinutes.Value=preferences.AutoIntervalMinutes;AutoPercent.Value=preferences.AutoThresholdPercent;
  autoPolicy.Configure(preferences,DateTimeOffset.UtcNow);
  ProcessSort.SelectedIndex=0;Navigation.SelectedItem=Navigation.MenuItems[0];initializing=false;ApplyAppearance();UpdateAutoState();
  tray=new TrayService(ShowMain,ShowMini,Exit);
  AppWindow.Closing+=(_,args)=>
  {
   if(exiting)return;
   if(Resident.IsOn){args.Cancel=true;AppWindow.Hide();return;}
   if(busy){args.Cancel=true;exitAfterWork=true;operationCancellation?.Cancel();activeDialog?.Hide();Message("正在取消当前任务，完成后退出。");return;}
   if(!pendingPreferences.IsCompleted){args.Cancel=true;Exit();}
  };
  Closed+=(_,_)=>{exiting=true;timer.Stop();tray.Dispose();mini?.Close();Microsoft.UI.Xaml.Application.Current.Exit();};
  timer.Interval=TimeSpan.FromSeconds(preferences.RefreshSeconds);timer.Tick+=(_,_)=>Refresh();timer.Start();Refresh(false);
 }
 private void Refresh(bool checkAutomatic=true)
 {
  try{var m=MemoryService.Read();memoryTimeline.Record(m.Total,m.Available,DateTimeOffset.UtcNow);latestLoad=memoryTimeline.Current!.Value.Percent;if(checkAutomatic)_=TryAutomaticAsync();}
  catch(Exception e){Message(e.Message,true);}
 }
 private void Message(string text,bool error=false){mini?.Report(text,error);Status.Message=text;Status.Severity=error?InfoBarSeverity.Error:InfoBarSeverity.Informational;}
 private async Task Run(Func<Task> work)
 {
  if(busy){Message("已有任务正在执行，可以取消该任务或继续切换页面。");return;}
  busy=true;operationId++;using var cancellation=new CancellationTokenSource();operationCancellation=cancellation;
  OperationBar.Visibility=Visibility.Visible;OperationText.Text="准备执行…";CancelOperation.IsEnabled=true;
  try{await work();}
  catch(OperationCanceledException){Message("任务已取消。");}
  catch(Exception e){Message(e.Message,true);}
  finally
  {
   operationCancellation=null;busy=false;OperationBar.Visibility=Visibility.Collapsed;
   Refresh(!exitAfterWork);if(exitAfterWork)Exit();
  }
 }
 private void CancelOperation_Click(object sender,RoutedEventArgs e)
 {operationCancellation?.Cancel();CancelOperation.IsEnabled=false;OperationText.Text="正在取消，等待当前系统调用结束…";}
 private double windowScale;
 private void UpdateWindowMinimum()
 {
  double scale=Root.XamlRoot.RasterizationScale;
  if(Math.Abs(scale-windowScale)<0.001)return;windowScale=scale;
  if(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
  {
   var work=Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
   presenter.PreferredMinimumWidth=Math.Min((int)Math.Ceiling(500*scale),work.Width);
   presenter.PreferredMinimumHeight=Math.Min((int)Math.Ceiling(600*scale),work.Height);
  }
 }
 private static ScrollViewer DialogText(string text,UIElement owner)=>new()
 {
  HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
  MaxHeight=Math.Min(440,Math.Max(60,owner.XamlRoot.Size.Height-200)),
  Content=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,24,0)}
 };
 private async Task<bool> Confirm(string title,string text)
 {
  var owner=miniMode&&mini!=null?mini.Content:Content;
  var dialog=new ContentDialog {RequestedTheme=PreferredTheme,Title=title,Content=DialogText(text,owner),PrimaryButtonText="确认执行",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close,XamlRoot=owner.XamlRoot};
  dialogOpen=true;activeDialog=dialog;try{return await dialog.ShowAsync()==ContentDialogResult.Primary;}finally{dialogOpen=false;activeDialog=null;}
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
  NetworkPage.Visibility=tag=="network"?Visibility.Visible:Visibility.Collapsed;
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
  bool medium=e.NewSize.Width<820;
  Grid.SetColumn(AvailableCard,narrow?0:1);Grid.SetRow(AvailableCard,narrow?1:0);
  Grid.SetColumnSpan(AvailableCard,narrow?2:1);
  Grid.SetColumnSpan((FrameworkElement)MemoryCards.Children[0],narrow?2:1);
  ActionCards.ColumnDefinitions[2].Width=medium?new GridLength(0):new GridLength(1,GridUnitType.Star);
  Grid.SetColumn(RegistryCard,narrow?0:1);Grid.SetRow(RegistryCard,narrow?1:0);Grid.SetColumnSpan(RegistryCard,narrow?2:1);
  Grid.SetColumnSpan((FrameworkElement)ActionCards.Children[0],narrow?2:1);
  Grid.SetRow(ProcessCard,narrow?2:medium?1:0);Grid.SetColumn(ProcessCard,medium?0:2);Grid.SetColumnSpan(ProcessCard,medium?2:1);
 }
 public void ApplyStartupMode(){if(preferences.StartInMini)ShowMini();}
 private void PersistPreferences()
 {
  // Snapshot on the UI thread; serialize disk writes on workers and discard obsolete queued saves.
  var snapshot=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences))!;
  long revision=++preferencesRevision;pendingPreferences=SavePreferencesAsync(snapshot,revision);
 }
 private async Task SavePreferencesAsync(Preferences snapshot,long revision)
 {
  await preferencesGate.WaitAsync();
  try{if(revision==preferencesRevision)await Task.Run(snapshot.Save);}
  catch(Exception ex){Message("设置未能保存："+ex.Message,true);}
  finally{preferencesGate.Release();}
 }
 private ElementTheme PreferredTheme => preferences.Theme switch {"Dark"=>ElementTheme.Dark,"Light"=>ElementTheme.Light,_=>ElementTheme.Default};
 private static readonly string[] AccentResourceKeys={"AccentFillColorDefaultBrush","AccentFillColorSecondaryBrush","AccentFillColorTertiaryBrush","ToggleSwitchFillOn","ToggleSwitchFillOnPointerOver","ToggleSwitchFillOnPressed","RadioButtonOuterEllipseFillChecked","RadioButtonOuterEllipseFillCheckedPointerOver","RadioButtonOuterEllipseFillCheckedPressed","NavigationViewSelectionIndicatorForeground"};
 private void ApplyAppearance()
 {
  Root.RequestedTheme=PreferredTheme;
  var hex=preferences.Accent switch {"Purple"=>0x7C3AEDu,"Teal"=>0x007D76u,"Rose"=>0xB82F61u,"Amber"=>0xA65C00u,_=>0x4F6BEDu};
  var brush=(Microsoft.UI.Xaml.Media.SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["AppAccentBrush"];
  brush.Color=Windows.UI.Color.FromArgb(255,(byte)(hex>>16),(byte)(hex>>8),(byte)hex);
  foreach(var key in AccentResourceKeys)
   ((Microsoft.UI.Xaml.Media.SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources[key]).Color=brush.Color;
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
 private void Automation_Toggled(object sender,RoutedEventArgs e)=>SaveAutomation();
 private void AutomationNumber_Changed(NumberBox sender,NumberBoxValueChangedEventArgs args)=>SaveAutomation();
 private void SaveAutomation()
 {
  if(initializing)return;
  if(!IsAdministrator&&(AutoInterval.IsOn||AutoThreshold.IsOn))
  {
   initializing=true;AutoInterval.IsOn=false;AutoThreshold.IsOn=false;initializing=false;
   Message("请以管理员身份启动后开启自动回收；不会自动弹出提权请求。",true);
  }
  preferences.AutoIntervalEnabled=AutoInterval.IsOn;preferences.AutoThresholdEnabled=AutoThreshold.IsOn;
  if(double.IsFinite(AutoMinutes.Value))preferences.AutoIntervalMinutes=(int)Math.Clamp(AutoMinutes.Value,5,1440);
  if(double.IsFinite(AutoPercent.Value))preferences.AutoThresholdPercent=(int)Math.Clamp(AutoPercent.Value,1,99);
  autoPaused=false;autoPolicy.Configure(preferences,DateTimeOffset.UtcNow);PersistPreferences();UpdateAutoState();
 }
 private void UpdateAutoState()
 {
  AutoStateText.Text=autoPaused?"自动回收已暂停，请检查下方错误后重新开启。":!IsAdministrator?"需要管理员权限；非管理员启动时自动回收暂停。":preferences.AutoIntervalEnabled||preferences.AutoThresholdEnabled?"已启用 · 在程序运行期间按设置回收待机缓存。":"默认关闭 · 开启后按设置自动执行。";
 }
 private async Task TryAutomaticAsync()
 {
  if(busy||autoPaused||exitAfterWork||!IsAdministrator)return;
  var reason=autoPolicy.GetDue(preferences,DateTimeOffset.UtcNow,latestLoad);if(reason==null)return;
  busy=true;
  try{Message("正在自动回收待机缓存…");await Task.Run(MemoryService.PurgeStandby);Message("已自动回收待机缓存 · "+reason);}
  catch(Exception ex){autoPaused=true;Message("自动回收已暂停："+ex.Message,true);}
  finally{busy=false;UpdateAutoState();if(exitAfterWork)Exit();}
 }
 public void ActivateExistingInstance()
 {
  if(exiting)return;
  if(miniMode&&mini!=null&&dialogOpen){mini.AppWindow.Show();mini.Activate();return;}
  ShowMain();
 }
 private void ShowMain()
 {
  if(busy&&dialogOpen&&miniMode)return;
  mini?.AppWindow.Hide();miniMode=false;
  AppWindow.Show();
  if(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter&&presenter.State==Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)presenter.Restore();
  Activate();
 }
 private void ShowMini()
 {
  if(busy&&dialogOpen){Message("请先关闭当前确认对话框。");return;}
  if(mini==null)
  {
   mini=new MiniWindow(ShowMain,()=>Memory_Click(this,new RoutedEventArgs()),()=>Diagnostics_Click(this,new RoutedEventArgs()),memoryTimeline);
   mini.ApplyAppearance(PreferredTheme,preferences.Mica,preferences.MiniOnTop);
   mini.AppWindow.Closing+=(_,args)=>
   {
    if(exiting)return;
    args.Cancel=true;
    if(busy&&dialogOpen)return;
    if(Resident.IsOn){mini?.AppWindow.Hide();miniMode=false;}
    else ShowMain();
   };
  }
  miniMode=true;Refresh();AppWindow.Hide();mini.AppWindow.Show();mini.Activate();
 }
 private void Mini_Click(object sender,RoutedEventArgs e)=>ShowMini();
 private async void Diagnostics_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  OperationText.Text="读取提交量与系统性能计数器…";var report=await Task.Run(MemoryDiagnostics.Read);CurrentToken.ThrowIfCancellationRequested();
  var dialog=new ContentDialog {RequestedTheme=PreferredTheme,Title="内存与提交压力诊断",Content=DialogText(report.Describe(),miniMode&&mini!=null?mini.Content:Content),PrimaryButtonText="系统虚拟内存设置",CloseButtonText="关闭",XamlRoot=(miniMode&&mini!=null?mini.Content:Content).XamlRoot};
  Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(dialog,"MemoryDiagnosticDialog");
  dialogOpen=true;activeDialog=dialog;try{if(await dialog.ShowAsync()==ContentDialogResult.Primary)OpenSystem("SystemPropertiesAdvanced.exe");}finally{dialogOpen=false;activeDialog=null;}
 });
 private void OpenSystem(string name)
 {try{Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),name)){UseShellExecute=true});}catch(Exception ex){Message(ex.Message,true);}}
 private async void Memory_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  if(!await Confirm("回收待机缓存","仅清理未修改的待机页，不触碰应用工作集或修改页。待机缓存本就属于可用内存，清空可能增加磁盘读取。继续？"))return;
  OperationText.Text="读取回收前的待机缓存…";var before=await Task.Run(MemoryDiagnostics.Read);CurrentToken.ThrowIfCancellationRequested();
  autoPolicy.RecordAttempt(preferences,DateTimeOffset.UtcNow,latestLoad);await Task.Run(MemoryService.PurgeStandby);
  var after=await Task.Run(MemoryDiagnostics.Read);
  string observed=before.Standby.HasValue&&after.Standby.HasValue?$"待机缓存观测：{MemoryService.Size(before.Standby.Value)} → {MemoryService.Size(after.Standby.Value)}。":"当前性能计数器不可用，未估算释放量。";
  Message("回收请求已完成。"+observed+"这是前后观测，不保证占用率或性能改善。");
 });
 private async void Processes_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  OperationText.Text="在后台采样进程…";var token=CurrentToken;
  processRows=await ProcessService.Scan(token,ProgressForOperation());ApplyProcessFilter();
  ProcessSummary.Text=processRows.Count==0?"未读取到可访问进程。":string.Join(" · ",processRows.Take(3).Select(x=>$"{x.Name} {x.Bytes/1048576d:F0} MB"));
  Message($"已采样 {processRows.Count} 个进程。可搜索并按工作集、CPU 或私有提交排序。");
 });
 private void ProcessFilter_Changed(object sender,TextChangedEventArgs e){if(!initializing)ApplyProcessFilter();}
 private void ProcessSort_Changed(object sender,SelectionChangedEventArgs e){if(!initializing)ApplyProcessFilter();}
 private void ApplyProcessFilter()
 {
  string query=ProcessSearch.Text.Trim();IEnumerable<ProcessRow> rows=processRows;
  if(query.Length>0)rows=rows.Where(x=>x.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||x.Path.Contains(query,StringComparison.OrdinalIgnoreCase)||x.Id.ToString().Contains(query));
  rows=ProcessSort.SelectedIndex switch {1=>rows.OrderByDescending(x=>x.Cpu),2=>rows.OrderByDescending(x=>x.PrivateBytes),_=>rows.OrderByDescending(x=>x.Bytes)};
  Processes.ItemsSource=rows.Take(1000).ToArray();
 }
 private void ProcessLocation_Click(object sender,RoutedEventArgs e)
 {
  if(Processes.SelectedItem is not ProcessRow row||!File.Exists(row.Path)){Message("请选择路径可访问的进程。");return;}
  try{Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),$"/select,\"{row.Path}\""){UseShellExecute=true});}catch(Exception ex){Message(ex.Message,true);}
 }
 private void TaskManager_Click(object sender,RoutedEventArgs e)=>OpenSystem("taskmgr.exe");
 private async void DiskScan_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  var token=CurrentToken;bool caches=IncludeAppCaches.IsChecked==true;
  OperationText.Text="扫描允许目录…";var progress=ProgressForOperation();var result=await Task.Run(()=>DiskService.Scan(caches,token,progress));
  diskRows=result.Rows;diskPage=0;UpdateDiskPage();DiskSummary.Text=$"{diskRows.Count} 项 · {diskRows.Sum(x=>x.Bytes)/1048576d:F1} MB";
  Message($"{(result.Cancelled?"已取消，保留部分结果":result.Limited?"达到扫描边界，保留已有结果":"扫描完成")}：已检查 {result.Seen}，候选 {diskRows.Count}；记录跳过 {result.Skipped}。每页最多 250 项。");
 });
 private void UpdateDiskPage()
 {
  int pages=Math.Max(1,(diskRows.Count+249)/250);diskPage=Math.Clamp(diskPage,0,pages-1);
  DiskList.ItemsSource=diskRows.Skip(diskPage*250).Take(250).ToArray();DiskPageText.Text=$"第 {diskPage+1}/{pages} 页 · {diskRows.Count} 项";
 }
 private void DiskPrevious_Click(object sender,RoutedEventArgs e){if(diskPage>0){diskPage--;UpdateDiskPage();}}
 private void DiskNext_Click(object sender,RoutedEventArgs e){if((diskPage+1)*250<diskRows.Count){diskPage++;UpdateDiskPage();}}
 private async void DiskClean_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  var selected=DiskList.SelectedItems.Cast<DiskRow>().ToList();if(selected.Count==0){Message("请先勾选当前页文件。");return;}
  if(!await Confirm("永久删除所选文件",$"删除当前页所选 {selected.Count} 个文件（{selected.Sum(x=>x.Bytes)/1048576d:F1} MB），不经过回收站。取消只影响尚未处理的文件。"))return;
  var token=CurrentToken;var progress=ProgressForOperation();var r=await Task.Run(()=>DiskService.Delete(selected,token,progress));
  var removed=await Task.Run(()=>selected.Where(x=>!File.Exists(x.Path)).Select(x=>x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase));diskRows.RemoveAll(x=>removed.Contains(x.Path));UpdateDiskPage();DiskSummary.Text="处理完成 · 可重新扫描";
  Message($"{(r.Cancelled?"已取消后续删除":"处理完成")}：删除 {r.Deleted} 个，{r.Bytes/1048576d:F1} MB；跳过 {r.Skipped} 个。");
 });
 private async void StartupScan_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  var token=CurrentToken;OperationText.Text="枚举 Run 键与启动文件夹…";var rows=await Task.Run(()=>StartupService.Scan(token));
  StartupList.ItemsSource=rows.Take(1000).ToArray();StartupList.Visibility=Visibility.Visible;RegistryList.Visibility=Visibility.Collapsed;
  RegistrySummary.Text=$"{rows.Count} 个启动条目";Message($"已列出 {rows.Count} 个启动条目；最多显示 1000 项。机器级与启动文件夹只读。");
 });
 private void SystemStartup_Click(object sender,RoutedEventArgs e)
 {try{Process.Start(new ProcessStartInfo("ms-settings:startupapps"){UseShellExecute=true});}catch(Exception ex){Message(ex.Message,true);}}
 private async void StartupDisable_Click(object sender,RoutedEventArgs e)=>await ChangeStartup(true);
 private async void StartupEnable_Click(object sender,RoutedEventArgs e)=>await ChangeStartup(false);
 private async Task ChangeStartup(bool disable)=>await Run(async()=>
 {
  if(StartupList.Visibility!=Visibility.Visible||StartupList.SelectedItem is not StartupRow row||!row.Managed){Message("请选择当前用户 Run 项；其他来源请使用 Windows 设置。");return;}
  if(row.Disabled==disable){Message(disable?"该项已经由本工具停用。":"该项不是本工具保留的停用项。");return;}
  if(!await Confirm(disable?"备份并停用启动项":"恢复启动项",$"{row.Name}\n{row.Command}\n\n仅移动当前用户 Run 值，保留备份，不结束进程。恢复后最终状态仍受 Windows 启动设置控制。"))return;
  CurrentToken.ThrowIfCancellationRequested();var backup=await Task.Run(()=>StartupService.SetDisabled(row,disable));
  var rows=await Task.Run(()=>StartupService.Scan());StartupList.ItemsSource=rows.Take(1000).ToArray();Message($"{(disable?"已停用":"已还原 Run 项")}。备份：{backup}");
 });
 private async void RegistryScan_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  var rows=await Task.Run(RegistryService.Scan);CurrentToken.ThrowIfCancellationRequested();RegistryList.ItemsSource=rows;RegistryList.Visibility=Visibility.Visible;StartupList.Visibility=Visibility.Collapsed;
  RegistrySummary.Text=$"{rows.Count} 项缺失目标";Message($"发现 {rows.Count} 个目标缺失启动项，请核对后选择。");
 });
 private async void RegistryClean_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  if(RegistryList.Visibility!=Visibility.Visible){Message("请先检查缺失目标，再勾选失效项。");return;}
  var rows=RegistryList.SelectedItems.Cast<RegistryRow>().ToList();if(rows.Count==0){Message("请先勾选失效项。");return;}
  if(!await Confirm("备份并删除失效启动项",$"删除所选 {rows.Count} 项。备份失败则不删除。"))return;
  CurrentToken.ThrowIfCancellationRequested();var r=await Task.Run(()=>RegistryService.Clean(rows));RegistryList.ItemsSource=null;RegistrySummary.Text="清理完成 · 请重新检查";Message($"删除 {r.Deleted} 项，跳过 {r.Skipped} 项。备份：{r.Backup}");
 });
 private async void Restore_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  Directory.CreateDirectory(RegistryService.BackupDirectory);var picker=new Windows.Storage.Pickers.FileOpenPicker();
  WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));picker.FileTypeFilter.Add(".json");
  dialogOpen=true;Windows.Storage.StorageFile? file;try{file=await picker.PickSingleFileAsync();}finally{dialogOpen=false;}if(file==null)return;
  if(!await Confirm("恢复启动项","仅使用你信任的 ClearNest 失效项备份。恢复将重新写入启动命令，现有同名值不会覆盖。"))return;
  CurrentToken.ThrowIfCancellationRequested();var r=await Task.Run(()=>RegistryService.Restore(file.Path));Message($"恢复 {r.Restored} 项，跳过 {r.Skipped} 项。");
 });

 private async void NetworkRefresh_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  var token=CurrentToken;OperationText.Text="读取网络适配器…";
  NetworkAdapters.Text=await Task.Run(()=>NetworkService.ReadAdapters(token));
  Message("网卡信息已更新；链路速率不是实测下载速度。");
 });
 private async void NetworkDiagnose_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  string host=NetworkService.NormalizeHost(NetworkTarget.Text);NetworkResults.Text="";
  OperationText.Text="正在诊断 DNS、TCP 和 HTTPS…";
  int id=operationId;
  var progress=new Progress<NetworkCheck>(row=>{if(id==operationId)NetworkResults.Text+=row.Display+"\n\n";});
  var token=CurrentToken;
  await Task.Run(()=>NetworkService.DiagnoseAsync(host,progress,token));
  Message("诊断完成。TCP 直连与 HTTPS 系统代理路径可能不同；单个站点失败不代表全网故障。");
 });
 private async void NetworkRepair_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
 {
  if(sender is not Button button||!Enum.TryParse<NetworkRepair>(button.Tag as string,out var repair))return;
  if(!IsAdministrator){Message("网络修复需要以管理员身份启动 ClearNest。",true);return;}
  string title=repair switch {NetworkRepair.FlushDns=>"清空 DNS 缓存",NetworkRepair.ResetWinsock=>"重置 Winsock",_=>"重置 TCP/IP"};
  string explanation=repair switch
  {
   NetworkRepair.FlushDns=>"仅清空本机 DNS 解析缓存，不更改 DNS 服务器地址。",
   NetworkRepair.ResetWinsock=>"将 Windows Winsock 目录恢复为默认状态。可能影响 VPN、代理及网络软件，通常需要重启电脑。",
   _=>"将 Windows TCP/IP 设置重置为默认状态。可能影响静态 IP、DNS、VPN 和网络连接；请先记录自己的网络配置，通常需要重启电脑。"
  };
  explanation+="\n\n执行后不能自动撤销。取消只阻止尚未开始的步骤，不中断已经启动的修复命令。本工具不会自动重启电脑。";
  if(!await Confirm(title,explanation))return;
  var token=CurrentToken;OperationText.Text=title+"，等待系统命令完成…";
  NetworkResults.Text=await Task.Run(()=>NetworkService.RepairAsync(repair,token));
  Message(title+"已完成，请查看详细结果。");
 });
 private void CopyNetwork_Click(object sender,RoutedEventArgs e)
 {
  var data=new Windows.ApplicationModel.DataTransfer.DataPackage();
  data.SetText("ClearNest 网络报告\n"+NetworkAdapters.Text+"\n"+NetworkResults.Text);
  Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
  Message("网络报告已复制。报告包含本机网卡和地址信息。");
 }

 private void Resident_Toggled(object sender,RoutedEventArgs e)
 {
  if(initializing||sender is not ToggleSwitch toggle)return;
  preferences.Resident=toggle.IsOn;initializing=true;
  Resident.IsOn=DashboardResident.IsOn=preferences.Resident;initializing=false;PersistPreferences();
 }
 private void Exit_Click(object sender,RoutedEventArgs e)=>Exit();
 private async void Exit()
 {
  if(busy){exitAfterWork=true;operationCancellation?.Cancel();activeDialog?.Hide();Message("正在取消当前任务，完成后退出。");return;}
  if(exiting)return;exiting=true;await pendingPreferences;Close();
 }
}
