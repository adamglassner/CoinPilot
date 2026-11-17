Your own sync server for WillowWallet: keeps WillowWallet for Windows, Mac, Linux and Android in step, with an account for each person and a web panel to run it. Data is end-to-end encrypted: devices lock it with your password before sending, so the server can't read it. See [server/README.md](https://github.com/adamglassner/coinpilot/blob/main/server/README.md) for setup.

**Downloads**

- **WillowWalletServer-<version>-Setup.exe**: Windows installer. Installs a Windows service, can open the firewall for your home network, and opens the web panel.
- **WillowWalletServer-<version>-win-x64.zip**: Windows without installing. Run `Start-WillowWallet-Server.cmd`.
- **WillowWalletServer-<version>-linux-x64.tar.gz** / **linux-arm64.tar.gz**: Linux and Raspberry Pi. `sudo ./install.sh` sets up a systemd service.
- **Docker**: `docker run -d --name willowwallet --restart unless-stopped -p 8484:8484 -v willowwallet-data:/data ghcr.io/adamglassner/willowwallet-server:latest`

Then open `http://<server>:8484` to create the admin account, and make an invite for each person.

**What's new in 1.6.0**

- Version numbers now match the apps: everything WillowWallet makes is 1.6.0 (no change to the sync protocol; 0.1.x servers keep working with the new apps).
- Works with the new WillowWallet for Mac and Linux, which show up under Devices as macOS and Linux.

**0.1.1**

- **Uninstall** from the Start Menu (Uninstall WillowWallet Server), Settings → Apps, or Get server → Uninstall in the Bytehawk Studio Launcher. It asks whether to delete the server's data too; the default keeps it so a reinstall picks up where it left off. Silent: `unins000.exe /SILENT /DELETEDATA=1`.
- Linux: `install.sh` adds `sudo willowwallet-server-uninstall` (with `--delete-data` to remove the data too). `Uninstall-Service.ps1` takes `-DeleteData`.
- Uninstalling removes the web panel shortcuts too.

**First release (0.1.0)**

- Sync for budgets, transactions, accounts, debts, bills, goals, tax documents, settings, receipts and tax files.
- Accounts with invite codes or open sign-up, a user limit, recovery keys for forgotten passwords, and per-device sign-out.
- Web panel: start, stop and restart; requests chart; users and devices; invites; activity and server logs; backup; light and dark.
- Runs on Windows, Linux (x64, ARM64) and Docker; optional built-in HTTPS.
