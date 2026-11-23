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
- Nintendont SD card menu: the button "Download Latest Teconmoon Nintendont from GitHub" fetches `boot.dol`, `meta.xml` and `icon.png` from this project's `main` branch, checks they look right (a DOL, the app metadata, a PNG, so an error page is never installed), and installs the build bundled with the app instead when there is no connection. The upstream box installs the plain upstream build.
- Nintendont SD card menu: SD cards are the volumes mounted under `/Volumes`. The "format" link opens Disk Utility.
- The Nintendont SD card menu installs the Teconmoon build of Nintendont by default (see `nintendont-teconmoon/README.md`): PAL discs ignore Force Progressive,
  Patch PAL50 and forced 60 Hz modes so their audio stays in sync. A box installs the plain upstream build instead.

## Game settings (GameCube)

The button "Game settings..." (GC Retail mode, under the cheat codes button) sets options for one game only, in the format of Dolphin's GameINI. Each option can follow the Nintendont
setting, be forced on or be forced off: Fullscreen (`AspectRatio = 1` or `2`, Nintendont's "WiiU Widescreen"), Widescreen hack (`wideScreenHack`, "Force Widescreen"), Progressive scan
(`ProgressiveScan`, "Force Progressive"), Skip IPL (`SkipIPL`), and three keys of this app that Dolphin does not have: Patch PAL50 (`PatchPAL50`), Video mode (`VideoMode`: Auto, PAL50, PAL60, NTSC or MPAL) and the Deflicker filter (`Deflicker`, with a forced video mode). The window shows the text of the file. "Save" keeps it in `~/Library/Application Support/WiiVCInjector/gamesettings/<id4>.ini`,
"Copy to SD card..." puts it at `teconmoon/<id4>.ini` on the card, and installing Nintendont from the SD Card menu copies every saved file. The Teconmoon Nintendont applies it when that game
starts, also from a WUP, without rebuilding the WUP and without changing `nincfg.bin` (see `nintendont-teconmoon/README.md`).

- "Fullscreen" is Nintendont's own Wii U widescreen option: the Wii U shows the game on the whole 16:9 screen, and the picture is stretched unless the game draws in 16:9. For that use a
  widescreen Gecko code (the Skies of Arcadia wiki page has one) or the Widescreen hack where Nintendont can find the game's projection code (it cannot in Skies of Arcadia, its log says
  `C_MTXPerspective not found`). This is applied when the game starts, not switched while it runs: the L + R + A shortcut of 1.2.10 is gone and there is no replacement.
- A PAL disc normally ignores Progressive scan and Patch PAL50 (`AUDIO-DELAY.md`), which leaves it on a 528 line interlaced mode with black bars above and below and a soft picture. Setting either one On in the
  game's own settings keeps it for that game. Progressive scan without Patch PAL50, together with a Gecko code that makes the game run at 60 Hz, is the combination meant to give a sharp full height picture without
  the sound delay; that is not measured.
- A forced video mode is kept on a PAL disc when set here. PAL50 keeps the game at 50 Hz, where the sound was in sync, and with the deflicker filter Off the interlaced picture is sharper (thin lines may flicker). Not measured.
- Dolphin keys other than these are ignored, so a Dolphin `GameSettings/<id>.ini` can be used as it is.
- The GamePad staying on with the wiiscreenoff plugin is not covered by this. The kernel now logs what it does with the Wii U aspect registers (`WiiU aspect:` lines in `ndebug.log`), which is the
  first thing to read if Nintendont turns out to interfere with that plugin.
- GameCube only. Release 1.2.14 did not apply the file at all (the loader read it after closing the card); 1.2.15 does. Not confirmed on a console yet.

## Cheat codes (GameCube and Wii)

The button "Download from https://dolphin-emu.org/compat/" (next to the game file, Wii Retail or GC Retail mode) opens a list of Gecko and Action Replay codes for the loaded game.

**GameCube:** the ticked codes are written as `codes/<id6>.gct`, the file Nintendont loads. The Teconmoon Nintendont (SD Card menu) turns the Gecko code handler on by itself for a game
started from a WUP when that file is on the card, so the WUP does not need rebuilding. "Copy to SD card..." in the list window writes the file to the card; installing
Nintendont from the SD Card menu also copies every saved `.gct`.

- Where the codes come from: the compatibility list at `dolphin-emu.org/compat` only lists ratings and links each game to its Dolphin wiki page, and the codes people
  wrote are on that page. The wiki (like the list) answers automated requests with a bot check, which the app does not try to get past: "Open wiki page" opens the
  page in your browser, you select the whole page (Cmd+A, Cmd+C) and paste it into the window. Only the code blocks are taken, named by the heading above them and by their
  region label ("Normal HUD (PAL)"), and the blocks labelled for another region than the disc are left out. A single code block can be pasted too. What the app downloads by itself, with nothing to paste, is: the Gecko code database the
  Wii loaders (USB Loader GX, WiiFlow) use, `codes.rc24.xyz/txt.php?txt=<id6>`, one list per 6 character disc id (the only thing sent is the public disc id), and the codes
  Dolphin ships in its open source `Data/Sys/GameSettings/<id3>.ini` and `<id6>.ini` files. A game only gets codes when its own id is listed, because the addresses change
  with the region: Skies of Arcadia NTSC-U (`GEAE8P`) downloads 124 codes, the PAL disc (`GEAP8P`) none, and for it the codes are only on the wiki page.
- Gecko codes are used as written. Action Replay codes are only used when they are plain 8, 16 and 32 bit writes (their words are the Gecko 00, 02 and 04 codes);
  other Action Replay types are listed but cannot be ticked. Pasted codes are taken as Gecko codes.
- Nothing is ticked by default, because codes such as infinite health are not meant to run together.
- A code named "60Hz" on a PAL disc gets a note: Nintendont's own 60 Hz forcing delays the audio (`AUDIO-DELAY.md`), a code that sets the mode inside the game was not measured.

**Wii:** a Wii game in a WUP has no code handler, so the ticked codes are saved as `<id6>.codes.txt` (same folder as the app's settings, `codes/`), and the next build of that
game puts them into its `sys/main.dol` before packing. Two ways, chosen by the build:

- **Plain writes** (8, 16 and 32 bit writes whose addresses are inside the DOL: widescreen, frame rate, region patches) are written straight into the DOL, a cheat whole or not at all. No extra tool.
- **Any other Gecko code** (assembly `C2`, conditional, pointer, like the Arc Rise Fantasia "60 FPS" code) needs a code handler. The cheat codes window has an "Install code handler tool" button that downloads
  [GeckoLoader](https://github.com/JoshuaMKW/GeckoLoader) by JoshuaMK (GPL-3.0; the app does not bundle it, it runs it as a separate program) with a Python environment into `tools/GeckoLoader` next to the app's
  settings (it needs `git` and `python3`, about 60 MB, a minute). With it installed those codes can be ticked, and the build adds GeckoLoader's code handler and a list of all the ticked codes to `main.dol`:
  GeckoLoader makes the DOL start at its own loader, which installs the handler and hooks it into the game's video code (a byte pattern search, so it can fail on a game it does not recognise).
  If it fails, or is not installed, the build still finishes, writes only the plain writes, and the build log says why (`[cheats] ERROR: ...` or `... code handler tool is not installed`).

**Frame rate codes (for example "60 FPS") on a Wii game.** They change the game's time step per frame and its frame interval, so the game has to really deliver 60 frames per second, or it runs in slow
motion. A real Wii (and so a vWii at its normal speed) usually cannot: the Dolphin wiki says the Arc Rise Fantasia 60 FPS code needs more than 125% CPU. The cheat codes window warns about it. What can help
is the **cafe2wii patching** option (Advanced tab, available in Wii Retail since 1.2.22 and in Wii Homebrew, port of FIX94's c2w_patcher, needs your Starbuck ancast key), which unlocks the Wii U CPU speed in vWii (1.215 GHz); the console also needs the `c2w_patcher`
Aroma plugin and signature patches for injected titles. It does not speed up the Wii's graphics chip, so a game that is graphics bound can still be slow. When testing, change one thing at a time (the
"Patch video mode" option is a separate variable). Checked on the real Arc Rise Fantasia (USA) `main.dol`: the code's variables are in BSS (only a code handler can set them, and the handler hooks a function that
runs once per displayed frame), and every `C2` block matches the game's own instructions, so it is the right version of the game for the code.

With "Disable trimming" on, a game that has saved codes is still extracted so the DOL can be edited. The plain writes were checked with an ISO rebuilt by `wit`; the code handler was checked the same way with a
synthetic DOL that carries GeckoLoader's hook pattern (the real tool, `WIIVC_GECKOLOADER_DIR` points the tests at it). **Neither has run on a real Wii game on a console.**
- Not tried on a console.

## Checks

- `WIIVC_SNAPSHOT=out.png [WIIVC_TAB=n] dotnet bin/Release/net10.0/WiiVCInjector.dll` renders a tab to a PNG and exits.
- `WIIVC_UITEST=1 dotnet bin/Release/net10.0/WiiVCInjector.dll` runs the UI logic self-test (uses a temporary settings file).
- `WIIVC_GCCONVERT=game.rvz [WIIVC_GCOUT=out.iso] WIIVC_SELFTEST=1 dotnet bin/Release/net10.0/WiiVCInjector.dll` unpacks a GameCube image (ISO, CISO, RVZ or NKit) with the code the build uses, walks the disc's file table and prints the SHA-1. The same game in CISO, RVZ and NKit must print the same SHA-1; an RVZ or NKit image downloads the NKit tool the first time.
