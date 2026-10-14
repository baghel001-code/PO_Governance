<#
  Stops and removes the background task and firewall rule.
  Your data folder is NOT deleted.
  Run in PowerShell "Run as administrator":
      powershell -ExecutionPolicy Bypass -File .\uninstall-service.ps1
#>
$TaskName = 'VMM PO Governance'
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Removed scheduled task '$TaskName'."
}
# pythonw keeps running after the task is stopped in some cases - end it
Get-CimInstance Win32_Process -Filter "Name='pythonw.exe'" |
    Where-Object { $_.CommandLine -like '*server.py*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Write-Host "Stopped server process $($_.ProcessId)." }
Get-NetFirewallRule -DisplayName 'VMM PO Governance*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Write-Host 'Firewall rule removed. The data folder was left in place.'
