using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
namespace ClearNest.Controls;
// Bounded native area chart; all plotted values come from real memory samples.
public sealed class MemoryHistory : Canvas
{
 private readonly List<(DateTimeOffset Time,double Load)> samples=new();
 private readonly Path area=new() {Opacity=0.18},line=new() {Opacity=0.65,StrokeThickness=1.5};
 private readonly TextBlock hint=new() {Text="正在采集实时趋势…",FontSize=11,Opacity=0.5};
 public MemoryHistory()
 {
  Height=56;IsHitTestVisible=true;
  var brush=(SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["AppAccentBrush"];
  area.Fill=brush;line.Stroke=brush;Children.Add(area);Children.Add(line);Children.Add(hint);
  SizeChanged+=(_,_)=>Draw();
  Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this,"实时内存使用率趋势");
 }
 public void Sample(double load)
 {
  var now=DateTimeOffset.UtcNow;
  // UI refreshes after actions do not insert duplicate points between polling intervals.
  if(samples.Count>0&&(now-samples[^1].Time).TotalSeconds<1)return;
  samples.Add((now,Math.Clamp(load,0,100)));if(samples.Count>120)samples.RemoveAt(0);Draw();
 }
 private void Draw()
 {
  var width=ActualWidth;var height=ActualHeight;
  hint.Visibility=samples.Count<2?Visibility.Visible:Visibility.Collapsed;
  if(samples.Count<2||width<2||height<2)return;
  // A limited adaptive range makes genuine small variations visible; no synthetic history.
  var min=Math.Max(0,samples.Min(x=>x.Load)-4);var max=Math.Min(100,samples.Max(x=>x.Load)+4);
  var duration=Math.Max(0.001,(samples[^1].Time-samples[0].Time).TotalSeconds);
  var points=samples.Select(x=>new Point((x.Time-samples[0].Time).TotalSeconds/duration*width,height-(x.Load-min)/Math.Max(1,max-min)*(height-4)-2)).ToArray();
  PathGeometry Make(bool filled)
  {
   var figure=new PathFigure {StartPoint=filled?new Point(points[0].X,height):points[0],IsClosed=filled};
   if(filled)figure.Segments.Add(new LineSegment {Point=points[0]});
   for(int i=1;i<points.Length;i++)
   {
    double mid=(points[i-1].X+points[i].X)/2;
    figure.Segments.Add(new BezierSegment {Point1=new Point(mid,points[i-1].Y),Point2=new Point(mid,points[i].Y),Point3=points[i]});
   }
   if(filled)figure.Segments.Add(new LineSegment {Point=new Point(points[^1].X,height)});
   var geometry=new PathGeometry();geometry.Figures.Add(figure);return geometry;
  }
  area.Data=Make(true);line.Data=Make(false);
  ToolTipService.SetToolTip(this,$"最近 {duration:F0} 秒的真实采样 · 当前 {samples[^1].Load:F1}% · 纵轴自动缩放");
 }
}
