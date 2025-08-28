# install.ps1 — автодетект свободного порта и установка службы
param(
  [int]$PreferredPort = 47613,
  [string]$ServiceName = "PcForGamerAgent"
)

$ErrorActionPreference = "Stop"

# путь до собранного exe (после dotnet publish)
$ExePath = Join-Path $PSScriptRoot "..\publish\PcForGamer.Agent.exe"
if (!(Test-Path $ExePath)) { throw "Agent exe not found: $ExePath`nСначала: dotnet publish -c Release -o .\publish" }

function Get-FreeTcpPort {
  param([int]$Start = 47613, [int]$MaxTries = 50)
  for ($p = $Start; $p -lt ($Start + $MaxTries); $p++) {
    try {
      $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $p)
      $listener.Start(); $listener.Stop()
      return $p
    } catch { continue }
  }
  throw "Не нашёл свободный порт начиная с $Start"
}

# если служба уже стоит — удалим/обновим
$exists = (sc.exe query $ServiceName 2>$null | Select-String $ServiceName) -ne $null
if ($exists) {
  Write-Host "Служба уже существует — останавливаю и удаляю…" -f Yellow
  sc.exe stop  $ServiceName | Out-Null
  sc.exe delete $ServiceName | Out-Null
  Start-Sleep -Seconds 1
}

# подбираем порт
$Port = Get-FreeTcpPort -Start $PreferredPort
$Url  = "http://127.0.0.1:$Port"
Write-Host "Выбран порт: $Port ($Url)"

# аккуратные кавычки для sc.exe (binPath= "<exe>" --urls http://127.0.0.1:PORT)
$BinPath = "`"$ExePath`" --urls $Url"

# ставим службу автозапуском
sc.exe create $ServiceName binPath= "$BinPath" start= auto DisplayName= "PC For Gamer Agent" | Out-Null
sc.exe description $ServiceName "Local helper for pcдляигрока.рф (listens $Url)" | Out-Null
Start-Sleep -Seconds 1
sc.exe start $ServiceName | Out-Null

Write-Host "Готово. Сервис слушает $Url"
