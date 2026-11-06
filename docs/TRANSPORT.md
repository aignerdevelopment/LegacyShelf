# Transport packs

LegacyShelf normally stores its data next to the executable:

```
LegacyShelf.exe
data\
  config.json
  artwork\
  artwork.json
  cover-index.local.json
  logs\
```

A transport pack takes that idea one step further and adds game folders plus the metadata needed to
run without the original launcher or the Windows registry.

## Creating a pack

- Top bar **Pack** button, or right-click a game → **Pack this game for transport…**.
- Select any set of games, or open it from a tile to preselect one.
- Choose a destination folder that does not exist yet.
- Optionally include the original launcher catalog and cover cache.

The packer refuses destinations inside a game folder, inside an install root, or inside
LegacyShelf's own data folder. It checks free space and copies with progress/cancel support.

## Pack layout

```
LegacyShelf-Transport-YYYYMMDD-HHmm\
  LegacyShelf.exe
  LegacyShelf.exe.config
  LegacyShelf.portable
  README-TRANSPORT.txt
  transport-manifest.json
  Games\
    <game folder>\
  data\
    config.json              roots are "Games"; launcher data is "LauncherData"
    artwork\
    artwork.json
    cover-index.local.json
    portable-games.json
  LauncherData\              optional
    app-state.json           sanitized public catalog
    Cache\Cache_Data\f_*     original cover bodies
```

## Why it works on a new PC

1. `AppPaths` prefers `<exe>\data`, so the pack runs portable.
2. `ConfigStore` resolves `Games` and `LauncherData` relative to the executable.
3. `LibraryScanner` reads `data\portable-games.json` before the registry/folder scan. Each entry
   carries the installer UUID, display name, relative folder, and executable name.
4. `ArtworkImporter` finds the copied cover by the same stable game ID.
5. The sanitized `LauncherData\app-state.json` supplies catalog names/descriptions if present.

No installer, registry import, store account, or original launcher is required.

## Sanitization

`LauncherData\app-state.json` is generated from the public catalog fields only:

- `installer_uuid`
- `game_name`
- `game_description`
- `game_coverart`
- `game_installed_size`

The original account token, profile, reviews, cookies, local storage, and logs are never copied.

## Limitations

- Game files are copied, not moved; packing can take a long time and needs free space.
- Saves stored inside the game folder are included. Saves in `Documents` or `%APPDATA%` are not
  discovered automatically.
- Reparse points/junctions inside game folders are skipped.
- The pack contains copyrighted game files and cover art; keep it private.
- The destination must be on a writable drive for LegacyShelf to use the pack's `data\`.
