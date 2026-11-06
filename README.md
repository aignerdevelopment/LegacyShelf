# LegacyShelf

**Your games. Your shelf. Offline.**

[![build](https://github.com/aignerdevelopment/LegacyShelf/actions/workflows/build.yml/badge.svg)](https://github.com/aignerdevelopment/LegacyShelf/actions/workflows/build.yml)
[![release](https://github.com/aignerdevelopment/LegacyShelf/actions/workflows/release.yml/badge.svg)](https://github.com/aignerdevelopment/LegacyShelf/actions/workflows/release.yml)

**Download:** [latest release](https://github.com/aignerdevelopment/LegacyShelf/releases/latest)

LegacyShelf is an unofficial, open-source Windows launcher for a Legacy Games library that is
already installed on your PC. It scans the game folders, imports the original cover art from the
old launcher's cache, and presents everything as a fast, searchable, full-screen shelf. It never
downloads games and never needs an account or the old store.

> LegacyShelf is not affiliated with, endorsed by, or supported by Legacy Games. It only starts
> games that are already installed. It does not download, install, crack, patch, or redistribute
> anything.

---

## Features

- **First-run setup** — choose the installed-games folder; optionally point at the original
  launcher data for cover art.
- **Tiled cover shelf** — responsive square tiles, hover play affordance, missing-file badges.
- **Live search** — filters by game name, folder, or executable; `Ctrl+F` focuses it.
- **Double-click to play** — or select a tile and press `Enter`.
- **Full screen** — `Alt+Enter` toggles; `Esc` or `Alt+Enter` returns.
- **Minimize while playing** - the launcher gets out of the way and comes back when the game exits.
- **Portable by default** - settings, artwork, and logs live in `data\` next to the exe when that
  folder is writable; otherwise LegacyShelf falls back to `%APPDATA%\LegacyGamesLauncher`.
- **Transport packs** - select games and create one launcher zip plus one zip per game, with
  LegacyShelf settings/artwork and optionally sanitized original launcher data. Games can be
  unpacked later; missing ones stay hidden but remembered.
- **Rescan dialog** - add/edit installed directories, edit the original launcher directory, rescan.
- **Artwork import** — exact SHA-256 matching against the original launcher cache, with icon and
  placeholder fallbacks. Right-click any tile to choose a different image.
- **Tiny footprint** — WPF on .NET Framework 4.8; no Electron, no bundled browser, no NuGet runtime
  packages, no installer.
- **Offline by default** — no telemetry, no store calls, no account. An optional, unchecked
  one-time cover fetch is the only network action.
- **0BSD** — the least restrictive OSI-approved license; no attribution required.

---

## Requirements

- Windows 10 or Windows 11
- .NET Framework 4.8 (included with Windows 10/11; no separate install)
- The games you already installed

The release is a small `.exe`; it does not need an installer. Put it anywhere writable and run it.

---

## Build from source

You need the .NET SDK (9 or newer is fine; it can target .NET Framework 4.8) or Visual Studio /
MSBuild with the .NET Framework 4.8 targeting pack.

```powershell
git clone https://github.com/aignerdevelopment/LegacyShelf.git
cd LegacyShelf
dotnet build .\LegacyShelf.sln -c Release
```

Output:

```
src\LegacyShelf\bin\Release\net48\LegacyShelf.exe
```

Create a distributable zip with:

```powershell
.\tools\build.ps1
```

Versioned local package:

```powershell
.\tools\build.ps1 -Version 1.0.0
# dist\LegacyShelf-1.0.0-win-x64.zip
```

## Release pipeline

- **build** runs on every push to `main` and every pull request. It builds the app, runs the
  scanner smoke test, and uploads the zip artifact.
- **release** is manually triggerable from **Actions → release → Run workflow**. Enter the version,
  for example `1.0.0`, optionally mark it as a pre-release, and run it.
  It builds `LegacyShelf-<version>-win-x64.zip`, generates `SHA256SUMS.txt`, and creates or updates
  the GitHub Release and `v<version>` tag.

Releases can also be triggered by pushing a version tag:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The app is **portable by default**. It writes its data to `<exe folder>\data\` when that folder is
writable:

```
data\config.json            user settings and source folders
data\library.json           last scan (disposable)
data\artwork.json           artwork choices and overrides
data\artwork\               imported and normalized cover images
data\cover-index.local.json learned cover hashes
data\portable-games.json    only present in a transport pack
data\logs\app.log           rolling log
```

If `<exe folder>\data\` cannot be created or written (for example LegacyShelf was placed in
`Program Files`), it falls back to `%APPDATA%\LegacyGamesLauncher\`. On first launch it migrates an
existing appdata installation into the portable data folder.

An empty `LegacyShelf.portable` marker is still honored as an explicit intent marker, but it is no
longer required. Paths under the executable folder are stored relative in `config.json`, so the
whole portable folder can be moved.

---

## First run

1. Run `LegacyShelf.exe`.
2. Confirm the **installed games folder** (the folder that contains one subfolder per game).
3. Optionally confirm the **original launcher data** folder. It is usually
   `%APPDATA%\legacy-games-launcher`; it is only read, never modified.
4. Optionally tick **Fetch missing covers once**. This downloads only missing cover images from the
   old catalog and saves them locally; normal use stays fully offline.
5. Press **Scan library**.

LegacyShelf also reads `HKCU\Software\Legacy Games` for the authoritative list of installed games
and their executables. A folder scan catches manually copied games. If a registry entry points at a
parent folder instead of the game folder (a real bug seen with *Lilas Sky Ark*), the scanner repairs
it automatically.

---

## Transport packs (move everything to a new PC)

Use the **Pack** button in the top bar, or right-click a game and choose
**Pack this game for transport…**.

The dialog lets you:

- pick any set of installed games (or all of them);
- choose a new destination folder;
- include the original launcher catalog and cover cache (sanitized; no account/profile data).

The result is a set of zips:

```
LegacyShelf-Transport-YYYYMMDD-HHmm\
  LegacyShelf-Launcher.zip       exe + data/ + optional LauncherData/ at the zip root
  100 Doors Escape from School.zip
  100 Doors Escape from School.zip's root contains exactly that game folder
  ...
  README-TRANSPORT.txt
  transport-manifest.json
```

On the new PC:

1. Create a folder, for example `D:\LegacyGames`.
2. Extract **LegacyShelf-Launcher.zip** into it. `LegacyShelf.exe` lands directly in that folder.
3. Extract any game zips into the same folder. Each zip contains its own game folder at the root.
4. Run `LegacyShelf.exe`.

You can also copy the game zips next to `LegacyShelf.exe` instead of extracting them. The launcher
detects them, offers **Unzip**, extracts each zip into the correct folder, and asks whether to delete
the zips when all of them are unpacked.

Packing and unpacking do **not** show a “are you sure?” dialog when there is enough free space —
they start immediately. If the destination cannot hold the pack or extraction, a themed
**This won't fit** dialog shows the needed space, available space, and shortfall, with an option to
choose another folder.

The launcher zip contains:

```
LegacyShelf.exe
LegacyShelf.exe.config
LegacyShelf.portable
data\
  config.json              roots: ".", "LauncherData"
  artwork\                 selected cover art
  artwork.json
  cover-index.local.json
  portable-games.json      id, uuid, name, relative folder, zip name, exe
LauncherData\              optional, sanitized
  app-state.json           public catalog metadata only
  Cache\Cache_Data\f_*
```

Games that are not unpacked yet are **hidden but remembered**. Their metadata stays in
`data\portable-games.json`; extract them later and they reappear. Enable
**Show games that are not unpacked** in Library sources to see them with a *Not unpacked* badge.

Notes:

- Game files are **copied into zips**, not moved. Packing 40+ GB takes time and needs free space.
- Saves stored inside the selected game folders are included. Saves kept in `Documents` or
  `%APPDATA%` are not automatically copied.
- The zips contain game files and copyrighted cover art. Do not publish them.
- The sanitized `LauncherData\app-state.json` contains only public catalog fields; the original
  launcher's account token, profile, reviews, and local storage are never copied.
- Keep the folder on a writable drive. If it is read-only, LegacyShelf falls back to `%APPDATA%`
  and cannot see the portable `data\`.

---

## Controls

| Input | Action |
|---|---|
| Double-click a tile | Play |
| `Enter` | Play the selected game (or the first search result) |
| `Ctrl+F` | Focus search |
| `Esc` | Clear search; if empty, leave full screen |
| `Alt+Enter` | Toggle full screen |
| Arrow keys | Move selection |
| `F5` | Rescan |
| `Ctrl+mouse wheel` | Change tile size |
| **Pack** button | Create a transport pack (launcher zip + game zips) |
| **Unzip** button | Extract game zips in the launcher folder |
| Right-click a tile | Play, open folder, change artwork, locate executable, pack this game, hide |

The top-right **⟳** button opens **Library sources**, with:

- **Add installed directory…**
- **Edit installed directory…**
- **Edit launcher directory…**
- **Rescan now**

---

## Command-line scan report

Useful for debugging, CI, and sharing a library report without opening the UI:

```powershell
LegacyShelf.exe --scan-report report.json `
  --install-root "F:\LegacyGames" `
  --launcher-dir "%APPDATA%\legacy-games-launcher" `
  --fetch-covers
```

`--no-art` skips artwork processing. The report contains the discovered games and artwork counts;
it does not contain account data.

---

## Artwork

LegacyShelf uses this priority:

1. Explicit artwork chosen by the user.
2. `cover.jpg`, `folder.jpg`, `poster.jpg`, `boxart.png`, or similar files dropped into the game folder.
3. A cover from the original launcher cache that matches the catalog cover **byte-for-byte** (SHA-256).
4. An optional one-time download from the catalog cover URL.
5. The game's `icon.ico`.
6. A generated placeholder.

The original cache files have no extension, so the importer detects JPEG/PNG/WebP/GIF/BMP/ICO by
magic bytes. The cache body files (`Cache_Data\f_*`) are readable even while the old launcher is
running; LegacyShelf never depends on the locked cache index or on the old launcher being installed.

**No cover art is included in this repository.** Images are imported from the user's own installation
at runtime. `src\LegacyShelf\Resources\cover-index.json` contains only UUIDs, names, URLs, and
SHA-256 hashes. `tools\build-cover-index.ps1` can generate an index for another collection.

---

## Privacy and network

- Normal startup, scanning, and launching are fully offline.
- No telemetry, analytics, accounts, or store sessions.
- The original launcher's `app-state.json` also contains account/profile data. LegacyShelf parses only
  the public catalog fields and `settings.gameLibraryPath`; it never logs, copies, or stores the
  `user`, `token`, profile, or review sections.
- The optional cover fetch only runs when the user ticks the box (or passes `--fetch-covers`).

---

## Project layout

```
concept.md                     design document and rationale
src\LegacyShelf\               WPF application
  Assets\                      original project logo/icon
  Controls\                    game tile view model
  Models\                      config, game, artwork, cache models
  Services\                    scanning, registry, artwork, launching, transport packing, config, logging
  Themes\                      dark WPF theme
  Views\                       welcome, sources, and transport-pack dialogs
  Resources\cover-index.json   public metadata/hashes (no images)
tools\build.ps1                release zip
tools\build-cover-index.ps1    generate a cover index from app-state.json
docs\                          scanning, artwork, and transport notes
```

---

## Contributing

Contributions are welcome under 0BSD. Keep the runtime dependency-free:

- No new NuGet runtime packages without a compelling reason.
- Keep scanning/launching services UI-free and testable.
- Prefer small, readable changes over frameworks.
- Do not add game files, cover art, or account data to the repository.

---

## License

**0BSD** — use, copy, modify, and distribute for any purpose, with or without fee, without
attribution. See [LICENSE](LICENSE).

Game files, cover art, trademarks, and the Legacy Games name remain the property of their
respective owners. This project is unofficial and not affiliated with Legacy Games.
