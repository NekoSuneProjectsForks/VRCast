@echo off
setlocal EnableExtensions
cd /d "%~dp0"
chcp 65001 >nul

echo VRCast: downloading the latest release from GitHub...
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference = 'Stop';" ^
  "[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12;" ^
  "$ua = @{ 'User-Agent' = 'VRCast-Download' };" ^
  "$zipUrl = $null; $sha = $null; $name = $null;" ^
  "try {" ^
  "  $j = Invoke-RestMethod -Headers $ua -Uri 'https://coffin299.github.io/VRCast/version.json';" ^
  "  if ($j.update -and $j.update.zipUrl -and $j.update.sha256) {" ^
  "    $zipUrl = [string]$j.update.zipUrl; $sha = ([string]$j.update.sha256).ToLowerInvariant();" ^
  "    $name = Split-Path $zipUrl -Leaf;" ^
  "  }" ^
  "} catch { }" ^
  "if (-not $zipUrl) {" ^
  "  $r = Invoke-RestMethod -Headers $ua -Uri 'https://api.github.com/repos/coffin299/VRCast/releases/latest';" ^
  "  $a = @($r.assets) | Where-Object { $_.name -match '^VRCast-.+-win64\.zip$' } | Select-Object -First 1;" ^
  "  if (-not $a) { throw 'No VRCast-*-win64.zip asset on the latest GitHub release.' }" ^
  "  $zipUrl = [string]$a.browser_download_url; $name = [string]$a.name; $sha = $null;" ^
  "}" ^
  "if ($zipUrl -notlike 'https://github.com/coffin299/VRCast/releases/download/*') { throw 'Unexpected download URL.' }" ^
  "$out = Join-Path (Get-Location) $name;" ^
  "Write-Host \"URL: $zipUrl\";" ^
  "Write-Host \"Saving: $out\";" ^
  "Invoke-WebRequest -Headers $ua -Uri $zipUrl -OutFile $out;" ^
  "if ($sha) {" ^
  "  $hash = (Get-FileHash -Algorithm SHA256 -Path $out).Hash.ToLowerInvariant();" ^
  "  if ($hash -ne $sha) { Remove-Item -Force $out; throw \"SHA-256 mismatch (got $hash).\" }" ^
  "  Write-Host \"SHA-256 OK: $sha\";" ^
  "}" ^
  "Write-Host ''; Write-Host \"Done: $out\";" ^
  "Write-Host 'Extract the zip in Explorer, then run VRCast\\VRCast.exe.'"

if errorlevel 1 (
  echo.
  echo Download failed.
  pause
  exit /b 1
)

echo.
pause
exit /b 0
