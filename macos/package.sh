#!/bin/bash
# Builds dist/WiiVCInjector.app (arm64, self-contained, ad-hoc signed) and a zip.
# Env: VERSION (default 1.0.0), TOOLS_SRC (dir with bin jar BASE DOL share, and nfs-src unless
# NFS_SRC set; default: the local tcmoon-mac/tools), JDK_HOME (default openjdk@17 from Homebrew).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="${VERSION:-1.0.0}"
TOOLS_SRC="${TOOLS_SRC:-/Volumes/Renato/Wii U/tcmoon-mac/tools}"
NFS_SRC="${NFS_SRC:-$TOOLS_SRC/nfs-src}"
JDK_HOME="${JDK_HOME:-$(/usr/libexec/java_home -v 17 2>/dev/null || echo /opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home)}"
DIST="$ROOT/dist"
APP="$DIST/WiiVCInjector.app"
RES="$APP/Contents/Resources"
TOOLS="$RES/tools"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

rm -rf "$APP" "$DIST/WiiVCInjector-macos-arm64.zip"
mkdir -p "$APP/Contents/MacOS" "$TOOLS"/{bin,jar}

echo "==> publish app"
dotnet publish "$ROOT/macos/WiiVCInjector" -c Release -r osx-arm64 --self-contained true -o "$WORK/app" -v q
# Managed dlls in Contents/MacOS break `codesign --verify --strict` (unsigned subcomponents), so the
# app lives in Resources/app and MacOS/WiiVCInjector is a tiny launcher that execs it.
mkdir -p "$RES/app"
cp -R "$WORK/app/." "$RES/app/"
rm -f "$RES/app/"*.pdb
cat > "$APP/Contents/MacOS/WiiVCInjector" <<'LAUNCH'
#!/bin/bash
exec "$(cd "$(dirname "$0")/../Resources/app" && pwd)/WiiVCInjector" "$@"
LAUNCH
chmod +x "$APP/Contents/MacOS/WiiVCInjector"

echo "==> Info.plist + icon"
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>WiiVC Injector</string>
<key>CFBundleDisplayName</key><string>WiiVC Injector</string>
<key>CFBundleIdentifier</key><string>com.orenatobr.wiivcinjector</string>
<key>CFBundleExecutable</key><string>WiiVCInjector</string>
<key>CFBundleIconFile</key><string>WiiVCInjector</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleVersion</key><string>$VERSION</string>
<key>CFBundleShortVersionString</key><string>$VERSION</string>
<key>LSMinimumSystemVersion</key><string>12.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
python3 "$ROOT/macos/make_icns.py" "$ROOT/TeconMoon's WiiVC Injector/WiiU.ico" "$RES/WiiVCInjector.icns"

echo "==> tools"
# copy without AppleDouble / .DS_Store (source may be exFAT)
cp_clean() { (cd "$1" && tar -cf - --exclude '._*' --exclude .DS_Store "${@:3}" .) | tar -xf - -C "$2"; }
for f in bin/wit bin/getexttypepatcher jar/JNUSTool.jar jar/NUSPacker.jar; do cp "$TOOLS_SRC/$f" "$TOOLS/$f"; done
for d in BASE DOL share; do mkdir -p "$TOOLS/$d"; cp_clean "$TOOLS_SRC/$d" "$TOOLS/$d"; done
chmod +x "$TOOLS/bin/"*

echo "==> nfs2iso2nfs (self-contained)"
mkdir -p "$WORK/nfs-src"
cp "$NFS_SRC/Program.cs" "$NFS_SRC/nfs2iso2nfs.csproj" "$WORK/nfs-src/"
sh "$(dirname "$0")/tools/patch_nfs_source.sh" "$WORK/nfs-src/Program.cs"
dotnet publish "$WORK/nfs-src" -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o "$TOOLS/nfs" -v q
rm -f "$TOOLS/nfs/"*.pdb

echo "==> jre (jlink)"
MODS="$("$JDK_HOME/bin/jdeps" --multi-release 17 --ignore-missing-deps --print-module-deps "$TOOLS/jar/JNUSTool.jar" "$TOOLS/jar/NUSPacker.jar")"
"$JDK_HOME/bin/jlink" --add-modules "$MODS" --strip-debug --no-header-files --no-man-pages --compress=2 --output "$TOOLS/jre"

find "$APP" \( -name '._*' -o -name .DS_Store \) -delete

# jlink copies read-only (0444) files; make them writable so users can run xattr -dr or delete the app
chmod -R u+w "$APP"

echo "==> codesign (ad-hoc)"
# sign every Mach-O (nested first), then the bundle
find "$APP/Contents/Resources" -type f | while read -r f; do
  if file -b "$f" | grep -q 'Mach-O'; then codesign --force -s - "$f" 2>/dev/null; fi
done
codesign --force -s - "$APP/Contents/MacOS/WiiVCInjector"
codesign --force --deep -s - "$APP"
codesign --verify --deep --strict "$APP" && echo "codesign verify OK"

echo "==> zip"
ditto -c -k --keepParent "$APP" "$DIST/WiiVCInjector-macos-arm64.zip"
ls -lh "$DIST/WiiVCInjector-macos-arm64.zip"
