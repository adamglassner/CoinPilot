# Removes the WillowWallet Server Windows service set up by Install-Service.ps1.
# Your data in C:\ProgramData\WillowWallet Server is kept unless you add -DeleteData.
# (If you used the Setup installer, uninstall from Settings > Apps or the Start Menu instead.)
param([switch]$DeleteData)
$ErrorActionPreference = 'SilentlyContinue'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $extra = if ($DeleteData) { ' -DeleteData' } else { '' }
    Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File `"$PSCommandPath`"$extra"
    exit
}
Stop-Service WillowWalletServer -Force
Start-Sleep -Seconds 2
sc.exe delete WillowWalletServer | Out-Null
Remove-NetFirewallRule -DisplayName 'WillowWallet Server'
Remove-Item (Join-Path $env:ProgramFiles 'WillowWallet Server') -Recurse -Force
$data = Join-Path $env:ProgramData 'WillowWallet Server'
if ($DeleteData) {
    Remove-Item $data -Recurse -Force
    Write-Host "WillowWallet Server and its data were removed."
} else {
    Write-Host "WillowWallet Server removed. Your data is still in $data (run with -DeleteData to remove it too)."
}
