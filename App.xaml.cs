using Microsoft.UI.Xaml;
namespace ClearNest;
public partial class App : Microsoft.UI.Xaml.Application
{
 private MainWindow? window;
 public App() { InitializeComponent(); }
 protected override void OnLaunched(LaunchActivatedEventArgs args) { window = new MainWindow(); window.Activate(); window.ApplyStartupMode(); }
}
