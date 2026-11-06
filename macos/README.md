# WiiVC Injector for macOS (Avalonia, arm64)

## Build

Requirements: macOS on Apple Silicon, .NET 10 SDK, JDK 17 (for `jlink`/`jdeps`), Python 3 with Pillow,
and a tools tree (`bin/wit`, `bin/getexttypepatcher`, `jar/JNUSTool.jar`, `jar/NUSPacker.jar`, `BASE`, `DOL`,
`share`, `nfs-src`).

    TOOLS_SRC=/path/to/tools VERSION=1.0.0 macos/package.sh

Output: `dist/WiiVCInjector.app` and `dist/WiiVCInjector-macos-arm64.zip` (`dist/` is gitignored).
Defaults: `TOOLS_SRC=/Volumes/Renato/Wii U/tcmoon-mac/tools`, `JDK_HOME` = Homebrew `openjdk@17`.

The app is published self-contained (no .NET install needed) and bundles a jlink-reduced Java runtime,
so it does not depend on Homebrew. The managed app lives in `Contents/Resources/app`; `Contents/MacOS/WiiVCInjector`
is a small launcher script (keeps `codesign --verify --deep --strict` clean).

`.github/workflows/macos-release.yml` builds on tag `v*`. It expects a release asset `wiivc-mac-tools.tar.gz`
(tools tree without `jre/` and `nfs/`, with `nfs-src/`) in this repo. It has not been run.

## First run

The app is ad-hoc signed, not notarized. On first launch right click the app and choose Open, or run:

    xattr -dr com.apple.quarantine WiiVCInjector.app

## Headless render check

    WIIVC_SNAPSHOT=/tmp/x.png WiiVCInjector.app/Contents/MacOS/WiiVCInjector
