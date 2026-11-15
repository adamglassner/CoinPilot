#!/usr/bin/env bash
# Removes WillowWallet Server. Your data in /var/lib/willowwallet-server is kept unless you add --delete-data.
#   sudo willowwallet-server-uninstall  (or sudo ./uninstall.sh from the download)
#   sudo ./uninstall.sh                 remove the server, keep the data
#   sudo ./uninstall.sh --delete-data   remove the server and every account and synced record on it
set -euo pipefail
if [ "$(id -u)" -ne 0 ]; then echo "Run this with sudo: sudo ./uninstall.sh"; exit 1; fi
purge=false
[ "${1:-}" = "--delete-data" ] && purge=true
systemctl disable --now willowwallet-server 2>/dev/null || true
rm -f /etc/systemd/system/willowwallet-server.service
systemctl daemon-reload
rm -rf /opt/willowwallet-server
rm -f /usr/local/sbin/willowwallet-server-uninstall
if $purge; then
  rm -rf /var/lib/willowwallet-server
  userdel willowwallet 2>/dev/null || true
  echo "WillowWallet Server and its data were removed."
else
  echo "WillowWallet Server removed. Data kept in /var/lib/willowwallet-server (run with --delete-data to remove it too)."
fi
