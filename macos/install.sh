#!/bin/bash
# Installs the latest WiiVCInjector release into /Applications without the
# quarantine flag, so Gatekeeper does not reject the ad-hoc signed app.
set -euo pipefail
url="https://github.com/orenatobr/TeconmoonWiiVCInjector/releases/latest/download/WiiVCInjector-macos-arm64.zip"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
curl -fL --progress-bar -o "$tmp/app.zip" "$url"
ditto -x -k "$tmp/app.zip" "$tmp/out"
app=$(find "$tmp/out" -maxdepth 2 -name 'WiiVCInjector.app' | head -1)
rm -rf /Applications/WiiVCInjector.app
ditto "$app" /Applications/WiiVCInjector.app
xattr -dr com.apple.quarantine /Applications/WiiVCInjector.app 2>/dev/null || true
echo "Installed: /Applications/WiiVCInjector.app"
