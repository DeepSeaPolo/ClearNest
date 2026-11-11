param($Main,[IntPtr]$Handle)
$ErrorActionPreference='Stop'
Add-Type @'
using System.Runtime.InteropServices;
public static class ScrollPointer {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
}
'@
function Element-Id([string]$id) {
 $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id)
 $element=$Main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
 if($null -eq $element){throw "UI element missing: $id"}
 return $element
}
function Select-Page([string]$id) {
 (Element-Id $id).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Start-Sleep -Milliseconds 400
}
function Vertical-Bar($root) {
 $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ScrollBar)
 $bars=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition)
 $visible=@($bars | Where-Object {$_.Current.BoundingRectangle.Height -gt $_.Current.BoundingRectangle.Width -and $_.Current.BoundingRectangle.Width -gt 0})
 return $visible | Sort-Object {$_.Current.BoundingRectangle.Right} -Descending | Select-Object -First 1
}
function Check-PageScroll([string]$navigation,[string]$scrollerId,[string]$lastId,[string]$name) {
 Select-Page $navigation
 $scroller=Element-Id $scrollerId
 $pattern=$scroller.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
 if(!$pattern.Current.VerticallyScrollable){throw "Expected overflowing small-window page: $name"}
 if($pattern.Current.HorizontallyScrollable){throw "Unexpected horizontal page scrolling: $name"}
 $pattern.SetScrollPercent(-1,0)
 $bounds=$scroller.Current.BoundingRectangle
 [void][ScrollPointer]::SetCursorPos([int]($bounds.Right-6),[int]($bounds.Top+40))
 Start-Sleep -Milliseconds 600
 Check-VisibleActions ($name+'-top')
 Capture-Window $Handle ($name+'-top')
 $pattern.SetScrollPercent(-1,100)
 Start-Sleep -Milliseconds 400
 $bar=Vertical-Bar $scroller
 if($null -eq $bar){throw "Vertical scrollbar not exposed: $name"}
 $last=Element-Id $lastId
 $rect=$last.Current.BoundingRectangle
 if($last.Current.IsOffscreen -or $rect.Bottom -gt $bounds.Bottom+2){throw "Last page action is clipped at end: $name"}
 if($rect.Right -gt $bar.Current.BoundingRectangle.Left-1){throw "Scrollbar overlays last page content: $name"}
 Check-VisibleActions ($name+'-bottom')
 Capture-Window $Handle ($name+'-bottom')
 $pattern.SetScrollPercent(-1,0)
 Write-Output "PASS: expanded scrollbar stays clear of content and last action remains reachable: $name"
}
function Check-VisibleActions([string]$name) {
 $bounds=$Main.Current.BoundingRectangle
 $buttons=$Main.FindAll([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button)))
 foreach($button in $buttons) {
  if($button.Current.IsOffscreen){continue}
  $rect=$button.Current.BoundingRectangle
  if($rect.Top -lt $bounds.Top+44 -or $rect.Width -le 0){continue}
  if($rect.Right -gt $bounds.Right-20){throw "Visible action is clipped at the window edge: $name / $($button.Current.Name)"}
 }
}
$window=$Main.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
$transform=$Main.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
$state=$window.Current.WindowVisualState
$original=$Main.Current.BoundingRectangle
try {
 $window.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
 $transform.Resize(860,700)
 Start-Sleep -Milliseconds 600
 Check-PageScroll 'OpenHome' 'HomeScroller' 'OpenNetworkHome' 'scroll-home-860'
 Check-PageScroll 'OpenSettings' 'SettingsPageScroller' 'ExitApp' 'scroll-settings-860'
 Check-PageScroll 'OpenNetwork' 'NetworkPageScroller' 'NetworkResults' 'scroll-network-860'
 # The report editor has its own native scrolling inside the page.
 $report=Element-Id 'NetworkAdapters'
 $text=$report.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
 $rectangles=$text.DocumentRange.GetBoundingRectangles()
 $rightLimit=$report.Current.BoundingRectangle.Right-20
 for($index=0;$index+3 -lt $rectangles.Length;$index+=4) {
  if($rectangles[$index]+$rectangles[$index+2] -gt $rightLimit+1){throw 'Report text overlaps its native scrollbar gutter'}
 }
 Write-Output 'PASS: multiline report text leaves room for its own scrollbar'
 Select-Page 'OpenHome'
 Invoke-Button $Main 'ScanProcessHome'
 Select-Page 'OpenProcesses'
 $list=Element-Id 'ProcessesList'
 $listPattern=$list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
 for($attempt=0;$attempt -lt 30 -and !$listPattern.Current.VerticallyScrollable;$attempt++){Start-Sleep -Milliseconds 100}
 if(!$listPattern.Current.VerticallyScrollable){throw 'Process fixture did not produce a scrolling list'}
 $listPattern.SetScrollPercent(-1,100)
 Start-Sleep -Milliseconds 400
 $bar=Vertical-Bar $list
 if($null -eq $bar){throw 'Process list scrollbar missing'}
 $items=$list.FindAll([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)))
 $checked=0
 foreach($item in $items) {
  if($item.Current.IsOffscreen){continue}
  if($item.Current.BoundingRectangle.Right -gt $bar.Current.BoundingRectangle.Left-1){throw 'Process row overlaps scrollbar'}
  $checked++
 }
 if($checked -eq 0){throw 'No visible process rows were verified'}
 Capture-Window $Handle 'scroll-process-860'
 Write-Output "PASS: $checked visible process rows stay clear of the scrollbar"
 $transform.Resize(640,700)
 Start-Sleep -Milliseconds 500
 Check-PageScroll 'OpenHome' 'HomeScroller' 'OpenNetworkHome' 'scroll-home-640'
 Check-PageScroll 'OpenSettings' 'SettingsPageScroller' 'ExitApp' 'scroll-settings-640'
 Check-PageScroll 'OpenNetwork' 'NetworkPageScroller' 'NetworkResults' 'scroll-network-640'
 foreach($width in @(640,500)) {
  $transform.Resize($width,700)
  Start-Sleep -Milliseconds 300
  foreach($page in @(@('OpenProcesses','process'),@('OpenFiles','files'),@('OpenStartup','startup'))) {
   Select-Page $page[0]
   Check-VisibleActions ($page[1]+'-'+$width)
   Capture-Window $Handle ('audit-'+$page[1]+'-'+$width)
  }
 }
 Check-PageScroll 'OpenHome' 'HomeScroller' 'OpenNetworkHome' 'scroll-home-500'
 Check-PageScroll 'OpenSettings' 'SettingsPageScroller' 'ExitApp' 'scroll-settings-500'
 Check-PageScroll 'OpenNetwork' 'NetworkPageScroller' 'NetworkResults' 'scroll-network-500'
 Select-Page 'OpenSettings'
 (Element-Id 'ThemeLight').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Start-Sleep -Milliseconds 400
 Check-VisibleActions 'settings-light-500'
 Capture-Window $Handle 'audit-settings-light-500'
 (Element-Id 'ThemeDark').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Write-Output 'PASS: all six pages remain within the small-window bounds, including light and dark settings'
} finally {
 Select-Page 'OpenHome'
 (Element-Id 'HomeScroller').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1,0)
 if($state -eq [System.Windows.Automation.WindowVisualState]::Normal){$transform.Resize($original.Width,$original.Height)}
 else{$window.SetWindowVisualState($state)}
 Start-Sleep -Milliseconds 400
}
