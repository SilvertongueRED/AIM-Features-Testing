# Audio replacements

A mod can replace the game's music and sound effects by shipping FMOD Studio sound banks.

```
My Audio Mod/
  manifest.toml
  audio/
    Fall.bank
    Master.bank
```

Each `audio/<Name>.bank` replaces `assets/audio/<Name>.bank` in the game archive, whole.

## Rules

- **Only existing banks.** The game ships `Master`, `Master.strings`, `Farm`, `Spring`, `Summer`,
  `Fall`, `Winter`, `MinesUpper`, `MinesDeepEarth`, `MinesLavaCaves`, `MinesRuins` and
  `MinesTideCaverns`. Any other name is skipped and reported.
- **Real banks only.** The file must be an FMOD bank (a RIFF file of form type `FEV `).
- **Top of `audio/` only.** Files in sub-folders are ignored, so you can keep sources there.
- **Last one wins.** Two mods replacing the same bank are a replacement conflict.

## Making a bank that works

A bank holds the game's event data (timelines, loop regions, parameters) as well as the audio.
Keep the event data from the **current** game version and change only the sound data:

- keep every sound's sample count, channel count and sample rate, or loop points and transitions
  drift;
- the game's banks use Vorbis; FMOD only decodes Vorbis streams whose setup header it knows, which
  libvorbis produces at quality 3 for 48 kHz mono and stereo.

A bank built against an older game version can break after a game update. Rebuild it from the
updated banks.
