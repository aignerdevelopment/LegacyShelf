# LegacyShelf

**Your games. Your shelf. Offline.**

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
- **Minimize while playing** — the launcher gets out of the way and comes back when the game exits.
- **Rescan dialog** — add/edit installed directories, edit the original launcher directory, rescan.
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
git clone <your-repository-url>
cd LegacyGamesDev
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

The app writes its own data to `%APPDATA%\LegacyGamesLauncher\` by default:

```
config.json            user settings and source folders
library.json           last scan (disposable)
artwork.json           artwork choices and overrides
artwork\               imported and normalized cover images
cover-index.local.json learned cover hashes
logs\app.log           rolling log
```

**Portable mode:** create an empty file named `LegacyShelf.portable` next to `LegacyShelf.exe`.
The app will then use `<exe folder>\data\` instead of `%APPDATA%`.

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
| Right-click a tile | Play, open folder, change artwork, locate executable, hide |

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
  Services\                    scanning, registry, artwork, launching, config, logging
  Themes\                      dark WPF theme
  Views\                       welcome and library-sources dialogs
  Resources\cover-index.json   public metadata/hashes (no images)
tools\build.ps1                release zip
tools\build-cover-index.ps1    generate a cover index from app-state.json
docs\                          scanning and artwork notes
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
