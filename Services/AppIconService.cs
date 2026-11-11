using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
namespace ClearNest.Services;
public static class AppIconService
{
 // HICONs are loaded once from the EXE's embedded resources and live until process exit.
 private static readonly Lazy<(IntPtr Large,IntPtr Small)> native=new(LoadNative);
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern uint ExtractIconEx(string file,int index,out IntPtr large,out IntPtr small,uint count);
 [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance,IntPtr resource);
 private static (IntPtr Large,IntPtr Small) LoadNative()
 {
  if(Environment.ProcessPath is string path&&ExtractIconEx(path,0,out var large,out var small,1)>0)
   return(large!=IntPtr.Zero?large:small,small!=IntPtr.Zero?small:large);
  var fallback=LoadIcon(IntPtr.Zero,new IntPtr(32512));return(fallback,fallback);
 }
 public static IntPtr SmallIcon => native.Value.Small;
 public static void ApplyTo(Window window) => window.AppWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(native.Value.Large));
 public static BitmapImage CreateImage()
 {
  using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ClearNest.AppIcon.png")??throw new InvalidOperationException("应用图标资源缺失");
  using var memory=new MemoryStream();stream.CopyTo(memory);memory.Position=0;
  using var random=memory.AsRandomAccessStream();
  var image=new BitmapImage();image.SetSource(random);return image;
 }
}
