using System;
using System.Collections.Generic;
using System.Linq;
namespace ClearNest.Services;
public readonly record struct MemorySample(DateTimeOffset Time,ulong Total,ulong Available)
{
 public ulong Used=>Total-Available;
 public double Percent=>Total==0?0:100d*Used/Total;
}
public sealed class MemoryTimeline
{
 public static readonly TimeSpan Window=TimeSpan.FromMinutes(5);
 private readonly List<MemorySample> samples=new();
 public IReadOnlyList<MemorySample> Samples=>samples;
 public MemorySample? Current=>samples.Count==0?null:samples[^1];
 public double Peak=>samples.Count==0?0:samples.Max(x=>x.Percent);
 public event Action? Changed;
 public void Record(ulong total,ulong available,DateTimeOffset time)
 {
  if(total==0||available>total)throw new ArgumentOutOfRangeException(nameof(available),"Invalid physical memory sample.");
  var sample=new MemorySample(time,total,available);
  // Actions can refresh the current value, but never leave the last chart point stale.
  if(samples.Count>0&&(time-samples[^1].Time).TotalSeconds<1)samples[^1]=sample;
  else samples.Add(sample);
  samples.RemoveAll(x=>x.Time<time-Window);
  while(samples.Count>180)samples.RemoveAt(0);
  Changed?.Invoke();
 }
 public static double PlotY(double percent,double height)=>height*(1-Math.Clamp(percent,0,100)/100d);
}
