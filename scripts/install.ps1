$ErrorActionPreference = "Stop"
$svc = "PcForGamerAgent"
$exe = Join-Path $PSScriptRoot "..\publish\PcForGamer.Agent.exe"
if(-not (Test-Path $exe)){ throw "Build first: dotnet publish (see installer/build.ps1)" }
sc.exe create $svc binPath= "`"$exe`"" start= auto DisplayName= "PC For Gamer Agent" | Out-Null
sc.exe description $svc "Local helper service for pcдляигрока.рф" | Out-Null
Start-Sleep -s 1
sc.exe start $svc
Write-Host "Started. -> http://127.0.0.1:47613"
