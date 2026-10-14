<#
  VMM PO Governance - install an update package (program files only).
  Your users, data, config.json and backups are never touched.

  1. Unzip the update package anywhere (e.g. Downloads).
  2. PowerShell "Run as administrator", in the unzipped folder:
       powershell -ExecutionPolicy Bypass -File .\apply-update.ps1
     (tool folder other than the default?  add  -Target "E:\path\to\VMM-PO-Governance")

  What it does: backs up the current program files to
  D:\VMM-PO-Governance2\releases\before-<version>-<time>, stops the tool,
  copies the new files, starts the tool and checks it answers.
  Undo:  .\rollback-update.ps1
#>
param([string]$Target = 'D:\VMM-PO-Governance2\VMM-PO-Governance')
$ErrorActionPreference = 'Stop'
$Pkg = Split-Path -Parent $MyInvocation.MyCommand.Path
$TaskName = 'VMM PO Governance'
function Fail($m) { Write-Host ''; Write-Host "ERROR: $m" -ForegroundColor Red; Write-Host 'Nothing else was changed. Send a screenshot if you need help.'; exit 1 }
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Fail 'Please run this in PowerShell opened with "Run as administrator".' }
if (-not (Test-Path (Join-Path $Target 'server.py'))) { Fail "Tool folder not found: $Target (use -Target)" }
if (-not (Test-Path (Join-Path $Pkg 'server.py'))) { Fail 'Run this from inside the unzipped update package.' }
$Version = (Get-Content (Join-Path $Pkg 'VERSION.txt') -ErrorAction SilentlyContinue | Select-Object -First 1)
if (-not $Version) { $Version = 'update' }
$cfg = Get-Content (Join-Path $Target 'config.json') -Raw | ConvertFrom-Json
$Port = if ($cfg.port) { [int]$cfg.port } else { 8080 }
Write-Host "Installing $Version into $Target" -ForegroundColor Cyan

# -- 1. backup current program files
$Releases = Join-Path (Split-Path -Parent $Target) 'releases'
$Backup = Join-Path $Releases ("before-$Version-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $Backup | Out-Null
$keep = @('data', 'backups', 'logs', 'pgdata', 'pgsql', 'Python', 'python', 'tmp', '__pycache__', 'releases', 'config.json')
Get-ChildItem $Target | Where-Object { $keep -notcontains $_.Name -and $_.Name -notlike 'config.*.json' } |
    ForEach-Object { Copy-Item $_.FullName -Destination $Backup -Recurse -Force }
Write-Host "1/4 Backup of current program files: $Backup"

# -- 2. stop the tool (scheduled task, Windows service such as NSSM, or a console window)
$restart = @()
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) { Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue; $restart += 'task' }
foreach ($s in (Get-CimInstance Win32_Service)) {
    $hit = $s.PathName -and $s.PathName -like "*$Target*"
    $p = "HKLM:\SYSTEM\CurrentControlSet\Services\$($s.Name)\Parameters"
    if (-not $hit -and (Test-Path $p)) {
        $pp = Get-ItemProperty $p -ErrorAction SilentlyContinue
        if ($pp.AppDirectory -and ($pp.AppDirectory.TrimEnd('\') -eq $Target.TrimEnd('\'))) { $hit = $true }
    }
    if ($hit -and $s.Name -ne 'VMM-PostgreSQL') { Stop-Service -Name $s.Name -Force -ErrorAction SilentlyContinue; $restart += "svc:$($s.Name)" }
}
Get-CimInstance Win32_Process -Filter "Name='pythonw.exe' OR Name='python.exe'" |
    Where-Object { $_.CommandLine -like '*server.py*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Start-Sleep -Seconds 2
Write-Host "2/4 Tool stopped ($($restart -join ', '))"

# -- 3. copy the new program files
$skip = @('apply-update.ps1', 'rollback-update.ps1', 'UPDATE-STEPS.txt')
Get-ChildItem $Pkg | Where-Object { $skip -notcontains $_.Name } | ForEach-Object { Copy-Item $_.FullName -Destination $Target -Recurse -Force }
Copy-Item (Join-Path $Pkg 'rollback-update.ps1') -Destination $Target -Force
Write-Host "3/4 New files copied"

# -- 4. start and check
if ($restart.Count -eq 0) { Write-Host 'No background task found - starting it with install-service.ps1'; & (Join-Path $Target 'install-service.ps1') }
foreach ($r in $restart) { if ($r -eq 'task') { Start-ScheduledTask -TaskName $TaskName } elseif ($r -like 'svc:*') { Start-Service -Name $r.Substring(4) } }
$ok = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 2
    try { $h = Invoke-RestMethod -UseBasicParsing -Uri "http://localhost:$Port/api/health" -TimeoutSec 3; if ($h.ok) { $ok = $true; break } } catch {}
}
if ($ok) {
    Write-Host "4/4 DONE. Tool is running: version $($h.version), storage $($h.storage)." -ForegroundColor Green
    Write-Host 'Ask users to press Ctrl+F5 once to load the new page.'
} else {
    Write-Host "The tool did not answer on port $Port. Run .\rollback-update.ps1 in $Target to go back, and send a screenshot." -ForegroundColor Red
    exit 1
}
