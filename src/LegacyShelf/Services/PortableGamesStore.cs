using System;
using LegacyShelf.Models;

namespace LegacyShelf.Services
{
    /// <summary>
    /// A transport pack writes portable-games.json with relative game folders and installer UUIDs.
    /// This lets a shelf work on a new PC without the original registry or installer.
    /// </summary>
    public static class PortableGamesStore
    {
        public static PortableGamesManifest Load()
        {
            try
            {
                var manifest = JsonUtil.Load<PortableGamesManifest>(AppPaths.PortableGamesPath);
                if (manifest == null) return null;
                if (manifest.Games == null) manifest.Games = new System.Collections.Generic.List<PortableGameEntry>();
                return manifest;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read portable-games.json: " + ex.Message);
                return null;
            }
        }
    }
}
