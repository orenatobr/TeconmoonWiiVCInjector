# Audio delay on the Wii U (vWii): status and how to measure it

## Status
Fixed for PAL games: the cause is Force Progressive. With it on, `kernel/Patch.c` (around lines 2340 to 2350) rewrites a game's PAL50 video mode (0x04) to the progressive 60 Hz mode (0x02), so a PAL game runs at 60 Hz instead of the 50 Hz it was made for. Sound effects then arrive late against the picture. Confirmed on hardware with Skies of Arcadia (GEAP8P), scene Cutlass Fury: same ISO, same Nintendont, only Force Progressive off, and the delay is gone.

What to do: leave Force Progressive off for PAL discs. The SD Card window now says so. A PAL game then outputs 50 Hz; if your TV shows a black screen, that is the older black screen of 50 Hz output, not this setting.

## Ruled out (read in the source and tested)
- Disc reads through the Wii VC virtual disc (`WDVD_FST_Read`): the same delay appears with the ISO read from the SD card through the Homebrew Channel.
- Disc audio streaming buffer (`kernel/Stream.c`): the game's header has the streaming flag (offset 0x8) at 0, so the half-buffer experiment cannot change its sound.
- `ARStartDMA` (synchronous copy), the AX DSP patches (sample format only), and the `__AI_set_stream_sample_rate` patch (no pattern entry, never applied).

The crackling on flames and footsteps is gone too with Force Progressive off, and the picture showed no black screen or flicker at 50 Hz (reported by the user on Skies of Arcadia PAL).

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
