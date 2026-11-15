Your own sync server for WillowWallet: keeps WillowWallet for Windows (1.1.0 and later) and WillowWallet for Android (0.2.0 and later) in step, with an account for each person and a web panel to run it. Data is end-to-end encrypted: devices lock it with your password before sending, so the server can't read it. See [server/README.md](https://github.com/adamglassner/coinpilot/blob/main/server/README.md) for setup.

**Downloads**

- **WillowWalletServer-<version>-Setup.exe**: Windows installer. Installs a Windows service, can open the firewall for your home network, and opens the web panel.
- **WillowWalletServer-<version>-win-x64.zip**: Windows without installing. Run `Start-WillowWallet-Server.cmd`.
- **WillowWalletServer-<version>-linux-x64.tar.gz** / **linux-arm64.tar.gz**: Linux and Raspberry Pi. `sudo ./install.sh` sets up a systemd service.
- **Docker**: `docker run -d --name willowwallet --restart unless-stopped -p 8484:8484 -v willowwallet-data:/data ghcr.io/adamglassner/willowwallet-server:latest`

Then open `http://<server>:8484` to create the admin account, and make an invite for each person.

**What's in 0.1.0**

- Sync for budgets, transactions, accounts, debts, bills, goals, tax documents, settings, receipts and tax files.
- Accounts with invite codes or open sign-up, a user limit, recovery keys for forgotten passwords, and per-device sign-out.
- Web panel: start, stop and restart; requests chart; users and devices; invites; activity and server logs; backup; light and dark.
- Runs on Windows, Linux (x64, ARM64) and Docker; optional built-in HTTPS.
