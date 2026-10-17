# Image replacements

This document describes how AIM handles image replacements. It is based on
the current installer behavior and the Fields of Mistria 1.0.4 asset layout.
It is not a complete catalog of every game image.

## Replacement layout

Place replacement images under `images/replace/` in the mod:

```text
images/
└── replace/
    └── spr_ui_title_screen_clouds.png
```

AIM uses the PNG filename to find the existing game animation metadata. The
game must therefore contain a matching file such as:

```text
assets/animations/Title Screen/spr_ui_title_screen_clouds.meta.toml
```

The replacement name is not a new language or display name. It is the name of
the existing game sprite being replaced.

## Atlas-backed animations

If the game's metadata contains an `atlas` field, AIM removes the old entry
from that atlas and packs the replacement into the same atlas category. The
existing sprite identity is retained, so references from the game continue to
work.

## Atlas-less animations

Some game animations have no `atlas` field in their metadata. Their PNG is a
standalone file next to the metadata file:

```text
assets/animations/Title Screen/spr_ui_title_screen_clouds.meta.toml
assets/animations/Title Screen/spr_ui_title_screen_clouds.png
```

For these replacements AIM writes the replacement PNG directly to that
standalone game path. It does not create or modify an atlas.

The atlas-less path is useful for replacements such as title-screen clouds,
Summit backgrounds, cloud shadows, cursor sprites and other standalone player
or item animations present in the 1.0.4 asset set.

## Metadata and image dimensions

The matching game metadata supplies the sprite ID, atlas information, frame
size and frame count. A mod may provide an optional matching
`images/replace/<sprite>.meta.toml` to override supported metadata fields.

The replacement image must have a width compatible with its frame count. AIM
can recalculate frame dimensions when the image strip is divisible by the
declared frame count; otherwise it skips the replacement and reports why.

## Palette LUTs

A replacement whose name ends in `_lut` (for example `spr_ui_master_lut.png`) is a palette lookup
table: the game draws some sprites through it, taking the row from each pixel's red value and the
column from the palette it wants (the HUD's `spr_ui_master_lut` uses column 0 for daytime and the
later columns as night falls). AIM does not overwrite an installed LUT by default. It appends the
mod's columns that the installed LUT does not already have, so several mods can each add colour
variants to the same LUT.

A recolour needs the opposite, because the game asks for palettes by column index and an appended
column is never asked for. Ship a matching `images/replace/<name>_lut.meta.toml` with:

```toml
[asset_properties]
lut_mode = "overwrite"
```

and AIM lays the mod's LUT over the installed one column for column. Columns past the mod's width,
such as ones another mod appended earlier, are kept. `lut_mode` is read from the mod's meta only and
is never written into the game's meta file.

Sprites drawn through a LUT have to keep their exact pixel values, since those values are the
lookup keys. Recolour them through their LUT instead of repainting them.

## Important limitations

This is a replacement mechanism, not a general language-aware asset resolver.
Renaming a replacement to a new suffix does not make the game select it. For
example, `spr_example_bg.png` will not automatically replace
`spr_example_en.png`; AIM first matches the existing game sprite by filename.
A separate game/runtime resolver or an explicit compatibility mechanism is
needed for language-specific asset selection.

The atlas-less path applies to files in `images/replace/`. A new image placed
under `images/` still requires valid animation metadata with an atlas and is
not automatically treated as a standalone replacement.

## Verification status

AIM has focused tests for byte-exact atlas-less PNG replacement and metadata
dimension adjustment. A real third-party mod using `images/replace/` should be
tested before this behavior is presented as a broadly supported user feature.
