# Audio delay on the Wii U (vWii): status and how to measure it

## Status
No fix exists. The Nintendont bundled here is upstream 7a18345 (6.510), which is also the current upstream master. Reading it found no code that delays sound effects:

- `ARStartDMA` copies ARAM synchronously, faster than the hardware.
- The AX DSP patches only fix the sample format.
- The `__AI_set_stream_sample_rate` patch has no pattern entry, so it is never applied.
- The only Nintendont audio buffer is the disc streaming one (`kernel/Stream.c`). The opt-in "audio buffer experiment" halves it. It has not been measured on hardware.
- The kernel has no access to the video registers, so Nintendont cannot delay the picture to match the sound.

Upstream has no issue about audio delay. Issues about crackling (FIX94/Nintendont #781, #1200) point at slow storage and large cluster sizes.

## Skies of Arcadia (GEAP8P) specifics
- The disc header has the audio streaming flag (offset 0x8) and the stream buffer size (offset 0x9) both at 0. The game does not use DVD audio streaming, so the half-buffer experiment cannot change its sound.
- Verified in the source: with Force Progressive and Patch PAL50 on, `kernel/Patch.c` (around lines 2340 to 2350) rewrites a game's PAL50 video mode (0x04) to the progressive NTSC mode (0x02), so the picture runs at 60 Hz instead of 50 Hz.
- Not verified: whether Skies of Arcadia PAL then runs its scripted moves faster than their sound. That depends on how the game paces itself. Test: Force Progressive off and Video Mode Auto (native 50 Hz), then play Cutlass Fury again.

## Check the storage first
1. Format the SD card or USB drive with a 32 KB cluster size.
2. Try the same game from a fast USB drive. If late or popping sounds go away, the cause was the card.

## Measure the delay
1. Record the screen with an HDMI capture card at 60 fps or more. A phone filming the TV works for comparing settings, but its microphone and 30 fps limit the result to about 33 ms and mix music with effects.
2. Use a dry, repeatable event (a hit with a visible flash). About 10 seconds per scene.
3. Extract frames and audio with `ffmpeg`, find the frame of the flash and the onset of the sound, and subtract. Repeat on several events and compare with a reference capture of the same scene.
4. The first phone recording (Skies of Arcadia, Cutlass Fury) gave a possible 200 ms delay with low confidence (correlation 0.2), so it is not a result.

## Open items
- Storage test (above).
- A clean capture for a real number.
- A/B of the experimental `boot.dol` against the stock one.
