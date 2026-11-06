using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using LegacyShelf.Models;

namespace LegacyShelf.Services
{
    /// <summary>
    /// Reads only the public catalog/settings information from the original launcher's app-state.json.
    /// Account, token, profile and review data are intentionally not represented in the DTOs and are
    /// never written to LegacyShelf's own files or log.
    /// </summary>
    public static class LauncherDataReader
    {
        public static string DetectDefaultLauncherDir()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "legacy-games-launcher");
            return Directory.Exists(dir) ? dir : null;
        }

        public static LauncherData Read(string launcherDir)
        {
            var result = new LauncherData();
            if (string.IsNullOrWhiteSpace(launcherDir)) return result;

            var appState = Path.Combine(launcherDir, "app-state.json");
            if (!File.Exists(appState))
            {
                Log.Warn("No app-state.json found in launcher data directory: " + launcherDir);
                return result;
            }

            try
            {
                AppStateDto dto;
                var settings = new DataContractJsonSerializerSettings
                {
                    MaxItemsInObjectGraph = int.MaxValue,
                    UseSimpleDictionaryFormat = true
                };
                var serializer = new DataContractJsonSerializer(typeof(AppStateDto), settings);

                using (var stream = File.OpenRead(appState))
                {
                    dto = (AppStateDto)serializer.ReadObject(stream);
                }

                if (dto == null) return result;

                if (dto.Settings != null && dto.Settings.GameLibraryPath != null)
                {
                    foreach (var path in dto.Settings.GameLibraryPath)
                    {
                        if (!string.IsNullOrWhiteSpace(path)) result.GameLibraryPaths.Add(path);
                    }
                }

                if (dto.SiteData != null && dto.SiteData.Catalog != null)
                {
                    foreach (var product in dto.SiteData.Catalog)
                    {
                        if (product == null || product.Games == null) continue;
                        foreach (var game in product.Games)
                        {
                            if (game == null || string.IsNullOrWhiteSpace(game.InstallerUuid)) continue;

                            var info = new CatalogGameInfo
                            {
                                Uuid = game.InstallerUuid.Trim(),
                                Name = Clean(game.GameName),
                                Description = Clean(game.GameDescription),
                                CoverUrl = Clean(game.GameCoverArt),
                                InstalledSize = Clean(game.GameInstalledSize)
                            };

                            CatalogGameInfo existing;
                            if (result.Games.TryGetValue(info.Uuid, out existing))
                            {
                                if (Score(info) > Score(existing)) result.Games[info.Uuid] = info;
                            }
                            else
                            {
                                result.Games[info.Uuid] = info;
                            }
                        }
                    }
                }

                Log.Info(string.Format(
                    "Read launcher catalog: {0} unique game entries, {1} library path(s).",
                    result.Games.Count,
                    result.GameLibraryPaths.Count));
            }
            catch (Exception ex)
            {
                Log.Error("Could not parse app-state.json. Continuing without catalog metadata.", ex);
            }

            return result;
        }

        private static int Score(CatalogGameInfo info)
        {
            var score = 0;
            if (!string.IsNullOrWhiteSpace(info.CoverUrl)) score += 4;
            if (!string.IsNullOrWhiteSpace(info.Name)) score += 2;
            if (!string.IsNullOrWhiteSpace(info.Description)) score += 1;
            if (!string.IsNullOrWhiteSpace(info.InstalledSize)) score += 1;
            return score;
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public sealed class LauncherData
        {
            public List<string> GameLibraryPaths { get; private set; }
            public Dictionary<string, CatalogGameInfo> Games { get; private set; }

            public LauncherData()
            {
                GameLibraryPaths = new List<string>();
                Games = new Dictionary<string, CatalogGameInfo>(StringComparer.OrdinalIgnoreCase);
            }
        }

#pragma warning disable 0649 // Fields are assigned by DataContractJsonSerializer.

        [DataContract]
        private sealed class AppStateDto
        {
            [DataMember(Name = "settings")]
            public SettingsDto Settings;

            [DataMember(Name = "siteData")]
            public SiteDataDto SiteData;
        }

        [DataContract]
        private sealed class SettingsDto
        {
            [DataMember(Name = "gameLibraryPath")]
            public string[] GameLibraryPath;
        }

        [DataContract]
        private sealed class SiteDataDto
        {
            [DataMember(Name = "catalog")]
            public CatalogProductDto[] Catalog;
        }

        [DataContract]
        private sealed class CatalogProductDto
        {
            [DataMember(Name = "games")]
            public CatalogGameDto[] Games;
        }

        [DataContract]
        private sealed class CatalogGameDto
        {
            [DataMember(Name = "installer_uuid")]
            public string InstallerUuid;

            [DataMember(Name = "game_name")]
            public string GameName;

            [DataMember(Name = "game_description")]
            public string GameDescription;

            [DataMember(Name = "game_coverart")]
            public string GameCoverArt;

            [DataMember(Name = "game_installed_size")]
            public string GameInstalledSize;
        }

#pragma warning restore 0649
    }
}
