# Artwork notes

## Cache format

The original launcher stores downloaded cover bodies as `Cache\Cache_Data\f_*` with no extension.
They are raw JPEG, PNG, WebP, GIF, BMP, or ICO payloads. LegacyShelf detects the format from magic
bytes and computes a SHA-256 over the original bytes.

The cache `index` and `data_*` files may be locked while the old launcher runs. LegacyShelf does not
read them. The `f_*` bodies are sufficient.

## Exact matching

The catalog metadata in `app-state.json` maps an installer UUID to a cover URL. The repository's
`Resources\cover-index.json` stores the UUID, name, URL, and SHA-256 of the official cover for the
maintainer's collection. If a cached body has the same hash as the index entry for a game, the match
is exact and unambiguous.

For a different collection, regenerate the index:

```powershell
.\tools\build-cover-index.ps1 `
  -AppStatePath "$env:APPDATA\legacy-games-launcher\app-state.json" `
  -OutputPath ".\src\LegacyShelf\Resources\cover-index.json" `
  -InstalledOnly
```

The script only writes metadata and hashes. It does not copy cover images into the repository.

## Import priority

1. User override (right-click → **Change artwork…**).
2. `cover.jpg` / `folder.jpg` / `poster.jpg` / `boxart.png` / `artwork.*` in the game folder.
3. Exact cache match.
4. Optional one-time catalog fetch.
5. `icon.ico` from the game folder.
6. The executable's associated icon.
7. Generated placeholder.

## Normalization

- Maximum 800 px on the long edge.
- Covers: JPEG quality 92.
- Images with alpha / icons: PNG.
- The UI decodes a smaller thumbnail for the grid; the full file is kept for future use.
- Files are stored in `%APPDATA%\LegacyGamesLauncher\artwork\` and keyed by the stable game id.

## Legal

Cover art is copyrighted by the game publishers. It is imported from the user's own installation for
personal display. Do not commit artwork to this repository.
