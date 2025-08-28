param(
  [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\PcForGamer.Agent\PcForGamer.Agent.csproj"
$pub  = Join-Path $root "publish"

# 1) publish single-file
dotnet publish $proj -c $Configuration -o $pub

# 2) build MSI (WiX v3 required: candle/light in PATH; ставится choco install wixtoolset -y)
$candle = "candle.exe"
$light  = "light.exe"
$wxs = Join-Path $PSScriptRoot "PcForGamer.Agent.wxs"
& $candle -dPublishDir=$pub -o "$pub\agent.wixobj" $wxs
& $light  -o "$pub\PcForGamer.Agent.msi" "$pub\agent.wixobj"

Write-Host "MSI ready: $pub\PcForGamer.Agent.msi"
