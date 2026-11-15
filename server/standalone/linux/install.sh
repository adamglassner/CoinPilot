#!/usr/bin/env bash
# Installs WillowWallet Server as a systemd service:  sudo ./install.sh
# Program: /opt/willowwallet-server   Data: /var/lib/willowwallet-server   Port: 8484
set -euo pipefail
if [ "$(id -u)" -ne 0 ]; then echo "Run this with sudo: sudo ./install.sh"; exit 1; fi
here="$(cd "$(dirname "$0")" && pwd)"

id willowwallet >/dev/null 2>&1 || useradd --system --home /var/lib/willowwallet-server --shell /usr/sbin/nologin willowwallet
mkdir -p /opt/willowwallet-server /var/lib/willowwallet-server
systemctl stop willowwallet-server 2>/dev/null || true
cp -r "$here/WillowWalletServer" "$here/wwwroot" /opt/willowwallet-server/
[ -f /opt/willowwallet-server/appsettings.json ] || cp "$here/appsettings.json" /opt/willowwallet-server/
chmod +x /opt/willowwallet-server/WillowWalletServer
chown -R willowwallet:willowwallet /var/lib/willowwallet-server
cp "$here/willowwallet-server.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now willowwallet-server

ip=$(hostname -I 2>/dev/null | awk '{print $1}')
echo
echo "WillowWallet Server is running."
echo "  Web panel:  http://${ip:-localhost}:8484"
echo "  Status:     systemctl status willowwallet-server"
echo "  Logs:       journalctl -u willowwallet-server -f"
echo "If a firewall is on, allow port 8484 (for example: sudo ufw allow 8484/tcp)."
