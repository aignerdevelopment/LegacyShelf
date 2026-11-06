# Changelog

## 1.0.0

First stable release.

- Portable-by-default data storage in `<exe>\data`, with `%APPDATA%` fallback and one-time migration.
- Fullscreen cover shelf, live search, keyboard navigation, and double-click/Enter launch.
- Automatic game discovery from the Legacy Games registry and folder scan, including the
  *Lilas Sky Ark* install-path repair.
- Cover-art import from the original launcher cache by exact SHA-256 match, with icon and placeholder
  fallbacks.
- Rescan dialog for installed directories, launcher data, and the four requested source actions.
- Individual game zips plus a launcher zip for transport to another PC.
- Startup detection and unpacking of game zips, with optional zip cleanup after all games are unpacked.
- Missing/unpacked games stay in metadata, are hidden by default, and can be shown in Library sources.
- Themed “This won’t fit” dialog with needed/available/shortfall values; pack/unpack starts directly
  when enough space is available.
- 0BSD license and a Windows GitHub Actions build/release pipeline.
