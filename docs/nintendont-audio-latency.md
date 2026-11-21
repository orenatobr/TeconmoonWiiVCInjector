# Nintendont audio delay on vWii: code audit

Question: Nintendont running on a Wii U (vWii) feels like it has a bad audio delay. Is there something in
the Nintendont code that can be changed to line the sound up with the picture?

Audited revision: [FIX94/Nintendont@7a18345](https://github.com/FIX94/Nintendont/commit/7a183459939653cb7bcf59ab137c2758a9f9fae0)
("Revert homeland fix", upstream `master` on the audit date). Paths below are relative to that tree.

## Verdict

No Nintendont code path adds a fixed audio delay, and no change was made to the Nintendont binary this
repo downloads. A patch without a measurement on real vWii hardware would be a guess, and a wrong guess
here (for example smaller stream buffers) trades latency for crackling. The findings below say where a
delay can and cannot come from, and what to measure before touching anything.

## How audio reaches the speakers

There are two separate paths. Only the second one is Nintendont code.

1. **Game PCM (music, effects, FMV audio made by the game's own mixer).** The game's audio library (AX or
   its own) fills buffers and starts the hardware Audio Interface DMA. Nintendont does not emulate this.
   On both Wii and vWii the real DSP and AI hardware run it, so its latency is the game's own buffering
   plus the console's output path.
2. **DVD streaming audio (ADP / DTK).** Games that stream music from the disc use DVD audio stream
   commands. The disc is not read by a real drive, so Nintendont decodes the stream itself and mixes it into
   the game's AI DMA buffer. This is the only audio logic in the tree:
   - `kernel/DI.c` (commands `0xE1` start and stop, `0xE2` status) calls `StreamStartStream` and `StreamEndStream`.
   - `kernel/Stream.c` reads ADP from the image, decodes it (`kernel/adp.c`) and fills two buffers.
   - `kernel/asm/__DSPHandler.S` runs in the game's DSP interrupt, adds the decoded samples into the
     game's current AI DMA buffer with a simple compressor, and raises `UPDATE_STREAM` when a buffer ends.

## What was checked and ruled out

| Suspect | Finding |
| --- | --- |
| Stream buffer size | Each chunk is `0xC400` bytes of PCM, `12544` stereo samples, about 261 ms at 48 kHz, double buffered (`Stream.c`: `BUFSIZE`, `CHUNK_48`). The stream starts playing as soon as the first chunk is decoded, so the size sets the refill cadence, not a startup delay. |
| Startup wait | `StreamStartStream` reads and decodes two chunks synchronously before it raises `STREAMING`. That is two short reads and a decode on the ARM, a few milliseconds. |
| Emulated disc seek time | `kernel/ReadSpeed.c` simulates a 50 ms seek and 3 MB/s reads, but only `DI.c` read commands use it. `Stream.c` calls `DiscReadSync` directly, so stream reads skip it. |
| Resampling | `Stream.c` has a 48 kHz to 32 kHz path (`WritePCM48to32`) when the game sets the 32 kHz AI rate. It is sample by sample, no queueing, no added latency. |
| Sample counter hack | `kernel/Patch.c` (`FCODE_AIResetStreamCount`) patches `AIResetStreamSampleCount` for most titles. It affects stream position reporting, not output timing. |
| Loader audio init | `loader/source/global.c` calls `AUDIO_Init`, `DSP_Init`, then stops the DMA. The loader does not set a buffer or latency parameter. |
| Config options | `NIN_CFG_*` has no audio option. The only timing knob is `NIN_CFG_REMLIMIT` (remove the read speed limiter), which affects loading, not audio. |
| Upstream reports | Issues and PRs in the upstream repo mention audio crackle and stream bugs for specific games, but none describes a vWii audio delay. |

## Where a vWii delay most likely comes from

Both of these are outside Nintendont and this repo, and neither is confirmed by a source:

- **The Wii U output chain.** vWii audio and video go through the Wii U's own pipeline and the HDMI
  device. A TV or AV receiver with video processing and no game mode usually delays the picture, and the
  Wii U path can add its own offset. Whichever of audio or video is late, the perceived gap is the
  difference.
- **Game specific buffering.** A game that already has noticeable audio latency on a real Wii or GameCube
  will have it here too, since the DSP runs the same code.

## How to isolate it (before changing any code)

1. Pick one game with a clear audio cue (a button press with a sound, or a rhythm game).
2. Compare three setups: the same game on vWii through Nintendont, on a real Wii or GameCube if you have
   one, and in Dolphin. A phone recording at 240 fps of screen plus speaker gives milliseconds by counting frames.
3. Change only the TV side first: game mode, disable audio processing, or take audio from a
   different output (TV speakers against a receiver).
4. If vWii through Nintendont is later than the real console on the same TV, the delay is in the vWii
   output path, not the emulation logic. Then try the two experiments below.

## Experiments, cheapest first

**1. Skip IPL on or off (no build needed).** When "Skip IPL" is off and a GameCube IPL image is found,
`loader/source/main.c` boots the real IPL and then writes `0x68` to `AI_CR` (`0xCD806C00`), which pins
the AI to 32 kHz with the comment "No Resample". With "Skip IPL" on, Nintendont's own loader runs and leaves
the AI rate to the game. The two modes can therefore hand the Wii U different sample rates, and the Wii U
resamples them in its own path. Run the same game with the option on and off and compare against the video. This
is a hypothesis: nothing in the code or in a source shows it changes latency, only that the rate differs.

**2. Smaller stream buffers (needs a build, only for streamed music).** The size `0xC400` is hard coded in
more than one place, so changing it in one file breaks the stream:

- `kernel/Stream.c`: `BUFSIZE`, `CHUNK_48`, `CHUNK_48to32`, and the addresses `buf1` and `buf2`.
- `kernel/asm/__DSPHandler.S`: `li r3,0xC40; slwi r3,r3,4` and the `MAXBUF` double (`0x18800`), plus the same
  `0xC40` constant again in `changebuffer`.
- `kernel/asm/MajoraAudioStream.S`: the same two constants.
- `kernel/asm/constants.inc`: `AI_ADP_BUF` (`0x93280000`).

Halving all of them gives about 130 ms chunks. That shortens the refill cadence, but streamed music already
starts at once, so a gain is not expected unless the delay is in how late a refill lands. Treat it as a way to
rule the stream path in or out, and watch for crackling on a slow SD card or USB drive.

## Why no binary was built

The upstream README pins **devkitARM r53-1** and **devkitPPC r35-2** and warns that newer devkitPro and libOGC
produce a build that crashes when returning to the Nintendont menu. No build of the Nintendont tree was
completed, and none was tested on a vWii.

What was checked, so the next attempt does not start from zero:

- The old Linux packages exist on an unofficial third-party mirror (`wii.leseratte10.de/devkitPro`), not on devkitPro's
  own servers: `devkitARM-r53-1-linux.pkg.tar.xz` (sha256 `140d217a...9fa9599`),
  `devkitPPC-r35-1-linux.pkg.tar.xz` (sha256 `2916d431...181658`; r35-2 is Windows only there) and
  `libogc-1.8.23-1-any.pkg.tar.xz` (sha256 `e5b61a2e...8514c6707`).
- Extracted into `/opt/devkitpro` in a `linux/amd64` Ubuntu 22.04 container, the compilers run:
  `powerpc-eabi-gcc (devkitPPC release 35) 8.3.0` and `arm-none-eabi-gcc (devkitARM release 53) 9.1.0`.
- Running the unmodified upstream `make forced` there stopped at `fatfs/Makefile.ppc`: `/opt/devkitpro/devkitPPC/wii_rules:
  No such file or directory`. That file comes from the separate `devkitppc-rules` package (the mirror lists versions
  1.0.0 to 1.2.1). Which version matches r35 was not determined.
- The upstream tree already commits a prebuilt `loader/loader.dol` (1651360 bytes), which is the baseline to compare any
  experimental build against.

The attempt to add the rules package and rerun was blocked by the session's permission check (it downloads and
runs third-party binaries), so it was not retried. The remaining step for a human: download
`https://wii.leseratte10.de/devkitPro/devkitPPC/devkitppc-rules/devkitppc-rules-1.1.1-1-any.pkg.tar.xz` (1.1.1 is a
guess, its hash was never computed, and 1.1.0-1 is the next candidate), extract it into the same `/opt/devkitpro`
tree as the three packages above, then rerun `make forced` with `DEVKITPRO`, `DEVKITARM` and `DEVKITPPC` exported.

Both experiments above are untested hypotheses. Neither is a known fix. The upstream `Makefile` also stops when `DEVKITPPC` or `DEVKITARM` is unset.

## How Nintendont reaches the user in this repo

The injector does not ship or build Nintendont. `macos/WiiVCInjector/SDCardWindow.axaml.cs` downloads `loader.dol`,
`meta.xml` and `icon.png` from upstream `master` at run time, so every user already gets the latest upstream
Nintendont with no step in this repo. Using a modified build would mean changing that download URL to a place that
hosts a hardware-tested binary.
