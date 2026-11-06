# Scanning notes

## Sources

1. **Registry (authoritative):** `HKCU\Software\Legacy Games\*`
   - `InstDir`, `ProductName`, `GameExe`, `InstallerUUID`
2. **Folder scan:** each configured install root, depth 2.
3. **Catalog metadata:** `app-state.json` → `siteData.catalog[].games[]`
   - `game_name`, `game_description`, `game_coverart`, `installer_uuid`, `game_installed_size`
4. **Artwork cache:** `Cache\Cache_Data\f_*` (raw image bodies).

## Transport manifest

If `data\portable-games.json` exists, it is merged **before** the registry and folder scan. Each
entry supplies the installer UUID, display name, relative game folder, and executable name. This is
how a transport pack works on a new PC without the original registry or installer.

## Merge rules

- Normalize every path and deduplicate case-insensitively.
- Registry entries outside the selected roots are ignored.
- If the registry `GameExe` is missing at `InstDir`, search immediate subdirectories. This repairs
  the observed *Lilas Sky Ark* entry, where `InstDir` is the parent rather than the game folder.
- Folder-only games are added unless an entry with the same executable already exists.
- The registry wins for name/UUID; the catalog wins for display name and description.
- A game with no executable stays visible with a **Missing files** badge.

## Executable choice

1. Registry `GameExe` (strip a leading `.\`).
2. An executable whose normalized name matches the folder name.
3. The largest plausible executable.

Excluded helper names include `unins*`, `uninstall*`, `UnityCrashHandler*`, `crashpad_handler*`,
`vc_redist*`, `DXSETUP*`, `dotnet*`, and similar.

## Stable identity

- `uuid:<installer_uuid>` when available.
- Otherwise `path:<sha1(normalized exe path)>`.

Artwork overrides, hidden flags, and playtime use this id.

## Privacy

The catalog reader uses explicit DTOs, not a generic dictionary. Account, token, profile, and
review data are not represented and therefore cannot leak into LegacyShelf's files or log.
