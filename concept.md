# LegacyShelf — Concept & Technical Design

**Status:** Implemented in v0.1.0 · 2026-11-06  
**Project:** an unofficial, offline Windows launcher for an already-installed Legacy Games library  
**License:** 0BSD (proposed — least restrictive OSI-approved license)  
**Working name:** LegacyShelf (easy to rename; not affiliated with Legacy Games)

---

## 1. One-liner

**LegacyShelf turns a folder of installed Legacy Games into a fast, searchable, full-screen shelf.**

No store, no account, no network, no re-downloads. It reads what is already on disk, shows the
original cover art, and starts the game with a double-click.

---

## 2. What we found on this machine

These observations drive the design. They are from a real installed collection, not assumptions.

| Finding | Detail |
|---|---|
| Installed games | **59** game folders under `F:\LegacyGames` |
| Registry entries | **59** keys under `HKCU\Software\Legacy Games`, each with `InstDir`, `ProductName`, `GameExe`, `InstallerUUID` |
| Launcher data | `%APPDATA%\legacy-games-launcher`, including `app-state.json` (7.4 MB), `Cache\Cache_Data`, logs |
| Catalog metadata | `app-state.json` → `siteData.catalog` has **976 products**, **2,413 game entries**, **941 unique installer UUIDs** |
| Matched metadata | all **59** installed UUIDs have a catalog entry (name, description, cover-art URL) |
| Cached artwork | **118** image files in `Cache\Cache_Data` (raw JPEG/PNG, no extension); 55 are square 600×600 covers |
| Exact art matches | **53 of 59** cached covers are byte-for-byte identical to the official catalog cover URLs |
| Missing cover cache | 6 games (e.g. *Hook*, *Naboki*, *Lilas Sky Ark*) do not have their cover in the cache; the catalog URLs still work today |
| Great edge case | the registry key for *Lilas Sky Ark* wrongly says `InstDir = F:\LegacyGames`, but the game is really in `F:\LegacyGames\Lilas Sky Ark`. The scanner must repair this. |

**Important:** `app-state.json` also contains account/profile data and an API token. LegacyShelf will
parse only the catalog game metadata and will never read, copy, or log the `user` section.

---

## 3. Goals and non-goals

### Goals

1. **First run is a two-step setup:** choose the installed-games folder, optionally choose the
   original launcher data folder, scan, done.
2. **A beautiful tiled shelf:** cover art, game names, responsive grid.
3. **Search as you type** and keyboard navigation.
4. **Double-click (or Enter) launches the game** with the correct working directory.
5. **Alt+Enter toggles full screen**, Esc / Alt+Enter returns.
6. **Top-right rescan button** opens the requested dialog:
   *Add installed directory*, *Edit installed directory*, *Edit launcher directory*, *Rescan*.
7. **Tiny footprint:** no Electron, no Chromium, no bundled runtime, no NuGet packages.
8. **Fully offline:** no telemetry, no store calls, no account. Optional cover enrichment is a
   one-time, explicit, user-initiated setup action only.
9. **Open source and reusable:** other people can point it at their own installed collection.
10. **Graceful degradation:** missing exe, moved folder, absent metadata, or absent cover art must
    never break the shelf.

### Non-goals (v1)

- Downloading, installing, updating, or repairing games.
- Bypassing DRM or purchasing logic.
- A store, recommendations, reviews, cloud sync, or accounts.
- Cross-platform support (the design stays portable in spirit, but v1 is Windows).
- Perfect automatic artwork matching without any user input.
- Mods, emulators, or launch arguments for every engine.

---

## 4. Recommended tech stack

### Decision

**C# + WPF on .NET Framework 4.8**, one SDK-style project, **zero NuGet/runtime dependencies**.

| Concern | Choice |
|---|---|
| Language | C# (modern language version, async/await) |
| UI | WPF (XAML), custom dark theme, `ItemsControl` + `WrapPanel` |
| Runtime | .NET Framework 4.8 — included with Windows 10/11; no installer |
| JSON | `System.Web.Extensions` (`JavaScriptSerializer`) or a small internal parser |
| Images | WPF `BitmapImage` (display) + `System.Drawing` (thumbnail generation) |
| Process launch | `System.Diagnostics.Process` |
| Registry | `Microsoft.Win32.Registry` |
| Build | .NET SDK 9/10 (`dotnet build`), verified working for `net48` |
| Distribution | one small `.exe` (+ optional `.config`), zipped; no setup required |

A hello-world WPF `net48` app built with the installed .NET 9 SDK produced a **5 KB exe**. The real
app should land in the low hundreds of KB.

### Why this stack

- **No large dependency.** WPF and .NET Framework 4.8 are already on the target OS. There is no
  Electron/Chromium/WebView2/runtime download.
- **Native access where it matters:** registry, file system, process creation, window management.
- **Great fit for a tiled image UI:** WPF handles DPI scaling, image caching, animations, and
  custom window chrome well.
- **Low contributor friction:** plain C#, XAML, and the standard SDK. No Node/Rust toolchain.
- **Small, auditable codebase:** roughly 1,500–2,500 lines for v1.

### Alternatives considered

| Option | Verdict |
|---|---|
| **Electron** | Rejected: 100–200 MB runtime and memory for a 59-tile grid. |
| **Tauri (Rust + WebView2)** | Good alternative, small binary, modern web UI. Rejected for v1 because it adds Rust + Node build tooling and a WebView2 dependency for little gain here. |
| **WinUI 3 / Windows App SDK** | Rejected: runtime/deployment complexity; not preinstalled everywhere. |
| **WinForms** | Viable but dated visuals; WPF gives a better shelf with the same runtime. |
| **Avalonia / .NET 8 self-contained** | Cross-platform, but a self-contained publish is large; unnecessary for a Windows-only shelf. |
| **Python + PySide** | Rejected: large dependency and packaging burden. |

> If the project later wants a web-based UI, the scanning/launch services are UI-free and can be
> reused behind Tauri or Photino. The v1 decision is WPF.

---

## 5. User experience

### 5.1 First run

A single welcome window:

```
┌──────────────────────────────────────────────────────────────┐
│  LegacyShelf                                                 │
│  Your games. Your shelf. Offline.                            │
│                                                              │
│  Installed games folder                                      │
│  [ F:\LegacyGames                              ] [ Browse ]  │
│  ✓ 59 game folders detected                                  │
│                                                              │
│  Original launcher data (optional, for cover art)            │
│  [ C:\Users\…\AppData\Roaming\legacy-games-launcher ] [ … ]  │
│  ✓ Cache and catalog metadata found                          │
│                                                              │
│  [ ] Fetch missing covers once (requires internet)            │
│      Covers are saved locally; the launcher stays offline.   │
│                                                              │
│                              [ Skip ]   [ Scan library → ]   │
└──────────────────────────────────────────────────────────────┘
```

Behavior:

- Auto-detect the installed root from:
  1. `settings.gameLibraryPath` in the launcher's `app-state.json`, if the data folder is found;
  2. the common parent of all `InstDir` values in the registry;
  3. otherwise empty.
- Auto-detect the launcher data folder at `%APPDATA%\legacy-games-launcher`.
- Validate the installed root by finding at least one plausible game folder.
- Validate the launcher folder by finding `Cache\Cache_Data` or `app-state.json`.
- If the original launcher is running, warn that its cache is locked and offer:
  *Continue with icons*, or *Close the launcher and retry*.
- Scan runs in the background with progress; the window never freezes.
- If the user skips the launcher folder, the app still works using `icon.ico` / exe icons and
  generated placeholder tiles.

### 5.2 Main shelf

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  LegacyShelf        [ Search games…                    ]  59 games   [⟳]     │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│   ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐           │
│   │  cover  │  │  cover  │  │  cover  │  │  cover  │  │  cover  │           │
│   │         │  │         │  │         │  │         │  │         │           │
│   └─────────┘  └─────────┘  └─────────┘  └─────────┘  └─────────┘           │
│   100 Doors    Angelo…      Around…      Atlantis…    Big Adventure…        │
│                                                                              │
│   ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐           │
│   │  cover  │  │  cover  │  │  cover  │  │  cover  │  │  cover  │           │
│   └─────────┘  └─────────┘  └─────────┘  └─────────┘  └─────────┘           │
│   Chimp…       Christmas…   Christmas…   City…        City…                 │
│                                                                              │
│  Double-click to play · Ctrl+F search · Alt+Enter full screen · Esc back     │
└──────────────────────────────────────────────────────────────────────────────┘
```

- **Tile:** square cover, `UniformToFill` crop, rounded corners, subtle border, title underneath.
- **Hover:** slight lift + brighter border + a small “Play” pill over the cover.
- **Double-click** anywhere on the tile launches.
- **Right-click menu:** Play · Open game folder · Change artwork… · Locate executable… · Hide game.
- **Search:** filters by display name, folder name, and exe name; accent- and case-insensitive.
  `Ctrl+F` focuses, `Esc` clears.
- **Keyboard:** arrows move focus, `Enter` plays, `Home/End` jump, `PageUp/PageDown` scroll.
- **Status:** count updates with the filter; a small “missing files” badge appears on broken tiles.
- **Responsive:** tile size adapts to window width (roughly 4–9 columns); no horizontal scroll.
- **Empty state:** friendly instructions + “Open sources” button if the library is empty.

### 5.3 Full screen

- `Alt+Enter` toggles borderless maximized full screen.
- In full screen: custom title bar hides; a translucent top bar appears when the mouse moves near
  the top edge and fades after a short delay.
- `Esc` or `Alt+Enter` returns to the windowed view.
- The chosen state is remembered between sessions.
- The launcher does not force itself on top of games; it yields focus when a game starts.

### 5.4 Rescan dialog

Top-right **⟳** opens a compact “Library sources” dialog with exactly the requested actions:

```
┌──────────────────────────────────────────────────────────────┐
│  Library sources                                         ✕   │
│                                                              │
│  Installed game directories                                  │
│   F:\LegacyGames                         [Edit] [Remove]      │
│   [ + Add installed directory… ]                             │
│                                                              │
│  Original launcher data                                      │
│   C:\Users\…\AppData\Roaming\legacy-games-launcher  [Edit]    │
│                                                              │
│  Last scan: 6 Nov 2026 08:12 · 59 games · 53 covers          │
│                                                              │
│                         [ Cancel ]   [ Rescan now ]          │
└──────────────────────────────────────────────────────────────┘
```

- **Add installed directory…** — folder picker, appends a root.
- **Edit installed directory…** — select a root, change it or remove it.
- **Edit launcher directory…** — folder picker; “Clear” supported.
- **Rescan** — closes the dialog and runs a full scan with progress.
- A rescan never loses artwork overrides, hidden flags, or play history. It only refreshes the
  discovered game list.

### 5.5 Launch lifecycle

1. Resolve the exe and working directory.
2. Start the process with `WorkingDirectory = <game folder>` and no arguments by default.
3. Log launch time, game id, exe path, and PID.
4. Minimize the launcher (default) or leave it open, per setting.
5. When the game exits, restore the launcher and record playtime.
6. On failure, show a clear dialog with the error and a “Open log” link; never crash.

No shell string is constructed. `UseShellExecute` / `ProcessStartInfo.ArgumentList` equivalent is
used safely, and only a validated `.exe` inside the game folder is launched.

---

## 6. Scanning and metadata

### 6.1 Sources, in priority order

1. **Registry** — `HKCU\Software\Legacy Games\*` is the authoritative list of games installed by
   the original launcher. Values: `InstDir`, `ProductName`, `GameExe`, `InstallerUUID`.
2. **Folder scan** — every configured root is walked (default depth 2) for game folders. This
   catches manually copied games and installs the registry missed.
3. **Catalog metadata** — `app-state.json` → `siteData.catalog[].games[]` supplies canonical
   names, descriptions, cover URLs, and installer UUIDs.
4. **Artwork cache** — `Cache\Cache_Data\f_*` supplies the original cover images.

### 6.2 Discovery algorithm

```
for each root:
    enumerate subdirectories (depth ≤ 2)
    for each candidate directory:
        if it contains an allowed .exe:
            create a provisional game
        else if it has icon.ico/legacygames.ico and a child has an .exe:
            create a provisional game from the child

read registry entries into the same list
deduplicate by normalized install path (case-insensitive)
for each registry entry:
    if InstDir/GameExe does not exist:
        search InstDir and its immediate subdirectories for GameExe
        if found, repair the install path in memory
        else mark the game "missing files" and keep it visible
merge folder-only games by normalized exe path
```

Allowed exe rules:

- Prefer the registry `GameExe` (strip a leading `.\`).
- Otherwise, prefer an exe whose normalized name matches the folder name.
- Otherwise, choose the largest plausible exe.
- Exclude: `unins*`, `uninstall*`, `UnityCrashHandler*`, `crashpad_handler*`, `vc_redist*`,
  `DXSETUP*`, `dotnet*`, `python*`, and other obvious helpers.
- If no exe is found, keep the game as **missing** rather than hiding it.

### 6.3 Stable identity

- `installer_uuid` when present.
- Else `path:` + SHA-1 of the normalized exe path.
- Artwork overrides, hidden flags, and play history are keyed by this stable id.

### 6.4 Display names

Priority:

1. Catalog `game_name` (proper punctuation, e.g. “100 Doors: Escape from School”).
2. Registry `ProductName`.
3. Folder name with light cleanup (`CE` preserved, underscores → spaces).

### 6.5 Catalog parsing and privacy

- The importer reads only:
  - `settings.gameLibraryPath` (for auto-detect), and
  - `siteData.catalog[].games[]` (`game_name`, `game_description`, `game_coverart`,
    `installer_uuid`, `game_installed_size`).
- It explicitly ignores `user`, `token`, `profile`, `customersReviews`, and all account data.
- Nothing from `app-state.json` is copied into the app's config except the public metadata above.
- If the file is missing or malformed, the app logs a warning and continues with registry/folder
  data.

### 6.6 Scan result cache

`library.json` stores the last scan (games + artwork paths + timestamps) so startup is instant.
A background scan refreshes it. The file is disposable and safe to delete.

---

## 7. Artwork pipeline

### 7.1 Principle: copy once, then never depend on the original launcher

The original cache is locked while the launcher runs and may disappear. LegacyShelf therefore
**imports** chosen images into its own folder:

```
%APPDATA%\LegacyGamesLauncher\
  artwork\
    <stable-id>.jpg
  artwork.json
```

At runtime the shelf reads only from `artwork\`; the original cache is needed only during import.

### 7.2 Source priority

| Priority | Source | Notes |
|---:|---|---|
| 1 | Explicit user override | Right-click → Change artwork…; stored in `artwork.json`. |
| 2 | `<game folder>\cover.jpg`, `folder.jpg`, `poster.jpg`, `boxart.png` | Drop-in convention for manual art. |
| 3 | Original cache image with an exact SHA-256 match | 53/59 games on this machine. |
| 4 | Optional one-time online fetch of the catalog cover URL | Only if the user ticks the box; saved locally. |
| 5 | `<game folder>\icon.ico` | 256×256 extraction; available for all 59. |
| 6 | Icon from the game exe | `ExtractAssociatedIcon` fallback. |
| 7 | Generated placeholder | Initials + deterministic color from the stable id. |

### 7.3 Why SHA-256 matching works

The cached files are the raw HTTP response bodies, without extension or URL header. They are
byte-for-byte identical to the official cover images. Computing SHA-256 over each `f_*` file and
comparing it with a cover index gives an exact, unambiguous match — no fuzzy heuristics required.

Observed coverage on this collection:

- **53 / 59** exact cache matches.
- 6 missing covers can be fetched once while `legacygames.com` is still reachable.

### 7.4 Cover index

A small `data/cover-index.json` (generated, no image bytes) maps:

```json
{
  "version": 1,
  "source": "legacygames.com catalog metadata",
  "covers": [
    {
      "uuid": "9d9b6980-57a3-4e98-bd5f-636e677dcc8a",
      "name": "100 Doors: Escape from School",
      "url": "https://legacygames.com/wp-content/uploads/Legacy-Games_100-Doors-Escape-from-School.png",
      "sha256": "…",
      "width": 600,
      "height": 600
    }
  ]
}
```

Two ways to use it:

- **Offline:** hash local cache files, look up the hash, copy the matching image.
- **Online (optional):** download any missing covers from `url`, hash and save them.

The repository may ship an index for the maintainer's collection (the 59 games) and a generator
script so any user can build an index for their own catalog. No copyrighted images are committed.

### 7.5 Normalization

- Square tiles use `UniformToFill`, so the original 600×600 art needs no destructive crop.
- Imported images are copied at up to 800×800 (or original size if smaller) as JPEG quality 90;
  transparent PNGs stay PNG.
- A 320–400 px thumbnail is decoded for the grid; full art is available for a future detail view.
- Never modify files inside the game folders.

### 7.6 Manual control

- Right-click a tile → **Change artwork…** → file picker.
- In the sources dialog → **Review artwork** (optional, v1.1): shows unmatched cache images and
  lets the user assign them.
- “Reset artwork” restores automatic selection.
- Hidden games are excluded from the shelf but remain in the sources list / a “Show hidden” toggle.

---

## 8. Architecture

```
F:\LegacyGamesDev\
  LegacyShelf.sln
  src\
    LegacyShelf\
      LegacyShelf.csproj        (net48, UseWPF, no packages)
      App.xaml / App.xaml.cs
      MainWindow.xaml / .cs
      Views\
        WelcomeWindow.xaml / .cs
        SourcesDialog.xaml / .cs
        ArtworkReviewWindow.xaml / .cs      (v1.1)
      Controls\
        GameTile.xaml / .cs
      Models\
        Game.cs
        AppConfig.cs
        CoverIndex.cs
      Services\
        ConfigStore.cs          (atomic JSON read/write)
        RegistryScanner.cs      (HKCU\Software\Legacy Games)
        FolderScanner.cs        (roots, exe rules)
        LibraryScanner.cs       (merge, dedupe, repair)
        LauncherDataReader.cs   (narrow app-state/catalog read)
        CoverIndexService.cs    (SHA-256 matching)
        ArtworkImporter.cs      (copy, normalize, overrides)
        ImageCache.cs           (decode, thumbnails)
        GameLauncher.cs         (safe process start, exit tracking)
        Log.cs                  (rolling text log)
        FolderPicker.cs         (modern IFileOpenDialog wrapper)
      Themes\
        Dark.xaml
      Assets\
        logo.png
  tools\
    build-cover-index.ps1       (online, optional)
  docs\
    ARTWORK.md
    SCANNING.md
  LICENSE
  README.md
  concept.md
```

**Rules**

- Services are UI-free and testable; views contain presentation only.
- All slow work runs in `Task.Run`; UI updates on the dispatcher.
- No static mutable state except the logger.
- One place writes config (`ConfigStore`), atomically (temp file + replace).
- The app never writes inside game folders.

---

## 9. Configuration and local data

### 9.1 Paths

- Config/artwork/logs: `%APPDATA%\LegacyGamesLauncher\`
- Portable mode: if `LegacyShelf.portable` exists next to the exe, use `<exe dir>\data\`.
- Logs: `logs\app.log`, rolling at 1 MB × 3 files.
- No data in the registry (read-only access to the original launcher's keys).

### 9.2 `config.json`

```json
{
  "version": 1,
  "sources": {
    "installRoots": [
      "F:\\LegacyGames"
    ],
    "launcherDataDir": "C:\\Users\\<user>\\AppData\\Roaming\\legacy-games-launcher"
  },
  "ui": {
    "fullscreen": false,
    "tileSize": 220,
    "sort": "name",
    "showHidden": false
  },
  "launch": {
    "minimizeWhilePlaying": true,
    "restoreOnExit": true
  },
  "games": {
    "hidden": [],
    "overrides": {}
  }
}
```

### 9.3 `library.json` (generated cache)

```json
{
  "scannedAt": "2026-11-06T08:12:00Z",
  "roots": ["F:\\LegacyGames"],
  "games": [
    {
      "id": "uuid:9d9b6980-57a3-4e98-bd5f-636e677dcc8a",
      "name": "100 Doors: Escape from School",
      "folderName": "100 Doors Escape from School",
      "installDir": "F:\\LegacyGames\\100 Doors Escape from School",
      "exe": "100 Doors Escape From School.exe",
      "uuid": "9d9b6980-57a3-4e98-bd5f-636e677dcc8a",
      "status": "ok",
      "cover": "artwork\\uuid-9d9b6980.jpg",
      "description": "…",
      "lastPlayed": null,
      "playSeconds": 0
    }
  ]
}
```

### 9.4 `artwork.json`

```json
{
  "version": 1,
  "overrides": {
    "uuid:9d9b6980-57a3-4e98-bd5f-636e677dcc8a": {
      "file": "uuid-9d9b6980.jpg",
      "source": "cache",
      "sha256": "…",
      "focusX": 0.5,
      "focusY": 0.5
    }
  }
}
```

---

## 10. Behavior details

### Keyboard

| Key | Action |
|---|---|
| `Ctrl+F` | Focus search |
| `Esc` | Clear search; if empty, leave full screen |
| `Alt+Enter` | Toggle full screen |
| `Enter` | Launch focused game |
| `Arrow keys` | Move focus |
| `Home` / `End` | First / last game |
| `PageUp` / `PageDown` | Scroll a page |
| `F5` | Rescan |
| `Ctrl+,` | Open sources dialog (optional) |

### Window chrome

- Custom borderless window with a dark title bar and drag region.
- Windowed mode keeps native resize borders via `WindowChrome`.
- Full screen is a maximized borderless window on the current monitor.
- Minimum size: 900 × 600; default: 1280 × 800 (or 70% of work area).
- Remember position/size, but clamp to a visible monitor if displays change.

### Error handling

- Missing exe → tile stays, shows a “Missing” badge; launch shows a short explanation and offers
  **Locate executable…**.
- Missing artwork → placeholder; never an empty tile.
- Unreadable folder → log and continue.
- Corrupt config → back up as `config.bad.json`, start with defaults.
- Launcher running during import → warn; do not block the shelf.

### Logging

- Timestamped, human-readable, no personal data.
- Log scan roots/counts, import decisions, launch PID/exit code, and exceptions.
- Never log the contents of `app-state.json` or any token/profile field.

---

## 11. Open source, license, and legal

### License: 0BSD

The **Zero-Clause BSD** license is the recommended “least restrictive” OSI-approved software
license: use, copy, modify, and distribute for any purpose, **no attribution required**, with the
usual warranty disclaimer. (The Unlicense/CC0 are alternatives if the project prefers a public
domain dedication.)

- `SPDX-License-Identifier: 0BSD` in source headers.
- `LICENSE` at the repository root.
- No contributor license agreement; contributions are accepted under 0BSD.

### What is *not* covered

- Game files, game art, trademarks, and the Legacy Games name remain with their owners.
- The repository contains **no game binaries and no cover art**. Artwork is imported locally from
  the user's own installation at runtime.
- LegacyShelf is unofficial and not affiliated with or endorsed by Legacy Games.

### Privacy and network

- Default behavior: **zero network access**, zero telemetry.
- The optional “Fetch missing covers once” action is the only network operation. It is unchecked by
  default, clearly explained, and can be skipped.
- The app never needs an account, token, or store session.
- The original launcher's `app-state.json` is treated as untrusted input: parse narrowly, never log
  personal fields.

### Ethical scope

LegacyShelf only starts games that are already installed. It does not download, install, crack,
patch, or redistribute games. It is a convenience/organization layer for a library the user
already owns.

---

## 12. Build, test, and release

### Build

```powershell
dotnet build .\LegacyShelf.sln -c Release
# output: src\LegacyShelf\bin\Release\net48\LegacyShelf.exe
```

Optional single-file distribution (still requires .NET Framework 4.8, which is part of Windows):

```powershell
Compress-Archive -Path .\src\LegacyShelf\bin\Release\net48\* -DestinationPath .\dist\LegacyShelf-win-x64.zip
```

### CI

GitHub Actions on `windows-latest`:
1. `dotnet build -c Release`
2. run the scan self-test against a fixture
3. upload `LegacyShelf-win-x64.zip` as an artifact
4. attach to a GitHub release on tags

### Testing without a UI

A `--scan-report <file>` command-line mode writes the detected library and artwork decisions to JSON. This makes
the core testable and gives users a bug-report tool.

Manual acceptance checklist:

- [ ] Fresh config → welcome → detect `F:\LegacyGames` → scan → 59 tiles.
- [ ] Close and reopen → instant shelf from `library.json`.
- [ ] Search “door” → 100 Doors tile remains; others filter out.
- [ ] Double-click a game → it starts in its own folder; launcher restores on exit.
- [ ] Alt+Enter → full screen; Esc → back.
- [ ] Rescan dialog → add/edit/remove a root; rescan updates the shelf.
- [ ] Remove the launcher data folder → tiles fall back to icons, no crash.
- [ ] Rename a game folder → rescan marks it missing but keeps it visible.
- [ ] Lilas Sky Ark registry bug → scanner finds the real exe.
- [x] No network activity during normal startup/scan/launch.

---

## 13. Milestones

| Milestone | Deliverable |
|---|---|
| **M0 — Concept** | This document; sign-off on stack/name/license. |
| **M1 — Skeleton** | WPF project, config store, logger, welcome window, registry + folder scanner, `--scan --json`. |
| **M2 — Shelf** | Main window, tiles, search, keyboard navigation, full screen, empty/error states. |
| **M3 — Artwork** | Cache image scanning, SHA-256 cover matching, import/normalize, icon fallback, right-click override. |
| **M4 — Actions** | Sources dialog (the four requested actions), rescan, launch lifecycle, restore-on-exit, playtime. |
| **M5 — Release** | README, screenshots, LICENSE, CI, zip, first tagged release. |

The first usable build (M1–M2) is deliberately small; artwork polish and edge cases follow.

---

## 14. Acceptance criteria (mapped to the request)

| Request | Implementation |
|---|---|
| Runs full screen | Borderless maximized window; `Alt+Enter` toggles; `Esc`/`Alt+Enter` returns. |
| Shows tiled games | Responsive `WrapPanel` of square cover tiles. |
| Search | Live filter over name/folder/exe; `Ctrl+F`. |
| Double-click start | Tile double-click or `Enter`; correct working directory; safe process start. |
| First start selects installed dir | Welcome window with auto-detect + Browse; validation. |
| Use original launcher images | SHA-256 exact match of `Cache_Data\f_*`; copies locally; 53/59 automatic, rest via icon or optional fetch. |
| Top-right rescan button | `⟳` opens “Library sources”. |
| Dialog actions | Add installed directory · Edit installed directory · Edit launcher directory · Rescan. |
| Small, no big dependencies | WPF/.NET Framework 4.8, no NuGet, no Electron; tiny exe. |
| Open source, least restrictive | 0BSD; no game art in repo; unofficial disclaimer. |
| Others can reuse | Registry/folder scan, generic cover index, manual artwork assignment, documented config. |

---

## 15. Risks and decisions needed

1. **Stack sign-off.** .NET Framework 4.8 WPF is the recommendation. If you prefer a web UI,
   Tauri is the fallback; it costs a Rust/Node build toolchain.
2. **Project name.** “LegacyShelf” is a working title. Alternatives: “Afterstore”, “ShelfGames”,
   “Legacy Vault”. The code and license do not depend on the name.
3. **Optional cover fetch.** Recommended: allow a one-time, unchecked-by-default fetch while the
   old store is still reachable, then stay offline forever. Without it, 6 of 59 games fall back to
   icons unless the user assigns art manually.
4. **Launch behavior.** Default plan: minimize the launcher and restore it when the game exits.
   Alternative: leave it open behind the game.
5. **Artwork matching for other users.** Exact SHA-256 matching needs a cover index. The repo can
   ship a generator and an index for this collection; other users build their own index or assign
   art manually.
6. **Distribution.** Prebuilt zip vs. build-from-source. Recommendation: do both; the source is
   the source of truth.

---

## 16. Appendix — observed samples

### Registry sample

```
HKCU\Software\Legacy Games\100 Doors Escape From School
  (default)     F:\LegacyGames\100 Doors Escape from School
  InstDir       F:\LegacyGames\100 Doors Escape from School
  InstallerUUID 9d9b6980-57a3-4e98-bd5f-636e677dcc8a
  ProductName   100 Doors Escape From School
  GameExe       100 Doors Escape From School.exe
```

### Catalog sample

```
siteData.catalog[].games[]:
  game_name:        "100 Doors: Escape from School"
  game_coverart:    "https://legacygames.com/wp-content/uploads/Legacy-Games_100-Doors-Escape-from-School.png"
  installer_uuid:   "9d9b6980-57a3-4e98-bd5f-636e677dcc8a"
  game_installed_size: "58.7 MB"
```

### Cache artwork

- `Cache\Cache_Data\f_000043` — 49,444 bytes — JPEG — *Road Trip USA* cover.
- `Cache\Cache_Data\f_000056` — 149,283 bytes — PNG — *100 Doors: Escape from School* cover.
- Files have no extension; format must be detected by magic bytes.
- The cache index/data files may be locked while the launcher runs; only the readable cover bodies (`f_*`) are needed.

### Why the exact-match approach is reliable

The official cover for *Road Trip USA* is 49,444 bytes and hashes exactly to `f_000043`. The same is
true for 52 other games. This is not a guess based on filenames or ordering — it is a cryptographic
identity check.

---

*End of concept. Next step: sign off on the stack and name, then start M1.*

