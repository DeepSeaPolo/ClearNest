$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowProbe {
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
}
'@
function Capture-Window($handle, $name) {
 $bounds = New-Object WindowProbe+Rect
 [void][WindowProbe]::GetWindowRect($handle,[ref]$bounds)
 $bitmap = New-Object System.Drawing.Bitmap(($bounds.Right-$bounds.Left),($bounds.Bottom-$bounds.Top))
 $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
 try {
  $graphics.CopyFromScreen($bounds.Left,$bounds.Top,0,0,$bitmap.Size)
  [void](New-Item -ItemType Directory -Force publish/ui-checks)
  $bitmap.Save((Join-Path (Get-Location) "publish/ui-checks/$name.png"),[System.Drawing.Imaging.ImageFormat]::Png)
 } finally {$graphics.Dispose();$bitmap.Dispose()}
}
function Find-Window($processId, $title) {
 $condition = New-Object System.Windows.Automation.AndCondition(
   (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$processId)),
   (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $title))
 )
 for ($attempt=0; $attempt -lt 30; $attempt++) {
  $element = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,$condition)
  if ($null -ne $element) {return $element}
  Start-Sleep -Milliseconds 500
 }
 throw "Window did not appear: $title"
}
function Invoke-Button($root, $automationId) {
 $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$automationId)
 $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
 if ($null -eq $button) {throw "Button not found: $automationId"}
 $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
 $pattern.Invoke()
 Start-Sleep -Seconds 1
}
$app = Start-Process -FilePath (Resolve-Path 'publish/portable/ClearNest.exe') -PassThru
try {
 $main = Find-Window $app.Id ('ClearNest '+[char]0xB7+' '+[char]0x6E05+[char]0x5DE2)
 $mainHandle = [IntPtr]$main.Current.NativeWindowHandle
 $screen=[System.Windows.Automation.AutomationElement]::RootElement.Current.BoundingRectangle
 if ($main.Current.BoundingRectangle.Width -gt $screen.Width -or $main.Current.BoundingRectangle.Height -gt $screen.Height) {
  $main.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
  Start-Sleep -Seconds 1
 }
 Start-Sleep -Seconds 2
 Capture-Window $mainHandle 'main'
 $navCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'OpenSettings')
 $settings=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$navCondition)
 $settings.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Start-Sleep -Seconds 1
 $darkCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'ThemeDark')
 $dark=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$darkCondition)
 $dark.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Start-Sleep -Seconds 1
 $preferences=Get-Content "$env:LOCALAPPDATA/ClearNest/preferences.json" -Raw | ConvertFrom-Json
 if ($preferences.Theme -ne 'Dark') {throw 'Dark theme was not persisted'}
 Write-Output 'PASS: dark theme selected and persisted through native UI'
 Capture-Window $mainHandle 'settings-dark'
 $homeCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'OpenHome')
 $home=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$homeCondition)
 $home.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Start-Sleep -Seconds 1
 Capture-Window $mainHandle 'dashboard-dark' 
 for ($cycle=0; $cycle -lt 3; $cycle++) {
  Invoke-Button $main 'SwitchToMini'
  $mini = Find-Window $app.Id 'ClearNest Mini'
  $miniHandle = [IntPtr]$mini.Current.NativeWindowHandle
  if ([WindowProbe]::IsWindowVisible($mainHandle)) {throw 'Main window remains visible in Mini mode'}
  if (![WindowProbe]::IsWindowVisible($miniHandle)) {throw 'Mini window is hidden in Mini mode'}
  Write-Output "PASS: Mini mode hides main window (cycle $cycle)"
  if ($cycle -eq 0) {Capture-Window $miniHandle 'mini'}
  Invoke-Button $mini 'ReturnToMain'
  if (![WindowProbe]::IsWindowVisible($mainHandle)) {throw 'Main window did not return'}
  if ([WindowProbe]::IsWindowVisible($miniHandle)) {throw 'Mini remains visible after returning to main'}
  Write-Output "PASS: Main mode hides Mini window (cycle $cycle)"
 }
 Write-Output 'Native window mode checks passed'
} finally {
 $app.Refresh()
 if (!$app.HasExited) {Stop-Process -Id $app.Id -Force}
}
