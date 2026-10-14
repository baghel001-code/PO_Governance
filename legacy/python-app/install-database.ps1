<#
  VMM PO Governance - switch to the PostgreSQL database (run once).

  Copy the contents of the update package INTO the tool folder first
  (the folder that has server.py, config.json and data), then run in
  PowerShell opened with "Run as administrator":

      Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
      .\install-database.ps1

  What it does (about 2-5 minutes):
    1. Finds Python and checks the files are complete
    2. Stops the tool
    3. Sets up PostgreSQL 17 as the Windows service "VMM-PostgreSQL"
       (database "pogov", localhost:5433 only, starts with the server)
    4. Copies all data - users and passwords, uploaded files, requests,
       approvals, history, saved runs, audit log - into the database and
       checks every record. The data folder is NOT changed (it is the fallback).
    5. Starts the tool on the database and checks it answers

  Undo (go back to the data folder):
      python migrate_to_db.py --use-files      then   .\restart-service.ps1
#>
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root
$TaskName = 'VMM PO Governance'
$LogFile = Join-Path $Root ('logs\install-database-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
New-Item -ItemType Directory -Force -Path (Join-Path $Root 'logs') | Out-Null
Start-Transcript -Path $LogFile -Append | Out-Null

function Step($t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }
function Stop-WithError($msg) {
    Write-Host ''
    Write-Host "ERROR: $msg" -ForegroundColor Red
    Write-Host "Log: $LogFile"
    Write-Host 'Send a screenshot of this window if you need help.'
    Stop-Transcript | Out-Null
    exit 1
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Stop-WithError 'Please run this in PowerShell opened with "Run as administrator".' }

# ---------------------------------------------------------------- 1. checks
Step '1/5  Checking files and Python'
foreach ($f in @('server.py', 'storage.py', 'accounts.py', 'setup_db.py', 'migrate_to_db.py', 'config.json', 'pylib\pg8000\__init__.py', 'pgsql\bin\initdb.exe', 'pgsql\bin\pg_ctl.exe', 'pgsql\bin\postgres.exe', 'app\index.html')) {
    if (-not (Test-Path (Join-Path $Root $f))) { Stop-WithError "Missing $f - copy the whole update package into $Root and run again." }
}
if (-not (Test-Path (Join-Path $Root 'data'))) {
    Write-Warning "No data folder in $Root - the database will start empty (only the admin login)."
}

function Find-Python {
    $bundled = Join-Path $Root 'python\python.exe'
    if (Test-Path $bundled) { return $bundled }
    foreach ($cmd in @('python.exe')) {
        $c = Get-Command $cmd -ErrorAction SilentlyContinue
        if ($c -and $c.Source -notlike '*WindowsApps*') { return $c.Source }
    }
    $roots = @("$env:ProgramFiles\Python3*", "C:\Python3*", "$env:LOCALAPPDATA\Programs\Python\Python3*", "C:\Users\*\AppData\Local\Programs\Python\Python3*")
    foreach ($r in $roots) {
        $hit = Get-ChildItem -Path $r -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending |
               Where-Object { Test-Path (Join-Path $_.FullName 'python.exe') } | Select-Object -First 1
        if ($hit) { return (Join-Path $hit.FullName 'python.exe') }
    }
    $py = Get-Command py.exe -ErrorAction SilentlyContinue
    if ($py) {
        try { $exe = & $py.Source -3 -c "import sys;print(sys.executable)" } catch { $exe = $null }
        if ($exe) { return $exe }
    }
    return $null
}
$Py = Find-Python
if (-not $Py) { Stop-WithError 'Python not found. Install Python 3.12 (64-bit) from python.org with "Install for all users" ticked, then run this again.' }
$ver = & $Py -c "import sys;print('%d.%d' % sys.version_info[:2])"
if ([version]$ver -lt [version]'3.9') { Stop-WithError "Python $ver found at $Py - version 3.9 or newer is needed." }
Write-Host "Python $ver : $Py"
& $Py -c "import sys; sys.path.insert(0, r'$Root\pylib'); import pg8000.native; print('Database driver OK')"
if ($LASTEXITCODE -ne 0) { Stop-WithError 'The bundled database driver (pylib) could not be loaded.' }

$cfg = Get-Content (Join-Path $Root 'config.json') -Raw | ConvertFrom-Json
$Port = if ($cfg.port) { [int]$cfg.port } else { 8080 }
Copy-Item (Join-Path $Root 'config.json') (Join-Path $Root ('config.before-database-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json'))

# ---------------------------------------------------------------- 2. stop the tool
Step '2/5  Stopping the tool'
$restart = @()
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($task) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    $restart += 'task'
    Write-Host "Stopped scheduled task '$TaskName'."
}
# Windows services (e.g. installed with NSSM) that run this folder's server.py
$svcs = @()
foreach ($s in (Get-CimInstance Win32_Service)) {
    $hit = $false
    if ($s.PathName -and $s.PathName -like "*$Root*") { $hit = $true }
    $p = "HKLM:\SYSTEM\CurrentControlSet\Services\$($s.Name)\Parameters"
    if (-not $hit -and (Test-Path $p)) {
        $pp = Get-ItemProperty $p -ErrorAction SilentlyContinue
        if (($pp.AppDirectory -and ($pp.AppDirectory.TrimEnd('\') -eq $Root.TrimEnd('\'))) -or
            ($pp.AppParameters -and $pp.AppParameters -like '*server.py*' -and $pp.Application -like "*$Root*")) { $hit = $true }
    }
    if ($hit -and $s.Name -ne 'VMM-PostgreSQL') { $svcs += $s.Name }
}
foreach ($n in $svcs) {
    Stop-Service -Name $n -Force -ErrorAction SilentlyContinue
    $restart += "svc:$n"
    Write-Host "Stopped service '$n'."
}
Get-CimInstance Win32_Process -Filter "Name='pythonw.exe' OR Name='python.exe'" |
    Where-Object { $_.CommandLine -like '*server.py*' -and $_.CommandLine -notlike '*--check*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Write-Host "Stopped server.py (process $($_.ProcessId))." }
Start-Sleep -Seconds 2

# ---------------------------------------------------------------- 3. PostgreSQL
Step '3/5  Setting up PostgreSQL'
& $Py (Join-Path $Root 'setup_db.py')
if ($LASTEXITCODE -ne 0) { Stop-WithError 'Database setup failed (see the messages above). The tool was not switched - start it again with .\restart-service.ps1' }

# ---------------------------------------------------------------- 4. copy the data
Step '4/5  Copying the data into the database'
& $Py (Join-Path $Root 'migrate_to_db.py')
if ($LASTEXITCODE -ne 0) { Stop-WithError 'Copying the data failed (see above). The tool was not switched to the database - start it again with .\restart-service.ps1' }

# ---------------------------------------------------------------- 5. start + check
Step '5/5  Starting the tool on the database'
if ($restart.Count -eq 0) {
    Write-Host 'No background task found - installing it now.'
    & (Join-Path $Root 'install-service.ps1')
} else {
    foreach ($r in $restart) {
        if ($r -eq 'task') { Start-ScheduledTask -TaskName $TaskName; Write-Host "Started task '$TaskName'." }
        elseif ($r -like 'svc:*') { $n = $r.Substring(4); Start-Service -Name $n; Write-Host "Started service '$n'." }
    }
}
$ok = $false; $storage = ''
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 2
    try {
        $h = Invoke-RestMethod -UseBasicParsing -Uri "http://localhost:$Port/api/health" -TimeoutSec 3
        if ($h.ok) { $ok = $true; $storage = $h.storage; break }
    } catch {}
}
Write-Host ''
& $Py (Join-Path $Root 'server.py') --check
Write-Host ''
if ($ok -and $storage -eq 'postgres') {
    Write-Host "DONE. The tool is running on PostgreSQL: http://$([System.Net.Dns]::GetHostName()):$Port/" -ForegroundColor Green
    Write-Host 'Everyone signs in with the same email and password as before.'
} elseif ($ok) {
    Stop-WithError "The tool answers but reports storage '$storage' instead of postgres. Check config.json and run .\restart-service.ps1"
} else {
    Stop-WithError "The tool did not answer on http://localhost:$Port/ within 2 minutes. Check logs\server.log."
}
Write-Host "Log of this installation: $LogFile"
Stop-Transcript | Out-Null
