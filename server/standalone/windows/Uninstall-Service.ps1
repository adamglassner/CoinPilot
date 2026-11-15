# Removes the WillowWallet Server Windows service. Your data in C:\ProgramData\WillowWallet Server is kept.
$ErrorActionPreference = 'SilentlyContinue'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}
Stop-Service WillowWalletServer -Force
sc.exe delete WillowWalletServer | Out-Null
Remove-NetFirewallRule -DisplayName 'WillowWallet Server'
Remove-Item (Join-Path $env:ProgramFiles 'WillowWallet Server') -Recurse -Force
Write-Host "WillowWallet Server removed. Your data is still in C:\ProgramData\WillowWallet Server."
