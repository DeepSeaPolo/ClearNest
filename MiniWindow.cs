using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;
namespace ClearNest;
public sealed class MiniWindow : Window
{
 private readonly TextBlock text=new() {FontSize=16};
 public MiniWindow(Action show,Action clean)
 {
  Title="ClearNest Mini";SystemBackdrop=new Microsoft.UI.Xaml.Media.MicaBackdrop();
  var panel=new StackPanel {Spacing=10,Padding=new Thickness(14)};
  panel.Children.Add(text); var buttons=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8};
  var open=new Button {Content="主窗口"};open.Click+=(_,_)=>show();
  var purge=new Button {Content="回收待机缓存"};purge.Click+=(_,_)=>clean();
  buttons.Children.Add(open);buttons.Children.Add(purge);panel.Children.Add(buttons);Content=panel;
  AppWindow.Resize(new SizeInt32(330,175));
  if(AppWindow.Presenter is OverlappedPresenter presenter) {presenter.IsAlwaysOnTop=true;presenter.IsResizable=false;presenter.IsMaximizable=false;}
 }
 public void Update(string value) => text.Text=value;
}
