param([string]$Executable="publish/isolated/ClearNest.exe")
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
 for ($attempt=0; $attempt -lt 60; $attempt++) {
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
$app = Start-Process -FilePath (Resolve-Path $Executable) -PassThru
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
 $homeElement=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$homeCondition)
 $homeElement.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Start-Sleep -Seconds 1
 Capture-Window $mainHandle 'dashboard-dark' 
 $identity=[System.Security.Principal.WindowsIdentity]::GetCurrent()
 try {$admin=(New-Object System.Security.Principal.WindowsPrincipal($identity)).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)} finally {$identity.Dispose()}
 if ($admin) {
  $autoCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'AutoIntervalToggle')
  $autoToggle=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$autoCondition)
  $autoToggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
  Start-Sleep -Seconds 1
  $autoPreferences=Get-Content "$env:LOCALAPPDATA/ClearNest/preferences.json" -Raw | ConvertFrom-Json
  if (!$autoPreferences.AutoIntervalEnabled) {throw 'Automatic interval setting was not saved'}
  $autoToggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
  Write-Output 'PASS: interval automation opt-in is persisted; no reclamation invoked during test'
 }

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

 # Exercise real process redirection in normal, Mini and resident-hidden states.
 function Test-Reactivation {
  $second=Start-Process -FilePath (Resolve-Path $Executable) -PassThru
  try {
   if(!$second.WaitForExit(10000)){throw 'A second application process remained running'}
   if($second.ExitCode -ne 0){throw "Secondary launch failed: $($second.ExitCode)"}
   Start-Sleep -Milliseconds 700
   if(![WindowProbe]::IsWindowVisible($mainHandle)){throw 'Secondary launch did not restore the existing main window'}
   $app.Refresh();if($app.HasExited){throw 'Primary instance exited during reactivation'}
  } finally {$second.Refresh();if(!$second.HasExited){Stop-Process -Id $second.Id -Force}}
 }
 Test-Reactivation
 Write-Output 'PASS: second launch exits and restores the existing main window'
 Invoke-Button $main 'SwitchToMini'
 Test-Reactivation
 if([WindowProbe]::IsWindowVisible($miniHandle)){throw 'Mini remained visible after secondary launch restored main'}
 Write-Output 'PASS: second launch restores main from Mini without creating another instance'
 $residentCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'ResidentHome')
 $resident=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$residentCondition).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
 $residentWasOn=$resident.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On
 if(!$residentWasOn){$resident.Toggle();Start-Sleep -Milliseconds 300}
 $main.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 Start-Sleep -Milliseconds 300
 if([WindowProbe]::IsWindowVisible($mainHandle)){throw 'Resident close did not hide main'}
 Test-Reactivation
 if(!$residentWasOn){$resident.Toggle()}
 Write-Output 'PASS: second launch restores the resident-hidden primary window'
 $networkCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'OpenNetwork')
 $network=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$networkCondition)
 $network.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Start-Sleep -Milliseconds 500
 Invoke-Button $main 'RefreshNetwork'
 $adapterCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'NetworkAdapters')
 $adapterValue=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$adapterCondition).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
 if($adapterValue.Length -lt 20 -or $adapterValue.StartsWith([string][char]0x70B9)){throw 'Network adapter inspection did not update the native UI'}
 Capture-Window $mainHandle 'network'
 Write-Output 'PASS: native network page displays adapter information without resetting networking'
 $homeElement.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Start-Sleep -Milliseconds 500

 # Verify a worker scan does not lock window-mode navigation.
 $scanCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'ScanProcessHome')
 $scanButton=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$scanCondition)
 $latency=[System.Diagnostics.Stopwatch]::StartNew()
 $scanButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 $switchCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'SwitchToMini')
 $switchButton=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$switchCondition)
 $switchButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 $busyMini=Find-Window $app.Id 'ClearNest Mini'
 if ([WindowProbe]::IsWindowVisible($mainHandle)) {throw 'Main window stayed visible during worker scan switch'}
 if ($latency.Elapsed.TotalSeconds -gt 5) {throw 'Worker scan blocked native window switching beyond test budget'}
 Invoke-Button $busyMini 'ReturnToMain'
 $cancelCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'CancelOperation')
 $cancelButton=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$cancelCondition)
 if ($null -ne $cancelButton -and !$cancelButton.Current.IsOffscreen) {$cancelButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()}
 Write-Output "PASS: worker scan permits Mini switching ($($latency.Elapsed.TotalMilliseconds) ms including UI automation)"
 Write-Output 'Native window mode checks passed'
} finally {
 $app.Refresh()
 if (!$app.HasExited) {Stop-Process -Id $app.Id -Force}
}
