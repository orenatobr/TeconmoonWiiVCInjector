# Nintendont audio experiment

An opt-in build of [FIX94/Nintendont](https://github.com/FIX94/Nintendont) for testing one idea about audio delay on
vWii. It is **not a known fix**: it has not been heard on a real console, and a read of Nintendont's audio code found
no path that adds a fixed delay (DVD stream reads skip the emulated seek time, the stream starts as soon as the first
buffer is decoded, and there is no audio option in the config).

## What it changes

Nintendont plays DVD streamed music (ADP) through two buffers of `0xC400` bytes (12544 stereo samples, about 261 ms at
48 kHz). This build halves them to `0x6200` bytes (6272 samples, about 130 ms). The size is hard coded in
`kernel/Stream.c` and in two assembly files, so the patch changes all three:

- `kernel/Stream.c`: `BUFSIZE`, `CHUNK_48`, `CHUNK_48to32` and the address of the second buffer.
- `kernel/asm/__DSPHandler.S` and `kernel/asm/MajoraAudioStream.S`: the `0xC40 << 4` constants and the `MAXBUF` comments.

Only streamed music uses these buffers. Sound effects and most in-game audio go through the console's DSP and are not
affected. Smaller buffers refill twice as often, so a slow SD card may crackle; a USB drive should cope.

Source: upstream commit `7a183459939653cb7bcf59ab137c2758a9f9fae0` plus `half-stream-buffers.patch`.
`Assets/nintendont-audio-exp/meta.xml` and `icon.png` are the files from that same commit, so the version shown in the
Homebrew Channel matches the loader.

## How to try it

**Try this first, it needs no build.** In Nintendont's own settings, toggle "Skip IPL" and play the same scene both ways.
Loading a real IPL pins the audio interface to 32 kHz, so the two modes can hand the Wii U different sample rates. Also
rule out the TV: game mode on, audio processing off, and compare TV speakers against a receiver. The delay you hear may well
come from the Wii U output chain and not from Nintendont at all, and this variant cannot change that.

Then, if the delay is still there and mostly affects streamed music:

1. Open the app's Nintendont SD Card menu.
2. Tick "Install the audio buffer experiment instead of the latest upstream build" (the box only shows when the app
   ships this build) and press the download button with your SD card selected.
3. Play your GameCube WUP as usual. The WUP's autobooter starts `sd:/apps/nintendont/boot.dol`.
4. Compare against the upstream build (box unticked), ideally with a phone recording at 240 fps of the screen and the
   speaker.

## Rebuilding

    macos/nintendont-audio-exp/build.sh half loader-half.dol        # this experiment
    macos/nintendont-audio-exp/build.sh upstream loader-upstream.dol  # the same toolchain, no patch

The script needs Docker and checks the sha256 of every toolchain package it downloads (devkitARM r53-1, devkitPPC r35-1,
libogc 1.8.23-1 and devkitppc-rules 1.1.1, from an unofficial mirror because devkitPro no longer hosts them). `bin2s` and
`elf2dol` are compiled from the pinned commits of devkitPro's `general-tools` and `gamecube-tools`.

What was checked when this build was made:

- Both variants built with the toolchain above (devkitPPC r35-1 instead of the r35-2 the Nintendont README names, which only
  exists for Windows). Each `loader.dol` is 1651328 bytes.
- `Assets/nintendont-audio-exp/boot.dol` has sha256 `a6a7adfe7aa67c937d87ebf5f5aec8a2309b419ed213c01d6de4e065b953fc40`.
  The same toolchain without the patch gives `3176ef6f0443fb99e2792814d2432703046fdaa2a626c2dcc13fc1594686cbac`.
- The upstream tree commits its own `loader/loader.dol` (1651360 bytes, sha256 `d56e3bda...`). The rebuild is **not** byte identical to
  it, so this build is not proven to behave the same as the official binary; it only shows the build path works.
- In the compiled `Stream.o` the variant holds `0x6200`, `0x2A00` and `0x13286200` where the unpatched build holds `0xC400`,
  `0x5400` and `0x1328C400`, and the generated PPC handlers carry `li 0x620` (3 places) where the unpatched ones carry `li 0xC40`.
- Not checked: how it sounds, whether it crackles, and whether it runs at all on a real Wii U.

## License

Nintendont is GPL v2. This directory ships a modified binary, so the matching source is the pinned upstream commit above
plus `half-stream-buffers.patch`. `build.sh` is meant to rebuild the binary from them; whether it reproduces the shipped
`boot.dol` hash was not confirmed when this was written (the shipped file was built by running the same steps by hand).
