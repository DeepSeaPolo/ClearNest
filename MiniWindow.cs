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
 private readonly ProgressBar bar=new() {Maximum=100};
 public MiniWindow(Action show,Action clean)
 {
  Title="ClearNest Mini";SystemBackdrop=new Microsoft.UI.Xaml.Media.MicaBackdrop();
  var panel=new StackPanel {Spacing=12,Padding=new Thickness(20)};
  var heading=new Grid();heading.Children.Add(new TextBlock {Text="内存使用",VerticalAlignment=VerticalAlignment.Center});
  var open=new Button {Content="返回主窗口",HorizontalAlignment=HorizontalAlignment.Right};
  Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(open,"ReturnToMain");open.Click+=(_,_)=>show();heading.Children.Add(open);panel.Children.Add(heading);
  panel.Children.Add(percent);panel.Children.Add(bar);panel.Children.Add(available);
  var purge=new Button {Content="回收待机缓存",HorizontalAlignment=HorizontalAlignment.Stretch};
  purge.Click+=(_,_)=>clean();panel.Children.Add(purge);panel.Children.Add(status);Content=panel;
  AppWindow.Resize(new SizeInt32(360,360));
  if(AppWindow.Presenter is OverlappedPresenter presenter) {presenter.IsAlwaysOnTop=true;presenter.IsResizable=false;presenter.IsMaximizable=false;}
 }
 public void Report(string text,bool error){status.Text=(error?"操作未完成：":"")+text;ToolTipService.SetToolTip(status,text);}
 public void Update(uint load,string free) {percent.Text=$"{load}%";bar.Value=load;available.Text=$"可用 {free} · 仅回收待机缓存";}
}
