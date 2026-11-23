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

CI (`.github/workflows/macos-ci.yml`) builds the app, runs the UI logic self-test and renders every page in both themes
on each pull request that touches `macos/`. The release zip is built locally with `package.sh` and uploaded by hand.

## Install

The simplest way, which avoids the Gatekeeper block below (`curl` does not set the quarantine flag):

    curl -fsSL https://raw.githubusercontent.com/orenatobr/TeconmoonWiiVCInjector/main/macos/install.sh | bash

It downloads the latest release and installs it in `/Applications`.

## First run

The app is ad-hoc signed, not notarized, so recent macOS versions block it with "WiiVCInjector.app was not opened"
(right click > Open no longer bypasses this). Click **OK** (not "Move to Trash") and do one of:

- System Settings > Privacy & Security, scroll to Security, click **Open Anyway** next to the WiiVCInjector message and confirm; or
- run this in Terminal (adjust the path):

      xattr -dr com.apple.quarantine ~/Downloads/WiiVCInjector.app

Removing the quarantine flag is the only thing the command does. Notarization needs a paid Apple Developer account and is not set up.

## UI

The three windows (`MainWindow`, `SettingsWindow`, `SDCardWindow`) are hand-written Avalonia XAML in a Fluent look:
cards, a step list on the left, mode tiles on top, stroke icons, and light or dark following the system theme.
Colours, icons and control styles live in `App.axaml` (theme dictionaries `Light` and `Dark`); event handlers live in the
matching `*.axaml.cs` files, and the control names are the ones from the WinForms designer.

![Light](docs/ui-light.png)
![Dark](docs/ui-dark.png)

Render a page without opening a window (`WIIVC_THEME=light|dark` pins the theme, `WIIVC_TAB=0..4` picks the page,
`WIIVC_WINDOW=settings|sdcard` renders another window, `WIIVC_SETTINGS=/tmp/x.json` keeps your real keys out of the picture):

    WIIVC_SETTINGS=/tmp/x.json WIIVC_THEME=dark WIIVC_TAB=2 WIIVC_SNAPSHOT=/tmp/x.png WiiVCInjector.app/Contents/MacOS/WiiVCInjector

## Converting several games at once

On the Source files tab, "Choose games and convert..." (Wii Retail or GC Retail modes; the keys must already be saved on the Build tab) builds every game you pick in one run.
All of them use the GamePad Emulation Mode and the advanced options currently set in the window, and the icon and banner of
each game are always downloaded from Cucholix's repo. A game the repo has no images for, a wrong system, a Wii NKit/NASOS image
or a failed build is skipped and listed in the final summary; the other games still convert.

## Differences from the Windows app

- Settings and keys are stored in `~/Library/Application Support/WiiVCInjector/settings.json` instead of the registry.
- Wii-VMC: the external wii-vmc.exe window is replaced by a dialog that picks the target video mode
  (NTSC 480i, PAL60, PAL50, 480p). It patches the video mode tables of the game's main.dol (experimental).
- C2W patch: ported to C# (`Engine/C2wPatcher.cs`, from FIX94's c2w_patcher). Needs your own ancast key. Verified only on a synthetic image.
- Wii NKit and NASOS input (the original's NKit converter) is not supported on macOS; use ISO or WBFS.
- GameCube images can be an ISO/GCM, a `.ciso`, a `.rvz` or a GameCube `.nkit.iso`. The build turns them into the plain `game.iso` that Nintendont expects
  (the same goes for the 2nd disc). A CISO is unpacked by the app itself. RVZ and NKit drop the junk padding of a disc and regenerate it on read, so they go
  through the [NKit](https://github.com/Nanook/NKit) command line tool (MIT): the first time one is needed the app downloads release 3.2.2 (about 18 MB) into
  `~/Library/Caches/WiiVCInjector/tools/`, checks it against a fixed SHA-256 and runs `nkit expand`. Wii RVZ is not supported. `WIIVC_NKIT=/path/to/nkit` uses another executable.
- `WIIVC_GUIBUILD=1` drives the real window (mode, game load, images, Build tab, BUILD button) through a homebrew build; keys come from WIIVC_COMMON_KEY and WIIVC_TITLE_KEY.
- Engine self-test (`WIIVC_SELFTEST=1`) also runs WBFS, GameCube (synthetic header) and WiiWare builds; none used a real game.
- Boot sound and TGA conversion are done in C# (no sox, no png2tgacmd); not compared byte for byte with the originals.
- Boot sound preview uses `afplay`; TGA images are decoded in-process.
- Nintendont SD card menu: SD cards are the volumes mounted under `/Volumes`. The "format" link opens Disk Utility.
- The Nintendont SD card menu installs the Teconmoon build of Nintendont by default (see `nintendont-teconmoon/README.md`): in game, L + R + A switches the Wii U
  screen between 4:3 and 16:9, and PAL discs ignore Force Progressive, Patch PAL50 and forced 60 Hz modes so their audio stays in sync. A box installs the plain upstream build instead.

## Checks

- `WIIVC_SNAPSHOT=out.png [WIIVC_TAB=n] dotnet bin/Release/net10.0/WiiVCInjector.dll` renders a tab to a PNG and exits.
- `WIIVC_UITEST=1 dotnet bin/Release/net10.0/WiiVCInjector.dll` runs the UI logic self-test (uses a temporary settings file).
- `WIIVC_GCCONVERT=game.rvz [WIIVC_GCOUT=out.iso] WIIVC_SELFTEST=1 dotnet bin/Release/net10.0/WiiVCInjector.dll` unpacks a GameCube image (ISO, CISO, RVZ or NKit) with the code the build uses, walks the disc's file table and prints the SHA-1. The same game in CISO, RVZ and NKit must print the same SHA-1; an RVZ or NKit image downloads the NKit tool the first time.
