#!/usr/bin/env bash
# Removes the WillowWallet Server service. Your data in /var/lib/willowwallet-server is kept.
set -euo pipefail
if [ "$(id -u)" -ne 0 ]; then echo "Run this with sudo: sudo ./uninstall.sh"; exit 1; fi
systemctl disable --now willowwallet-server 2>/dev/null || true
rm -f /etc/systemd/system/willowwallet-server.service
systemctl daemon-reload
rm -rf /opt/willowwallet-server
echo "WillowWallet Server removed. Data kept in /var/lib/willowwallet-server (delete it to remove everything)."
