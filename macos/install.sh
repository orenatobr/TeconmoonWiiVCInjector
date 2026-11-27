#!/bin/bash
# Installs the latest TeconMoonWiiVCInjector release into /Applications without the
# quarantine flag, so Gatekeeper does not reject the ad-hoc signed app.
set -euo pipefail
url="https://github.com/orenatobr/TeconmoonWiiVCInjector/releases/latest/download/TeconMoonWiiVCInjector-macos-arm64.zip"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
curl -fL --progress-bar -o "$tmp/app.zip" "$url"
curl -fsSL -o "$tmp/app.zip.sha256" "$url.sha256"
[ "$(shasum -a 256 "$tmp/app.zip" | cut -d" " -f1)" = "$(cut -d" " -f1 "$tmp/app.zip.sha256")" ] || { echo "SHA-256 mismatch, aborting" >&2; exit 1; }
ditto -x -k "$tmp/app.zip" "$tmp/out"
app=$(find "$tmp/out" -maxdepth 2 -name 'TeconMoonWiiVCInjector.app' | head -1)
rm -rf /Applications/TeconMoonWiiVCInjector.app /Applications/WiiVCInjector.app
ditto "$app" /Applications/TeconMoonWiiVCInjector.app
xattr -dr com.apple.quarantine /Applications/TeconMoonWiiVCInjector.app 2>/dev/null || true
echo "Installed: /Applications/TeconMoonWiiVCInjector.app"
