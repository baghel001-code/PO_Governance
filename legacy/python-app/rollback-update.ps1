<#
  VMM PO Governance - put back the program files saved by apply-update.ps1.
  PowerShell "Run as administrator", in the tool folder:
      powershell -ExecutionPolicy Bypass -File .\rollback-update.ps1
  (a specific backup:  -From "D:\VMM-PO-Governance2\releases\before-v1.1-20261012-1900")
  Users, data and config.json are not touched.
#>
param([string]$From = '', [string]$Target = '')
$ErrorActionPreference = 'Stop'
if (-not $Target) { $Target = Split-Path -Parent $MyInvocation.MyCommand.Path }
$TaskName = 'VMM PO Governance'
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Write-Host 'Please run in PowerShell opened with "Run as administrator".' -ForegroundColor Red; exit 1 }
$Releases = Join-Path (Split-Path -Parent $Target) 'releases'
if (-not $From) { $From = (Get-ChildItem $Releases -Directory -Filter 'before-*' | Sort-Object Name -Descending | Select-Object -First 1).FullName }
if (-not $From -or -not (Test-Path (Join-Path $From 'server.py'))) { Write-Host "No backup found in $Releases" -ForegroundColor Red; exit 1 }
Write-Host "Restoring program files from $From" -ForegroundColor Cyan
$restart = @()
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) { Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue; $restart += 'task' }
foreach ($s in (Get-CimInstance Win32_Service)) {
    $hit = $s.PathName -and $s.PathName -like "*$Target*"
    $p = "HKLM:\SYSTEM\CurrentControlSet\Services\$($s.Name)\Parameters"
    if (-not $hit -and (Test-Path $p)) { $pp = Get-ItemProperty $p -ErrorAction SilentlyContinue; if ($pp.AppDirectory -and ($pp.AppDirectory.TrimEnd('\') -eq $Target.TrimEnd('\'))) { $hit = $true } }
    if ($hit -and $s.Name -ne 'VMM-PostgreSQL') { Stop-Service -Name $s.Name -Force -ErrorAction SilentlyContinue; $restart += "svc:$($s.Name)" }
}
Get-CimInstance Win32_Process -Filter "Name='pythonw.exe' OR Name='python.exe'" | Where-Object { $_.CommandLine -like '*server.py*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Start-Sleep -Seconds 2
Get-ChildItem $From | ForEach-Object { Copy-Item $_.FullName -Destination $Target -Recurse -Force }
foreach ($r in $restart) { if ($r -eq 'task') { Start-ScheduledTask -TaskName $TaskName } elseif ($r -like 'svc:*') { Start-Service -Name $r.Substring(4) } }
if ($restart.Count -eq 0) { & (Join-Path $Target 'install-service.ps1') }
Write-Host 'Done. Previous version restored and started. Ask users to press Ctrl+F5.' -ForegroundColor Green
