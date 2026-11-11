using System;
using System.Linq;
using ClearNest.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
namespace ClearNest.Controls;
public sealed class MemoryHistory : Canvas
{
 private MemoryTimeline? timeline;
 private readonly Path area=new() {Opacity=0.14},line=new() {StrokeThickness=1.6};
 private readonly TextBlock hint=new() {Text="正在采集真实趋势…",FontSize=11,Opacity=0.5};
 public MemoryHistory()
 {
  Height=58;
  var brush=(SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["AppAccentBrush"];
  area.Fill=brush;line.Stroke=brush;Children.Add(area);Children.Add(line);Children.Add(hint);
  SizeChanged+=(_,_)=>Draw();
  Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this,"物理内存占用趋势，纵轴固定 0 到 100%");
 }
 public void Bind(MemoryTimeline source)
 {
  if(timeline!=null)timeline.Changed-=Draw;
  timeline=source;timeline.Changed+=Draw;Draw();
 }
 private void Draw()
 {
  var samples=timeline?.Samples;
  double width=ActualWidth,height=Math.Max(0,ActualHeight-2);
  Clip=new RectangleGeometry {Rect=new Rect(0,0,Math.Max(0,width),Math.Max(0,ActualHeight))};
  hint.Visibility=samples==null||samples.Count<2?Visibility.Visible:Visibility.Collapsed;
  if(samples==null||samples.Count<2||width<2||height<2){area.Data=null;line.Data=null;return;}
  var now=samples[^1].Time;var start=now-MemoryTimeline.Window;
  var points=samples.Select(x=>new Point(Math.Clamp((x.Time-start).TotalSeconds/MemoryTimeline.Window.TotalSeconds,0,1)*width,1+MemoryTimeline.PlotY(x.Percent,height))).ToArray();
  PathGeometry Make(bool filled)
  {
   var figure=new PathFigure {StartPoint=filled?new Point(points[0].X,ActualHeight):points[0],IsClosed=filled};
   if(filled)figure.Segments.Add(new LineSegment {Point=points[0]});
   for(int i=1;i<points.Length;i++)
   {
    double mid=(points[i-1].X+points[i].X)/2;
    figure.Segments.Add(new BezierSegment {Point1=new Point(mid,points[i-1].Y),Point2=new Point(mid,points[i].Y),Point3=points[i]});
   }
   if(filled)figure.Segments.Add(new LineSegment {Point=new Point(points[^1].X,ActualHeight)});
   var geometry=new PathGeometry();geometry.Figures.Add(figure);return geometry;
  }
  area.Data=Make(true);line.Data=Make(false);
  ToolTipService.SetToolTip(this,$"最近最多 5 分钟 · 当前 {samples[^1].Percent:F1}%\n纵轴固定 0–100%，不放大波动；启动前的空白没有填造数据。\n占用率 =（物理总量 − 可用物理内存）÷ 物理总量。");
 }
}
