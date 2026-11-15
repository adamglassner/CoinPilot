# Starts a WillowWallet Server for the Windows app's sync self-test and screenshots.
# GitHub's Windows runners stop background processes when a step ends, so each step that
# needs the server calls this first. Usage: ./tools/start-test-server.ps1 <data folder name>
param([string]$Name = 'ww-server-data')
$bin = Join-Path $env:RUNNER_TEMP 'ww-server-bin\WillowWalletServer.exe'
$env:WW_DATA = Join-Path $env:RUNNER_TEMP $Name
$env:WW_ADMIN_USERNAME = 'admin'
$env:WW_ADMIN_PASSWORD = 'self-test-admin-password'
$log = Join-Path $env:RUNNER_TEMP "$Name.log"
Start-Process $bin -RedirectStandardOutput $log -RedirectStandardError "$log.err" -WindowStyle Hidden | Out-Null
for ($i = 0; $i -lt 40; $i++) {
  try { Invoke-RestMethod http://localhost:8484/api/v1/info | Out-Null; Write-Host "WillowWallet Server is up ($Name)"; return } catch { Start-Sleep 1 }
}
Get-Content $log, "$log.err" -ErrorAction SilentlyContinue
throw 'WillowWallet Server did not start'
