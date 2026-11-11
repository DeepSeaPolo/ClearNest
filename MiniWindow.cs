using ClearNest.Services;
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;
namespace ClearNest;
public sealed class MiniWindow : Window
{
 private readonly TextBlock percent=new() {FontSize=36,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold};
 private readonly TextBlock available=new() {FontSize=13};
 private readonly TextBlock status=new() {FontSize=12,TextWrapping=TextWrapping.Wrap,MaxLines=2,TextTrimming=TextTrimming.CharacterEllipsis};
 private readonly Button purge=new() {Content="回收待机缓存",HorizontalAlignment=HorizontalAlignment.Stretch};
 private readonly ProgressBar bar=new() {Maximum=100};
 public MiniWindow(Action show,Action clean)
 {
  Title="ClearNest Mini";SystemBackdrop=new Microsoft.UI.Xaml.Media.MicaBackdrop();
  var panel=new StackPanel {Spacing=12,Padding=new Thickness(20)};
  var heading=new Grid();heading.Children.Add(new TextBlock {Text="内存使用",VerticalAlignment=VerticalAlignment.Center});
  var open=new Button {Content="返回主窗口",HorizontalAlignment=HorizontalAlignment.Right};
  Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(open,"ReturnToMain");open.Click+=(_,_)=>show();heading.Children.Add(open);panel.Children.Add(heading);
  panel.Children.Add(percent);panel.Children.Add(bar);panel.Children.Add(available);
  purge.Click+=(_,_)=>clean();panel.Children.Add(purge);panel.Children.Add(status);
  var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition {Height=new GridLength(40)});layout.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});
  var titleRegion=new Grid {Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent)};
  var brand=new StackPanel {Orientation=Orientation.Horizontal,Spacing=6,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
  brand.Children.Add(new Image {Width=16,Height=16,Source=AppIconService.CreateImage()});brand.Children.Add(new TextBlock {Text="清巢",FontSize=12});titleRegion.Children.Add(brand);
  layout.Children.Add(titleRegion);Grid.SetRow(panel,1);layout.Children.Add(panel);Content=layout;
  ExtendsContentIntoTitleBar=true;SetTitleBar(titleRegion);AppIconService.ApplyTo(this);
  layout.ActualThemeChanged+=(_,_)=>UpdateCaption();
  AppWindow.Resize(new SizeInt32(360,310));
  if(AppWindow.Presenter is OverlappedPresenter presenter) {presenter.IsAlwaysOnTop=true;presenter.IsResizable=false;presenter.IsMaximizable=false;}
 }
 public void ApplyAppearance(ElementTheme theme,bool mica,bool onTop)
 {
  ((FrameworkElement)Content).RequestedTheme=theme;
  SystemBackdrop=mica?new Microsoft.UI.Xaml.Media.MicaBackdrop():null;
  if(AppWindow.Presenter is OverlappedPresenter presenter)presenter.IsAlwaysOnTop=onTop;
  bar.Foreground=(Microsoft.UI.Xaml.Media.SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["AppAccentBrush"];
  percent.Foreground=bar.Foreground;purge.Background=bar.Foreground;purge.Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
  UpdateCaption();
 }
 private void UpdateCaption()
 {
  var root=(Grid)Content;var dark=root.ActualTheme==ElementTheme.Dark;
  root.Background=SystemBackdrop!=null?null:new Microsoft.UI.Xaml.Media.SolidColorBrush(dark?Windows.UI.Color.FromArgb(255,28,29,33):Windows.UI.Color.FromArgb(255,246,247,250));
  AppWindow.TitleBar.ButtonBackgroundColor=Microsoft.UI.Colors.Transparent;AppWindow.TitleBar.ButtonInactiveBackgroundColor=Microsoft.UI.Colors.Transparent;
  AppWindow.TitleBar.ButtonForegroundColor=dark?Microsoft.UI.Colors.White:Microsoft.UI.Colors.Black;
 }
 public void Report(string text,bool error){status.Text=(error?"操作未完成：":"")+text;ToolTipService.SetToolTip(status,text);}
 public void Update(uint load,string free) {percent.Text=$"{load}%";bar.Value=load;available.Text=$"可用 {free} · 仅回收待机缓存";}
}
