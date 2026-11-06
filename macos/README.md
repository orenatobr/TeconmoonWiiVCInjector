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

The app is ad-hoc signed, not notarized, so recent macOS versions block it with "WiiVCInjector.app was not opened"
(right click > Open no longer bypasses this). Click **OK** (not "Move to Trash") and do one of:

- System Settings > Privacy & Security, scroll to Security, click **Open Anyway** next to the WiiVCInjector message and confirm; or
- run this in Terminal (adjust the path):

      xattr -dr com.apple.quarantine ~/Downloads/WiiVCInjector.app

Removing the quarantine flag is the only thing the command does. Notarization needs a paid Apple Developer account and is not set up.

## Headless render check

    WIIVC_SNAPSHOT=/tmp/x.png WiiVCInjector.app/Contents/MacOS/WiiVCInjector

## UI port

UI port of TeconMoon's WiiVC Injector. The three windows (`MainWindow`, `SettingsWindow`, `SDCardWindow`)
are generated from the WinForms designers by `macos/tools/regen.sh` (never hand-edit the `.axaml` files).
Event handlers live in the matching `*.axaml.cs` files.

## Differences from the Windows app

- Settings and keys are stored in `~/Library/Application Support/WiiVCInjector/settings.json` instead of the registry.
- Wii-VMC: the external wii-vmc.exe window is replaced by a dialog that picks the target video mode
  (NTSC 480i, PAL60, PAL50, 480p). It patches the video mode tables of the game's main.dol (experimental).
- C2W patch: ported to C# (`Engine/C2wPatcher.cs`, from FIX94's c2w_patcher). Needs your own ancast key. Verified only on a synthetic image.
- NKit/NASOS input (the original's NKit converter) is not supported on macOS; use ISO or WBFS.
- `WIIVC_GUIBUILD=1` drives the real window (mode, game load, images, Build tab, BUILD button) through a homebrew build; keys come from WIIVC_COMMON_KEY and WIIVC_TITLE_KEY.
- Engine self-test (`WIIVC_SELFTEST=1`) also runs WBFS, GameCube (synthetic header) and WiiWare builds; none used a real game.
- Boot sound and TGA conversion are done in C# (no sox, no png2tgacmd); not compared byte for byte with the originals.
- Boot sound preview uses `afplay`; TGA images are decoded in-process.
- Nintendont SD card menu: SD cards are the volumes mounted under `/Volumes`. The "format" link opens Disk Utility.

## Checks

- `WIIVC_SNAPSHOT=out.png [WIIVC_TAB=n] dotnet bin/Release/net10.0/WiiVCInjector.dll` renders a tab to a PNG and exits.
- `WIIVC_UITEST=1 dotnet bin/Release/net10.0/WiiVCInjector.dll` runs the UI logic self-test (uses a temporary settings file).
