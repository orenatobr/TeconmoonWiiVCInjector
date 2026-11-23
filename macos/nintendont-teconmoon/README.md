# Teconmoon Nintendont

A build of [FIX94/Nintendont](https://github.com/FIX94/Nintendont) with three changes for the Wii U (vWii). The app's Nintendont SD Card menu
installs it by default; a box in that window installs the plain upstream build instead.

## What it changes

**No L + R + A shortcut.** Nothing in this build reads that button combination, and the wiiscreenoff plugin has no shortcut either. Release 1.2.10 had one. It wrote the Wii U aspect register `0xd8006a0` (and `0xd8006a8`) while a game ran, and on a console it
gave horizontal black bars, and the wiiscreenoff plugin was also reported not working (the link between the two is not proven). WiiUBrew lists those two addresses as the DMCU message and control registers, the
display microcontroller whose firmware wiiscreenoff and evWii patch; nobody documents what the two values do or whether they can be written during a game.
It was removed rather than guessed at again. A working 4:3 / 16:9 switch needs a console test loop first.

**PAL discs stay at 50 Hz.** A PAL game forced to 60 Hz plays its sound late against the picture (Force Progressive, confirmed on Skies of Arcadia,
see `../AUDIO-DELAY.md`). Right before the game starts, `loader/source/main.c` checks the disc's bi2.bin region and, for a PAL disc, clears
Force Progressive, Patch PAL50 and any forced video mode other than PAL50 in the config the kernel receives. NTSC and Japanese discs are untouched.
Only Force Progressive was measured on hardware; the other two are cleared because they also run a PAL game at 60 Hz. The Nintendont menu
text for Force Progressive says it is ignored on PAL games.

**Settings for one game.** `/teconmoon/<id4>.ini` on the card (the first 4 characters of the disc id, which carry the region) holds a few keys of Dolphin's GameINI format:
`AspectRatio = 1` or `2` (Wii U Widescreen on or off), `wideScreenHack` (Force Widescreen), `ProgressiveScan` (Force Progressive) and `SkipIPL`. `loader/source/GameSettings.c` reads them
(`test-gamesettings.sh` compiles and tests that file on the Mac) and `loader/source/main.c` sets or clears the matching option bit in the config the kernel receives, for that start only, so `nincfg.bin`
is not changed. A key that is not there leaves the option as it is. Section names are ignored and other lines skipped, so a Dolphin `GameSettings/<id>.ini` can be dropped in as it is (only the first 4 KB is read).
It is read as soon as the game id is final, while the card is still open and after `nincfg.bin` has been written (so nothing is saved), and before the loader uses any of the four options.
The PAL guard below still runs after it, so a PAL disc cannot be put back on Force Progressive this way. The app writes the file (see "Game settings" in `../README.md`).
The Wii U decides 4:3 or 16:9 for a game in `kernel/main.c`: it writes the DMCU registers `0xd8006a0` and `0xd8006a8` only when the Wii U Widescreen option and the console's own aspect setting disagree, and restores them when the game ends.
The kernel now logs that decision in `ndebug.log` (`WiiU aspect: ...`, with the register value and whether it writes), because those registers are also what display patches such as wiiscreenoff touch.

**Cheat codes load for a game started from a WUP.** A WUP starts Nintendont with its own embedded config (game path `di`), so there is no Cheats box to tick.
In `kernel/Patch.c`, when the game path is `di` and Nintendont's own lookup finds a `.gct` file for the game (`/codes/<id6>.gct` on the card, the same names it already
searches), the Gecko code handler is switched on. Any other start (a game file on the card) behaves as upstream: the Cheats option decides. The app writes the `.gct`
(see "Cheat codes" in `../README.md`). Seen working on a console: with a `/codes/GEAP8P.gct` on the card, a Skies of Arcadia PAL WUP logged `Patch:Cheats on for a Wii U disc game, found /codes/GEAP8P.gct` and
`Patch:Copied /codes/GEAP8P.gct to memory` in `ndebug.log`, and the Gecko code took effect.

The patch is `teconmoon.patch`, 181 added lines in 6 files, on top of upstream commit `7a183459939653cb7bcf59ab137c2758a9f9fae0`.
`Assets/nintendont-teconmoon/icon.png` is the file from that same commit. `meta.xml` is the upstream one with the name, author and description changed so the Homebrew Channel shows this is the Teconmoon build (the version number is still upstream's 4.431).

## What was and was not checked

- Checked: the patch applies to the pinned commit and the toolchain below builds it (see the hashes at the end).
- Checked on a console: the cheat codes (see above). Not checked on a console: the per game settings, the PAL guard on its own, and the aspect log. The PAL guard runs in the loader, so it applies to a game started from the Nintendont menu and to one started
  by the WUP autobooter (both go through the same launch code). It does not change the `nincfg.bin` file on the card.

## How to use it

1. Open the app's Nintendont SD Card menu, choose the card and press the download button (leave the upstream box unticked).
2. Start your GameCube WUP as usual. The WUP's autobooter starts `sd:/apps/nintendont/boot.dol`, so nothing in the WUP needs rebuilding.

## Rebuilding

    macos/nintendont-teconmoon/build.sh teconmoon loader-teconmoon.dol    # this build
    macos/nintendont-teconmoon/build.sh upstream loader-upstream.dol      # the same toolchain, no patch

The script needs Docker and checks the sha256 of every toolchain package it downloads (devkitARM r53-1, devkitPPC r35-1,
libogc 1.8.23-1 and devkitppc-rules 1.1.1, from an unofficial mirror because devkitPro no longer hosts them). `bin2s` and
`elf2dol` are compiled from the pinned commits of devkitPro's `general-tools` and `gamecube-tools`. The toolchain is linux/amd64, so on Apple
silicon it runs under emulation and takes 15 to 40 minutes.

What was checked when this build was made:

- Built with the toolchain above (devkitPPC r35-1 instead of the r35-2 the Nintendont README names, which only exists for Windows).
  `Assets/nintendont-teconmoon/boot.dol` is 1653792 bytes with sha256 `d1332cd4ac274f4925f106c63853edeab3c4e1805feead259d9a44d95b6ee819`, and `UiSelfTest` checks that hash.
- Release 1.2.14 read the file after the loader had already closed the card, so it never opened it and every option kept its WUP value: the console log of a Skies of Arcadia run showed
  `WiiU aspect: console 16:9=1, Wii U Widescreen option=0` although `GEAP.ini` had `AspectRatio = 1`. The host test of the parser could not catch that, it does not run the loader. 1.2.15 moves the call
  and the kernel log now prints the whole config, so `ndebug.log` shows whether the file arrived.
- The kernel is stored zipped inside `boot.dol`, so its messages cannot be found in the file with `strings`; `ndebug.log` on the card is where they show.
- The loader string `PAL disc: Force Progressive ignored, it delays the audio` and the menu text `Ignored on PAL games: 60 Hz` are in the binary. The unpatched
  build of the same toolchain is a different file (`3176ef6f...` for the earlier build of this toolchain).

## License

Nintendont is GPL v2. This directory ships a modified binary, so the matching source is the pinned upstream commit above plus `teconmoon.patch`.
`build.sh` rebuilds the binary from them. The result is not guaranteed to be bit for bit identical to the shipped `boot.dol`: the loader and kernel
print their build time, `build.sh` pins it, and an earlier build of this kind still differed from its shipped file in a few thousand bytes near the
embedded kernel. Treat the shipped file as not independently reproducible.
