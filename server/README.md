# WillowWallet Server

Your own sync server for WillowWallet. It keeps WillowWallet for Windows and WillowWallet for Android in step, with
an account for each person in your household, and a web panel to start, stop and watch it.

- **End-to-end encrypted.** Each device encrypts your data with a key made from your password before sending it.
  The server stores locked records and can't read your budgets, transactions, receipts or tax documents. Not even
  the person who runs the server can.
- **Optional.** Both apps work on their own with everything saved on the device. Turning sync on doesn't move your
  data off the device: each device keeps a full copy and syncs when the server can be reached.
- **Small.** One program with a SQLite database. Runs on a Windows PC, a Linux box, a Raspberry Pi, a NAS or a VPS.

## Install

Pick one. All of them listen on port **8484** and keep their data in one folder you can back up.

### Windows

Download **WillowWalletServer-<version>-Setup.exe** from the
[latest server release](https://github.com/adamglassner/coinpilot/releases?q=server-v) (or click **Get server** on
WillowWallet in the Bytehawk Studio Launcher) and run it. It installs a Windows service that starts with Windows,
can open the firewall for your home network, and opens the web panel when it's done.

Data: `C:\ProgramData\WillowWallet Server`.

### Docker

```sh
docker run -d --name willowwallet --restart unless-stopped \
  -p 8484:8484 -v willowwallet-data:/data \
  ghcr.io/adamglassner/willowwallet-server:latest
```

or with Compose, using [docker-compose.yml](docker-compose.yml): `docker compose up -d`. The image is built for
x64 and ARM64 and has a health check.

### Linux (standalone)

Download **WillowWalletServer-<version>-linux-x64.tar.gz** (or `linux-arm64` for a Raspberry Pi 4/5), unpack it and
run `sudo ./install.sh`. That installs it to `/opt/willowwallet-server` as a systemd service (data in
`/var/lib/willowwallet-server`). To try it without installing, run `./start-here.sh`.

### Windows without installing

Unzip **WillowWalletServer-<version>-win-x64.zip** and double-click `Start-WillowWallet-Server.cmd`. Data goes in the
`data` folder next to it. `Install-Service.ps1` turns it into a Windows service later.

## Uninstall

Uninstalling keeps the server's data (every account and everything synced) unless you choose to delete it, so a
reinstall picks up where it left off. Phones and PCs always keep their own copy.

- **Windows installer:** Start Menu → **Uninstall WillowWallet Server**, Settings → Apps, or **Get server →
  Uninstall** in the Bytehawk Studio Launcher. It asks whether to delete the data too (default: keep it).
  Silent: `"C:\Program Files\WillowWallet Server\unins000.exe" /SILENT /DELETEDATA=1` (leave out `/DELETEDATA=1`
  to keep the data).
- **Docker:** `docker rm -f willowwallet`, then `docker volume rm willowwallet-data` to delete the data
  (with Compose: `docker compose down`, and delete the `willowwallet-data` folder).
- **Linux:** `sudo willowwallet-server-uninstall` (servers installed with 0.1.0: `sudo ./uninstall.sh` from the
  download); add `--delete-data` to remove `/var/lib/willowwallet-server` too.
- **Windows without installing:** close it (or run `Uninstall-Service.ps1` if you made it a service; add
  `-DeleteData` to remove the data), then delete the folder.

## First run

Open `http://<server>:8484` in a browser. The first visit asks you to create the **admin** account for the web
panel (this is separate from the WillowWallet accounts people sync with).

Then, from the panel:

1. **Invites** → create an invite code for each person, or turn on **Open sign-up** in **Settings**.
2. On the PC: WillowWallet → **Sync & Devices** → enter the server address, choose **Create account**, and save the
   recovery key it shows.
3. On the phone: **Settings → Sync** → the same server address, **Sign in** with that account.

The panel's **Overview** shows the address to type on your devices, whether syncing is running, requests over the
last hour, users, devices and recent activity. **Stop** pauses syncing (devices keep their data and catch up later),
**Restart** restarts the server. **Users** lets you turn accounts off, sign out their devices, or erase what's
synced. **Logs** has the activity log and the server's own log. **Settings → Backup** downloads a copy of the
database.

## Reaching it from outside your home

The apps talk plain HTTP on your home network. To sync from anywhere, put the server behind HTTPS: a reverse proxy
(Caddy, nginx, Traefik), a tunnel (Tailscale, Cloudflare Tunnel), or built-in HTTPS with your own certificate:

| Setting | Environment variable | Default |
| --- | --- | --- |
| Port | `WW_PORT` | `8484` |
| Data folder | `WW_DATA` | `data` next to the program (`/data` in Docker) |
| HTTPS port | `WW_HTTPS_PORT` | off |
| HTTPS certificate (.pfx) | `WW_HTTPS_CERT`, `WW_HTTPS_CERT_PASSWORD` | |
| First admin, without the setup page | `WW_ADMIN_USERNAME`, `WW_ADMIN_PASSWORD` | |

The same settings can go in `appsettings.json` under `"WillowWallet"` (`Port`, `DataFolder`, `HttpsPort`,
`HttpsCertificate`, `HttpsCertificatePassword`).

## Forgotten passwords

- **A WillowWallet account:** use **Forgot your password?** in the app with the recovery key saved when the account
  was made. The server can't reset it: without the password or the recovery key, the data can't be unlocked. The
  admin can still erase the account's synced data so it can start over from a device that has it.
- **The web panel admin:** stop the server, start it once with `WW_ADMIN_USERNAME` and `WW_ADMIN_PASSWORD` set to a
  new admin, and sign in with that.

## How it works

See [PROTOCOL.md](PROTOCOL.md) for the API and the encryption. In short: records (transactions, budgets, accounts,
bills, goals, debts, settings…) are sealed one by one with AES-256-GCM on the device and sent with a timestamp. The
server gives each change a sequence number; devices send what changed and fetch everything after the last number
they saw. When two devices change the same record, the newest change wins. Deletes are kept as markers so every
device sees them. Receipts and tax documents are sealed the same way and stored as files.

## Building

```sh
dotnet test server/WillowWallet.Server.Tests
dotnet run --project server/WillowWallet.Server         # http://localhost:8484
docker build -f server/Dockerfile -t willowwallet-server .
```

The [Server workflow](../.github/workflows/server.yml) tests it, builds the standalone packages, the Docker image
and the Windows installer, takes screenshots of the web panel against a server with demo data, and publishes a
`server-v<version>` release (never marked "latest", so the launcher keeps installing the app) when the version in
`WillowWallet.Server.csproj` changes on `main`.
