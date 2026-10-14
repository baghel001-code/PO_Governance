<#
  Installs the VMM PO Governance server so it runs in the background and
  starts automatically when the VM boots (a Windows scheduled task running
  as SYSTEM, restarted if it stops). Also opens the port in Windows Firewall.

  Run once, in PowerShell "Run as administrator", from this folder:
      powershell -ExecutionPolicy Bypass -File .\install-service.ps1
#>
$ErrorActionPreference = 'Stop'
$TaskName = 'VMM PO Governance'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Please run this script in PowerShell opened with "Run as administrator".' }

# -- find Python (bundled folder first, then installed) --
function Find-Pythonw {
    $bundled = Join-Path $Root 'python\pythonw.exe'
    if (Test-Path $bundled) { return $bundled }
    foreach ($cmd in @('pythonw.exe', 'python.exe')) {
        $c = Get-Command $cmd -ErrorAction SilentlyContinue
        if ($c -and $c.Source -notlike '*WindowsApps*') {
            $w = Join-Path (Split-Path $c.Source) 'pythonw.exe'
            if (Test-Path $w) { return $w }
        }
    }
    # usual install folders: all users, this user, then any user profile (newest version first)
    $roots = @("$env:ProgramFiles\Python3*", "C:\Python3*", "$env:LOCALAPPDATA\Programs\Python\Python3*", "C:\Users\*\AppData\Local\Programs\Python\Python3*")
    foreach ($r in $roots) {
        $hit = Get-ChildItem -Path $r -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending |
               Where-Object { Test-Path (Join-Path $_.FullName 'pythonw.exe') } | Select-Object -First 1
        if ($hit) { return (Join-Path $hit.FullName 'pythonw.exe') }
    }
    $py = Get-Command py.exe -ErrorAction SilentlyContinue
    if ($py) {
        $exe = & $py.Source -3 -c "import sys;print(sys.executable)" 2>$null
        if ($exe) {
            $w = Join-Path (Split-Path $exe) 'pythonw.exe'
            if (Test-Path $w) { return $w }
        }
    }
    return $null
}
$Pythonw = Find-Pythonw
if (-not $Pythonw) {
    throw 'Python not found. Install Python 3.8+ for all users, or unzip the Windows embeddable Python into a "python" folder here (see README.md), then run this again.'
}
$PyExe = Join-Path (Split-Path $Pythonw) 'python.exe'
$ver = & $PyExe -c "import sys;print('%d.%d' % sys.version_info[:2])"
if ([version]$ver -lt [version]'3.8') { throw "Python $ver found at $PyExe - version 3.8 or newer is required." }
Write-Host "Python $ver : $Pythonw"

# -- read port from config.json --
$cfg = Get-Content (Join-Path $Root 'config.json') -Raw | ConvertFrom-Json
$Port = if ($cfg.port) { [int]$cfg.port } else { 8080 }

# -- quick self-test: does the server start? --
& $PyExe -c "import ast,sys; ast.parse(open(r'$Root\server.py',encoding='utf-8').read())"
if ($LASTEXITCODE -ne 0) { throw 'server.py failed a syntax check with this Python.' }

# -- scheduled task --
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}
$action    = New-ScheduledTaskAction -Execute $Pythonw -Argument "`"$Root\server.py`"" -WorkingDirectory $Root
$trigger   = New-ScheduledTaskTrigger -AtStartup
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
               -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
               -StartWhenAvailable -MultipleInstances IgnoreNew
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
    -Description 'VMM PO Governance shared server (server.py)' | Out-Null
Start-ScheduledTask -TaskName $TaskName
Write-Host "Scheduled task '$TaskName' registered and started."

# -- firewall --
$ruleName = "VMM PO Governance (TCP $Port)"
Get-NetFirewallRule -DisplayName 'VMM PO Governance*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Domain,Private | Out-Null
Write-Host "Firewall: inbound TCP $Port allowed (Domain, Private networks)."

# -- health check --
$ok = $false
for ($i = 0; $i -lt 15; $i++) {
    Start-Sleep -Seconds 1
    try {
        $r = Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:$Port/api/health" -TimeoutSec 3
        if ($r.StatusCode -eq 200) { $ok = $true; break }
    } catch {}
}
$hostName = [System.Net.Dns]::GetHostName()
$ips = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' }).IPAddress
if ($ok) {
    Write-Host ''
    Write-Host 'Server is running. Users can open:' -ForegroundColor Green
    Write-Host "   http://$hostName`:$Port/"
    foreach ($ip in $ips) { Write-Host "   http://$ip`:$Port/" }
} else {
    Write-Warning "The task started but http://localhost:$Port/ did not answer. Check logs\server.log (a port already in use is the usual cause - change 'port' in config.json and run this again)."
}
