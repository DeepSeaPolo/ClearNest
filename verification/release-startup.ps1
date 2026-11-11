param([string]$BaselineExecutable)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class StartupProbe {
 [DllImport("kernel32.dll")] public static extern uint SetErrorMode(uint flags);
}
'@
# Inherited by child processes: do not leave an unattended Windows crash dialog open.
$previousErrorMode=[StartupProbe]::SetErrorMode(3)
function Probe-Startup([string]$Executable) {
 $app=Start-Process -FilePath (Resolve-Path $Executable) -PassThru
 try {
  $condition=New-Object System.Windows.Automation.AndCondition(
   (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,[int]$app.Id)),
   (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,('ClearNest '+[char]0xB7+' '+[char]0x6E05+[char]0x5DE2)))
  )
  for($attempt=0;$attempt -lt 40;$attempt++) {
   $app.Refresh()
   if($app.HasExited) {Write-Host "EXIT: $Executable ($($app.ExitCode))";return $false}
   $main=[System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,$condition)
   if($null -ne $main) {
    $buttonCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'SwitchToMini')
    if($null -ne $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$buttonCondition)) {return $true}
   }
   Start-Sleep -Milliseconds 500
  }
  Write-Host "TIMEOUT: $Executable";return $false
 } finally {$app.Refresh();if(!$app.HasExited){Stop-Process -Id $app.Id -Force}}
}
try {
 if($BaselineExecutable) {
  $renamed=Resolve-Path $BaselineExecutable
  $canonical=Join-Path (Split-Path $renamed) 'ClearNest.exe'
  Copy-Item $renamed $canonical
  if(!(Probe-Startup $canonical)) {throw 'Baseline failed even with its original name; rename diagnosis is not established'}
  if(Probe-Startup $renamed) {throw 'Baseline renamed EXE did not reproduce the reported failure'}
  Write-Host 'REPRODUCED: published v0.4.0 starts as ClearNest.exe but exits under its public asset filename'
 }
 $source=Resolve-Path 'publish/single/ClearNest.exe'
 $asset='ClearNest-v0.4.1-win11-x64.exe'
 New-Item -ItemType Directory -Force 'publish/release' | Out-Null
 Copy-Item $source (Join-Path 'publish/release' $asset)
 # Test the exact bytes and final filename that will be uploaded, plus a browser duplicate name.
 $cases=@(
  @{Name='canonical';Path='publish/isolated/ClearNest.exe'},
  @{Name='release';Path=(Join-Path 'publish/release' $asset)},
  @{Name='browser-duplicate';Path='publish/download check/ClearNest-v0.4.1-win11-x64 (1).exe'}
 )
 foreach($case in $cases) {
  if($case.Name -eq 'browser-duplicate') {
   New-Item -ItemType Directory -Force (Split-Path $case.Path) | Out-Null
   Copy-Item $source $case.Path
  }
  $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR=Join-Path $env:RUNNER_TEMP ("clearnest-startup-"+$case.Name)
  if(Test-Path $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR){Remove-Item $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR -Recurse -Force}
  & ./verification/window-modes.ps1 -Executable $case.Path
  if($LASTEXITCODE -ne 0){throw "Window tests failed: $($case.Name)"}
  if(@(Get-ChildItem (Split-Path $case.Path) -File).Count -ne 1){throw "Dependencies leaked beside EXE: $($case.Name)"}
  Write-Host "PASS: full native UI checks with a fresh extraction cache: $($case.Path)"
 }
 $expected=(Get-FileHash $source -Algorithm SHA256).Hash
 $actual=(Get-FileHash (Join-Path 'publish/release' $asset) -Algorithm SHA256).Hash
 if($expected -ne $actual){throw 'Release asset differs from tested build'}
 "$actual  $asset" | Set-Content 'publish/release/SHA256.txt'
} finally {[void][StartupProbe]::SetErrorMode($previousErrorMode)}
