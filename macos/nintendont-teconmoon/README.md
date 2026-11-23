# Teconmoon Nintendont

A build of [FIX94/Nintendont](https://github.com/FIX94/Nintendont) with two changes for the Wii U (vWii). The app's Nintendont SD Card menu
installs it by default; a box in that window installs the plain upstream build instead.

## What it changes

**No L + R + A shortcut.** Release 1.2.10 had one. It wrote the Wii U aspect register `0xd8006a0` (and `0xd8006a8`) while a game ran, and on a console it
gave horizontal black bars, and the wiiscreenoff plugin was also reported not working (the link between the two is not proven). WiiUBrew lists those two addresses as the DMCU message and control registers, the
display microcontroller whose firmware wiiscreenoff and evWii patch; nobody documents what the two values do or whether they can be written during a game.
It was removed rather than guessed at again. A working 4:3 / 16:9 switch needs a console test loop first.

**PAL discs stay at 50 Hz.** A PAL game forced to 60 Hz plays its sound late against the picture (Force Progressive, confirmed on Skies of Arcadia,
see `../AUDIO-DELAY.md`). Right before the game starts, `loader/source/main.c` checks the disc's bi2.bin region and, for a PAL disc, clears
Force Progressive, Patch PAL50 and any forced video mode other than PAL50 in the config the kernel receives. NTSC and Japanese discs are untouched.
Only Force Progressive was measured on hardware; the other two are cleared because they also run a PAL game at 60 Hz. The Nintendont menu
text for Force Progressive says it is ignored on PAL games.

**Cheat codes load for a game started from a WUP.** A WUP starts Nintendont with its own embedded config (game path `di`), so there is no Cheats box to tick.
In `kernel/Patch.c`, when the game path is `di` and Nintendont's own lookup finds a `.gct` file for the game (`/codes/<id6>.gct` on the card, the same names it already
searches), the Gecko code handler is switched on. Any other start (a game file on the card) behaves as upstream: the Cheats option decides. The app writes the `.gct`
(see "Cheat codes" in `../README.md`). Not tried on a console.

The patch is `teconmoon.patch`, 28 added lines in three files, on top of upstream commit `7a183459939653cb7bcf59ab137c2758a9f9fae0`.
`Assets/nintendont-teconmoon/icon.png` is the file from that same commit. `meta.xml` is the upstream one with the name, author and description changed so the Homebrew Channel shows this is the Teconmoon build (the version number is still upstream's 4.431).

## What was and was not checked

- Checked: the patch applies to the pinned commit and the toolchain below builds it (see the hashes at the end).
- Not checked: any of it on a real console, including the cheat codes. The PAL guard runs in the loader, so it applies to a game started from the Nintendont menu and to one started
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
  `Assets/nintendont-teconmoon/boot.dol` is 1651904 bytes with sha256 `ef1571fc7e7976c986ee7735f66e4ed3713e52b5ffa46949546eacbd82bb4191`, and `UiSelfTest` checks that hash.
- The cheat change is in the kernel, whose release build has no debug text and keeps its strings out of plain sight, so it cannot be found in `boot.dol` with `strings`. It was checked
  only by the patch applying and the kernel compiling, and the file size and contents changing. Not run.
- The loader string `PAL disc: Force Progressive ignored, it delays the audio` and the menu text `Ignored on PAL games: 60 Hz` are in the binary. The unpatched
  build of the same toolchain is a different file (`3176ef6f...` for the earlier build of this toolchain).

## License

Nintendont is GPL v2. This directory ships a modified binary, so the matching source is the pinned upstream commit above plus `teconmoon.patch`.
`build.sh` rebuilds the binary from them. The result is not guaranteed to be bit for bit identical to the shipped `boot.dol`: the loader and kernel
print their build time, `build.sh` pins it, and an earlier build of this kind still differed from its shipped file in a few thousand bytes near the
embedded kernel. Treat the shipped file as not independently reproducible.
