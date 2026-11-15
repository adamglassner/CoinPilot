@echo off
rem Runs WillowWallet Server in this window (close it to stop). Data goes in the "data" folder next to this file.
cd /d "%~dp0"
start "" http://localhost:8484
WillowWalletServer.exe
