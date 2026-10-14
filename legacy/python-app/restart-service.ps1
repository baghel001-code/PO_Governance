<#
  Restarts the server, e.g. after editing config.json or replacing app\index.html.
  Run in PowerShell "Run as administrator":
      powershell -ExecutionPolicy Bypass -File .\restart-service.ps1
#>
$TaskName = 'VMM PO Governance'
Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='pythonw.exe'" |
    Where-Object { $_.CommandLine -like '*server.py*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Start-Sleep -Seconds 2
Start-ScheduledTask -TaskName $TaskName
Write-Host "Restarted '$TaskName'. Log: logs\server.log"
