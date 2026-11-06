using System;
using System.IO;
using System.Linq;
using LegacyShelf.Models;

namespace LegacyShelf.Services
{
    /// <summary>
    /// A transport pack writes portable-games.json with relative game folders and installer UUIDs.
    /// This lets a shelf work on a new PC without the original registry or installer.
    /// </summary>
    public static class PortableGamesStore
    {
        private static readonly object Gate = new object();

        public static PortableGamesManifest Load()
        {
            try
            {
                var manifest = JsonUtil.Load<PortableGamesManifest>(AppPaths.PortableGamesPath);
                if (manifest == null) return null;
                if (manifest.Games == null)
                {
                    manifest.Games = new System.Collections.Generic.List<PortableGameEntry>();
                }
                return manifest;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read portable-games.json: " + ex.Message);
                return null;
            }
        }

        public static void Save(PortableGamesManifest manifest)
        {
            if (manifest == null) return;
            if (manifest.Games == null) manifest.Games = new System.Collections.Generic.List<PortableGameEntry>();
            try
            {
                JsonUtil.Save(AppPaths.PortableGamesPath, manifest, pretty: true);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not save portable-games.json: " + ex.Message);
            }
        }

        public static PortableGameEntry ReadMetadataFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                return JsonUtil.Load<PortableGameEntry>(path);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read game metadata '" + path + "': " + ex.Message);
                return null;
            }
        }

        public static void Remember(PortableGameEntry entry)
        {
            if (entry == null) return;
            lock (Gate)
            {
                var manifest = Load() ?? new PortableGamesManifest();
                var existing = manifest.Games.FirstOrDefault(g =>
                    (!string.IsNullOrWhiteSpace(entry.ZipName) &&
                     string.Equals(g.ZipName, entry.ZipName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(entry.RelativeDir) &&
                     string.Equals(g.RelativeDir, entry.RelativeDir, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(entry.Id) &&
                     string.Equals(g.Id, entry.Id, StringComparison.OrdinalIgnoreCase)));

                if (existing == null)
                {
                    manifest.Games.Add(entry);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(entry.Name)) existing.Name = entry.Name;
                    if (!string.IsNullOrWhiteSpace(entry.Uuid)) existing.Uuid = entry.Uuid;
                    if (!string.IsNullOrWhiteSpace(entry.FolderName)) existing.FolderName = entry.FolderName;
                    if (!string.IsNullOrWhiteSpace(entry.RelativeDir)) existing.RelativeDir = entry.RelativeDir;
                    if (!string.IsNullOrWhiteSpace(entry.ExeName)) existing.ExeName = entry.ExeName;
                    if (!string.IsNullOrWhiteSpace(entry.ZipName)) existing.ZipName = entry.ZipName;
                    if (!string.IsNullOrWhiteSpace(entry.Id)) existing.Id = entry.Id;
                }

                Save(manifest);
            }
        }
    }
}
