# Installs WillowWallet Server as a Windows service that starts with Windows.
# Right-click → Run with PowerShell (it asks for administrator rights). Data: C:\ProgramData\WillowWallet Server
$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}
$dest = Join-Path $env:ProgramFiles 'WillowWallet Server'
$data = Join-Path $env:ProgramData 'WillowWallet Server'
New-Item -ItemType Directory -Force $dest, $data | Out-Null
if (Get-Service WillowWalletServer -ErrorAction SilentlyContinue) { Stop-Service WillowWalletServer -Force; Start-Sleep 2 }
Copy-Item (Join-Path $PSScriptRoot 'WillowWalletServer.exe') $dest -Force
Copy-Item (Join-Path $PSScriptRoot 'wwwroot') $dest -Recurse -Force
$settings = Join-Path $dest 'appsettings.json'
if (-not (Test-Path $settings)) {
    @{ WillowWallet = @{ Port = 8484; DataFolder = $data } } | ConvertTo-Json | Set-Content $settings -Encoding UTF8
}
if (-not (Get-Service WillowWalletServer -ErrorAction SilentlyContinue)) {
    sc.exe create WillowWalletServer binPath= "`"$dest\WillowWalletServer.exe`"" start= delayed-auto DisplayName= "WillowWallet Server" | Out-Null
    sc.exe description WillowWalletServer "Sync server for the WillowWallet budgeting app (web panel on port 8484)." | Out-Null
}
# Restart it if it stops unexpectedly (and when restarted from the web panel)
sc.exe failure WillowWalletServer reset= 86400 actions= restart/3000/restart/3000/restart/10000 | Out-Null
sc.exe failureflag WillowWalletServer 1 | Out-Null
if (-not (Get-NetFirewallRule -DisplayName 'WillowWallet Server' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'WillowWallet Server' -Direction Inbound -Protocol TCP -LocalPort 8484 -Action Allow -Profile Private,Domain | Out-Null
}
Start-Service WillowWalletServer
Write-Host "WillowWallet Server is installed and running. Opening the web panel..."
Start-Process 'http://localhost:8484'
