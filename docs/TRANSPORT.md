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

A transport pack separates the launcher from the games:

- one **launcher zip** whose root contains `LegacyShelf.exe` and `data\`;
- one **game zip per selected game**, whose root contains exactly that game folder.

## Creating a pack

- Top bar **Pack** button, or right-click a game → **Pack this game for transport…**.
- Select any set of games. Opening from a tile preselects only that game.
- Choose a destination folder that does not exist yet.
- Optionally include the original launcher catalog and cover cache.

Output:

```
LegacyShelf-Transport-YYYYMMDD-HHmm\
  LegacyShelf-Launcher.zip
  <Game folder name>.zip
  <Game folder name>.zip
  README-TRANSPORT.txt
  transport-manifest.json
```

`LegacyShelf-Launcher.zip`:

```
LegacyShelf.exe
LegacyShelf.exe.config
LegacyShelf.portable
data\config.json
data\artwork\...
data\artwork.json
data\cover-index.local.json
data\portable-games.json
LauncherData\...        optional, sanitized
```

Every game zip:

```
<Game folder name>\
  <game files...>
  .legacyshelf-game.json
```

The metadata file inside the game folder makes even a manually unpacked game zip self-describing.
The game zip root is the proper game folder, so `Extract here` produces the expected layout.

## On the new PC

1. Create a folder such as `D:\LegacyGames`.
2. Extract `LegacyShelf-Launcher.zip` into it. The exe is at the root.
3. Extract some or all game zips into the same folder.
4. Run `LegacyShelf.exe`.

If you copy game zips into that folder without extracting them, LegacyShelf's **Unzip** button or
startup prompt extracts them, verifies the destination folders, and asks whether to delete the zips
once all of them are unpacked.

There is no confirmation prompt when free space is sufficient: packing and unpacking start
immediately. If the destination is too small, a themed **This won't fit** dialog shows needed,
available, and shortfall values, plus a **Choose another folder** action for packs.

## Missing games

`data\portable-games.json` keeps every packed game's id, UUID, name, relative folder, executable,
and zip name. If a game folder is missing:

- the game is hidden from the shelf by default;
- its metadata is **not** deleted;
- it reappears after the zip is extracted and the library is rescanned.

Library sources has **Show games that are not unpacked** for users who want to see the hidden
entries with a *Not unpacked* badge.

## Sanitization

`LauncherData\app-state.json` is generated from the public catalog fields only:

- `installer_uuid`
- `game_name`
- `game_description`
- `game_coverart`
- `game_installed_size`

The original account token, profile, reviews, cookies, local storage, and logs are never copied.

## Limitations

- Game files are zipped, not moved; packing can take a long time and needs free space.
- Saves stored inside the game folder are included. Saves in `Documents` or `%APPDATA%` are not
  discovered automatically.
- Reparse points/junctions inside game folders are skipped.
- The zips contain copyrighted game files and cover art; keep them private.
- The launcher folder must be writable so the portable `data\` can be used.
