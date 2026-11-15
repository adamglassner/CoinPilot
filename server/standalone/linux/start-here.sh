#!/usr/bin/env bash
# Runs WillowWallet Server in this terminal (Ctrl+C stops it). Data goes in ./data next to this script.
cd "$(dirname "$0")"
chmod +x ./WillowWalletServer
exec ./WillowWalletServer "$@"
