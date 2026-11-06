using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LegacyShelf.Models;

namespace LegacyShelf.Services
{
    public sealed class CoverIndexService
    {
        private readonly Dictionary<string, CoverIndexEntry> _byHash;
        private readonly Dictionary<string, CoverIndexEntry> _byUuid;

        public CoverIndexService()
        {
            _byHash = new Dictionary<string, CoverIndexEntry>(StringComparer.OrdinalIgnoreCase);
            _byUuid = new Dictionary<string, CoverIndexEntry>(StringComparer.OrdinalIgnoreCase);

            Merge(LoadEmbedded());
            Merge(LoadFile(Path.Combine(AppPaths.ExeDir, "data", "cover-index.json")));
            Merge(LoadFile(AppPaths.LocalCoverIndexPath));
        }

        public int Count
        {
            get { return _byUuid.Count; }
        }

        public CoverIndexEntry FindByHash(string sha256)
        {
            if (string.IsNullOrWhiteSpace(sha256)) return null;
            CoverIndexEntry entry;
            return _byHash.TryGetValue(sha256, out entry) ? entry : null;
        }

        public CoverIndexEntry FindByUuid(string uuid)
        {
            if (string.IsNullOrWhiteSpace(uuid)) return null;
            CoverIndexEntry entry;
            return _byUuid.TryGetValue(uuid, out entry) ? entry : null;
        }

        public void Remember(CoverIndexEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Uuid)) return;
            _byUuid[entry.Uuid] = entry;
            if (!string.IsNullOrWhiteSpace(entry.Sha256)) _byHash[entry.Sha256] = entry;

            try
            {
                var local = LoadFile(AppPaths.LocalCoverIndexPath) ?? new CoverIndex
                {
                    Version = 1,
                    Source = "learned locally"
                };

                var existing = local.Covers.FindIndex(c =>
                    string.Equals(c.Uuid, entry.Uuid, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0) local.Covers[existing] = entry;
                else local.Covers.Add(entry);

                JsonUtil.Save(AppPaths.LocalCoverIndexPath, local, pretty: true);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not save the local cover index: " + ex.Message);
            }
        }

        private static CoverIndex LoadEmbedded()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("LegacyShelf.cover-index.json"))
                {
                    if (stream == null) return null;
                    return JsonUtil.Load<CoverIndex>(stream);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the embedded cover index: " + ex.Message);
                return null;
            }
        }

        private static CoverIndex LoadFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return JsonUtil.Load<CoverIndex>(path);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read cover index '" + path + "': " + ex.Message);
                return null;
            }
        }

        private void Merge(CoverIndex index)
        {
            if (index == null || index.Covers == null) return;
            foreach (var entry in index.Covers)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Uuid)) continue;
                _byUuid[entry.Uuid] = entry;
                if (!string.IsNullOrWhiteSpace(entry.Sha256)) _byHash[entry.Sha256] = entry;
            }
        }
    }
}
