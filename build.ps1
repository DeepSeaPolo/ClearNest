param([switch]$Portable)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
 if ($Portable) {
  dotnet publish .\ClearNest.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o .\publish\portable
 } else {
  dotnet publish .\ClearNest.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained false -o .\publish\small
 }
 if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
} finally { Pop-Location }
