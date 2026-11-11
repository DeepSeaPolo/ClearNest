param([switch]$Portable,[switch]$SingleFile)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
 if ($SingleFile) {
  dotnet publish .\ClearNest.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -o .\publish\single
 } elseif ($Portable) {
  dotnet publish .\ClearNest.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o .\publish\portable
 } else {
  dotnet publish .\ClearNest.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained false -o .\publish\small
 }
 if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
} finally { Pop-Location }
