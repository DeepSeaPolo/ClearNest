$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowProbe {
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
}
'@
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
 $main = Find-Window $app.Id 'ClearNest · 清巢'
 $mainHandle = [IntPtr]$main.Current.NativeWindowHandle
 for ($cycle=0; $cycle -lt 3; $cycle++) {
  Invoke-Button $main 'SwitchToMini'
  $mini = Find-Window $app.Id 'ClearNest Mini'
  $miniHandle = [IntPtr]$mini.Current.NativeWindowHandle
  if ([WindowProbe]::IsWindowVisible($mainHandle)) {throw 'Main window remains visible in Mini mode'}
  if (![WindowProbe]::IsWindowVisible($miniHandle)) {throw 'Mini window is hidden in Mini mode'}
  Write-Output "PASS: Mini mode hides main window (cycle $cycle)"
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
