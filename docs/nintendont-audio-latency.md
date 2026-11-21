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
   output path, not the emulation logic. If only streamed music is late (menu and effect sounds are fine),
   test the stream path: set `BUFSIZE`, `CHUNK_48` and `CHUNK_48to32` in `kernel/Stream.c` to a smaller
   consistent value, rebuild, and listen for underruns on a slow SD card.

## If a stream path experiment is wanted later

Building Nintendont needs devkitPPC and devkitARM (the kernel runs on the Starlet ARM, the loader on
the PPC); the upstream `Makefile` stops with an error when `DEVKITPPC` or `DEVKITARM` is not set. Neither
toolchain was installed or built during this audit. The injector does not ship Nintendont: `macos/WiiVCInjector/SDCardWindow.axaml.cs`
downloads `loader.dol`, `meta.xml` and `icon.png` from upstream `master` at run time, so a tested fork binary
would be a deliberate change to that download URL, not a build step of this repo.
