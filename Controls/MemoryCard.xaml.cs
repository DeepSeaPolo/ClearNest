using ClearNest.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace ClearNest.Controls;
public sealed partial class MemoryCard : UserControl
{
 private MemoryTimeline? timeline;
 public event RoutedEventHandler? ReclaimRequested;
 public event RoutedEventHandler? DiagnosticsRequested;
 public MemoryCard(){InitializeComponent();}
 public void Bind(MemoryTimeline source)
 {
  if(timeline!=null)timeline.Changed-=Render;
  timeline=source;History.Bind(source);source.Changed+=Render;Render();
 }
 private void Render()
 {
  if(timeline?.Current is not MemorySample sample)return;
  UsageValue.Text=$"{sample.Percent:F1}%";
  CapacityValue.Text=$"已用 {MemoryService.Size(sample.Used)} / {MemoryService.Size(sample.Total)} · 可用 {MemoryService.Size(sample.Available)}";
  PeakValue.Text=$"峰值 {timeline.Peak:F1}%";
  ToolTipService.SetToolTip(PeakValue,"本次运行最近最多 5 分钟的采样峰值");
  ToolTipService.SetToolTip(UsageValue,"物理内存占用率：（总量 − 可用量）÷ 总量。数字与曲线使用同一采样；不是清巢自身占用。");
 }
 private void Reclaim_Click(object sender,RoutedEventArgs e)=>ReclaimRequested?.Invoke(this,e);
 private void Diagnose_Click(object sender,RoutedEventArgs e)=>DiagnosticsRequested?.Invoke(this,e);
}
