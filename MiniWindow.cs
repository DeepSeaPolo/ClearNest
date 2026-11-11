using ClearNest.Services;
using ClearNest.Controls;
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;
namespace ClearNest;
public sealed class MiniWindow : Window
{
 private readonly MemoryCard memory=new();
 private readonly TextBlock status=new() {FontSize=12,TextWrapping=TextWrapping.Wrap,MaxLines=2,TextTrimming=TextTrimming.CharacterEllipsis};
 public MiniWindow(Action show,Action clean,Action diagnose,MemoryTimeline timeline)
 {
  Title="ClearNest Mini";SystemBackdrop=new Microsoft.UI.Xaml.Media.MicaBackdrop();
  var panel=new StackPanel {Spacing=8,Padding=new Thickness(12)};
  memory.Bind(timeline);memory.ReclaimRequested+=(_,_)=>clean();memory.DiagnosticsRequested+=(_,_)=>diagnose();
  panel.Children.Add(memory);
  var open=new Button {Content="返回主窗口",HorizontalAlignment=HorizontalAlignment.Stretch};
  Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(open,"ReturnToMain");
  open.Click+=(_,_)=>show();panel.Children.Add(open);panel.Children.Add(status);
  var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition {Height=new GridLength(40)});layout.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});
  var titleRegion=new Grid {Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent)};
  var brand=new StackPanel {Orientation=Orientation.Horizontal,Spacing=6,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
  brand.Children.Add(new Image {Width=16,Height=16,Source=AppIconService.CreateImage()});brand.Children.Add(new TextBlock {Text="清巢",FontSize=12});titleRegion.Children.Add(brand);
  layout.Children.Add(titleRegion);
  var scroll=new ScrollViewer {Content=panel,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  scroll.ViewChanged+=(_,_)=>{double gutter=scroll.ScrollableHeight>0.5?24:0;if(panel.Margin.Right!=gutter)panel.Margin=new Thickness(0,0,gutter,0);};
  Grid.SetRow(scroll,1);layout.Children.Add(scroll);Content=layout;
  ExtendsContentIntoTitleBar=true;SetTitleBar(titleRegion);AppIconService.ApplyTo(this);
  layout.ActualThemeChanged+=(_,_)=>UpdateCaption();
  layout.Loaded+=(_,_)=>{
   double scale=layout.XamlRoot.RasterizationScale;
   var work=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Nearest).WorkArea;
   AppWindow.Resize(new SizeInt32(Math.Min((int)Math.Ceiling(360*scale),work.Width),Math.Min((int)Math.Ceiling(350*scale),work.Height)));
  };
  AppWindow.Resize(new SizeInt32(360,350));
  if(AppWindow.Presenter is OverlappedPresenter presenter) {presenter.IsAlwaysOnTop=true;presenter.IsResizable=false;presenter.IsMaximizable=false;}
 }
 public void ApplyAppearance(ElementTheme theme,bool mica,bool onTop)
 {
  ((FrameworkElement)Content).RequestedTheme=theme;
  SystemBackdrop=mica?new Microsoft.UI.Xaml.Media.MicaBackdrop():null;
  if(AppWindow.Presenter is OverlappedPresenter presenter)presenter.IsAlwaysOnTop=onTop;
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
}
