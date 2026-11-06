# TeconmoonWiiVCInjector (macOS fork)

This repository is a **fork** of [piratesephiroth/TeconmoonWiiVCInjector](https://github.com/piratesephiroth/TeconmoonWiiVCInjector), which is itself a fork of the Wii Virtual Console Injector for Wii U by **TeconMoon**. All credit for the injector, its pipeline and its Windows application goes to TeconMoon and to piratesephiroth, who added NKit/NASOS support and many fixes. The Windows application in `TeconMoon's WiiVC Injector/` is left unchanged.

## What this fork changes

This fork adds a native **macOS (Apple Silicon) port** under [`macos/`](macos/), because the original is a WinForms app that shells out to Windows `.exe` tools and cannot run on a Mac.

- Same window layout as the original: the Avalonia windows are generated from `Form1.Designer.cs`, `SettingsForm` and `Form2` by `macos/tools/gen_axaml.py`, and the logic of `Form1.cs` is ported.
- A C# build engine replaces the Windows tools: wit, GetExtTypePatcher, nfs2iso2nfs and NUSPacker run natively, PNG to TGA and boot sound (WAV to btsnd) are done in C#, and the Wii U base files are downloaded with JNUSTool using your own keys.
- The C2W patch is a C# port of [FIX94's c2w_patcher](https://github.com/FIX94/c2w_patcher) (MIT); on a synthetic image its output is byte identical to the original tool.
- Wii VMC (closed source, Windows only) is replaced by an open, experimental video mode patcher with a target picker (NTSC 480i, PAL60, PAL50, 480p).
- Settings are stored in `~/Library/Application Support/WiiVCInjector/settings.json` instead of the Windows registry.
- Packaging: `macos/package.sh` builds a self-contained `WiiVCInjector.app` (bundled .NET runtime and a minimal Java runtime). Download it from the [latest release](https://github.com/orenatobr/TeconmoonWiiVCInjector/releases/latest).

**Status:** one real game was built: a 6.78 GB Xenoblade WBFS produces a 6.4 GiB package, and its WBFS to ISO to NFS to ISO roundtrip passes `wit verify`. The other paths (ISO, GameCube, WiiWare, homebrew DOL) were only run with synthetic inputs, and no package has been installed on a Wii U or opened in Cemu yet. NKit/NASOS input is not supported on macOS; use ISO or WBFS. See [`macos/README.md`](macos/README.md) for build, first run (the app is ad-hoc signed, not notarized: use Open Anyway or `xattr -dr com.apple.quarantine`) and checks.

---

## Original README

# TeconmoonWiiVCInjector
Fork of the popular Wii Virtual Console Injector for Wii U by Teconmoon

The original can be found here:
https://gbatemp.net/threads/release-wiivc-injector-script-gc-wii-homebrew-support.483577/

Many little things were fixed.
Added support for NKIT (nkit.iso) and NASOS (iso.dec).

This isn't a complete rewrite though.
Source code is still SUPER CRAPPY™ as defined by the original author.

