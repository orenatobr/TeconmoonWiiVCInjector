# AGENTS.md

Guide for AI coding agents (and humans) working in this repo. Keep it short and true: when you change a
command, path or rule below, change it here in the same PR.

## What this repo is

A fork of [TeconMoon's WiiVC Injector](https://github.com/piratesephiroth/TeconmoonWiiVCInjector) with a native
macOS (Apple Silicon) port added under `macos/`. It packs a Wii, GameCube, WiiWare or homebrew game into a
Wii U Virtual Console package. Two independent codebases live here:

| Path | What | Status |
| --- | --- | --- |
| `TeconMoon's WiiVC Injector/` | Original Windows WinForms app (C#, .NET Framework) | Upstream code, left unchanged. Do not edit. |
| `macos/` | Avalonia port for macOS arm64 (C#, .NET 10) | All active work happens here. |
| `docs/` | Investigation notes | Add notes here, not in code comments. |

## macOS app map (`macos/WiiVCInjector/`)

- `Program.cs`, `App.axaml(.cs)`: entry point and theme dictionaries (`Light`, `Dark`).
- `MainWindow.axaml(.cs)`, `SettingsWindow.*`, `SDCardWindow.*`: the three windows. XAML is hand written; control
  names match the original WinForms designer. Handlers live in the matching `.axaml.cs`.
- `BatchConverter.cs`: "Choose games and convert..." multi-game flow.
- `AppSettings.cs`: settings in `~/Library/Application Support/WiiVCInjector/settings.json`.
- `Engine/BuildEngine.cs`: the build pipeline, no UI. Entry point `BuildEngine.RunAsync(BuildOptions, ...)`.
- `Engine/BaseFiles.cs`, `Tools.cs`, `Metadata.cs`, `Media.cs`: base file download (JNUSTool), external tool
  runner, game metadata and images, boot sound and TGA conversion in C#.
- `Engine/C2wPatcher.cs`: C# port of FIX94's c2w_patcher. `Engine/VideoModePatcher.cs`: experimental video mode patch.
- `Engine/WupValidator.cs`: validates the produced package.
- `UiSelfTest.cs`, `Engine/SelfTest.cs`, `GuiBuildTest.cs`: the built-in checks, switched on by env vars (below).
- `macos/C2wCheck/`: console project that builds a synthetic cafe2wii image, patches it and checks every byte
  (`patch <in> <keyhex-file> <out>` patches a real image instead).
- `macos/package.sh`: builds `dist/WiiVCInjector.app` (self-contained .NET plus a jlink Java runtime). `dist/` is gitignored.
- `macos/install.sh`: curl-installer that fetches the latest release.

External tools (wit, nfs2iso2nfs, NUSPacker, JNUSTool, GetExtTypePatcher) are not in git. `package.sh` bundles
them from `TOOLS_SRC`. Nintendont is not in git either: `SDCardWindow.axaml.cs` downloads `loader.dol`,
`meta.xml` and `icon.png` from upstream `FIX94/Nintendont` `master` at run time.

## Commands

Requirements: macOS on Apple Silicon, .NET 10 SDK. Packaging also needs JDK 17 and Python 3 with Pillow.

    dotnet build macos/WiiVCInjector -c Release -o /tmp/wiivc-app        # build
    WIIVC_UITEST=1 dotnet /tmp/wiivc-app/WiiVCInjector.dll                # UI logic self-test (CI runs this)
    WIIVC_SELFTEST=1 dotnet /tmp/wiivc-app/WiiVCInjector.dll              # engine self-test, synthetic images only
    WIIVC_SETTINGS=/tmp/x.json WIIVC_THEME=dark WIIVC_TAB=2 \
      WIIVC_SNAPSHOT=/tmp/x.png dotnet /tmp/wiivc-app/WiiVCInjector.dll   # render a tab to PNG without a window
    TOOLS_SRC=/path/to/tools VERSION=1.0.0 macos/package.sh               # release app bundle

Env vars: `WIIVC_THEME=light|dark`, `WIIVC_TAB=0..4`, `WIIVC_WINDOW=settings|sdcard`, `WIIVC_SETTINGS=<file>`
(always set it when rendering, so your real keys stay out of the picture), `WIIVC_GUIBUILD=1` (drives the real
window through a homebrew build; needs `WIIVC_COMMON_KEY` and `WIIVC_TITLE_KEY`).

CI is `.github/workflows/macos-ci.yml`: build, UI self-test, render all 5 tabs in both themes. It runs on pull
requests that touch `macos/`. Always pass `-o` to `dotnet build` when you also run the app, or you will pick up a stale `bin/`.

## Rules

- Tests: add or extend a check in `UiSelfTest.cs` (UI logic) or `Engine/SelfTest.cs` (engine) for every behavior
  change. Test the decision, not the platform readback.
- Surgical changes. Do not touch the Windows app, and do not reformat files you are not changing.
- Never commit keys. The Wii U common key and title key are the user's own; they live in the settings file,
  never in the repo, logs or test fixtures. `.env*` files stay out of git.
- Never claim a path works on a real console unless it was run on one. The README lists what was only run on
  synthetic inputs; keep that list honest.
- Do not use em dashes in any text, code, commit or PR.
- Commits: Conventional Commits with a mandatory scope, English, imperative, header at most 72 chars,
  for example `fix(macos): keep the build tab scroll position`. No force push, no commits to `main`.
- Branch names: `<type>/<short-kebab-description>`. PR title uses the same format as the commit header.
  Merging goes through a pull request.
- Verify before saying done: build, then run `WIIVC_UITEST=1`, and say plainly if a check was skipped.

## Known gaps and traps

- NKit/NASOS input is not supported on macOS; use ISO or WBFS.
- The app is ad-hoc signed, not notarized. Gatekeeper blocks a browser-downloaded zip; `install.sh` avoids it.
- `docs/nintendont-audio-latency.md` records an audit of Nintendont's audio code. Read it before proposing
  a Nintendont audio change, and measure on real hardware first.
