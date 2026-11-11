using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
namespace ClearNest.Controls;
// Native layout panel: toolbar actions wrap instead of extending beyond narrow cards.
public sealed class ActionWrapPanel : Panel
{
 public static readonly DependencyProperty SpacingProperty=DependencyProperty.Register(nameof(Spacing),typeof(double),typeof(ActionWrapPanel),new PropertyMetadata(8d,(d,_)=>((ActionWrapPanel)d).InvalidateMeasure()));
 public double Spacing {get=>(double)GetValue(SpacingProperty);set=>SetValue(SpacingProperty,value);}
 protected override Size MeasureOverride(Size available)
 {
  double limit=double.IsInfinity(available.Width)?double.MaxValue:available.Width;
  double x=0,y=0,row=0,width=0;
  foreach(var child in Children)
  {
   child.Measure(new Size(limit,double.PositiveInfinity));
   var size=child.DesiredSize;
   if(x>0&&x+size.Width>limit){y+=row+Spacing;x=0;row=0;}
   width=Math.Max(width,x+size.Width);row=Math.Max(row,size.Height);x+=size.Width+Spacing;
  }
  return new Size(width,y+row);
 }
 protected override Size ArrangeOverride(Size final)
 {
  double x=0,y=0,row=0;
  foreach(var child in Children)
  {
   var size=child.DesiredSize;
   if(x>0&&x+size.Width>final.Width){y+=row+Spacing;x=0;row=0;}
   child.Arrange(new Rect(x,y,Math.Min(size.Width,final.Width),size.Height));
   row=Math.Max(row,size.Height);x+=size.Width+Spacing;
  }
  return final;
 }
}
