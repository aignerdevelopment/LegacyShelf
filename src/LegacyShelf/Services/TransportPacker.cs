using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using LegacyShelf.Models;

namespace LegacyShelf.Services
{
    /// <summary>
    /// Creates a self-contained folder that can be copied to another PC:
    /// LegacyShelf.exe + data (settings/artwork) + Games\... + optional sanitized launcher data.
    /// </summary>
    public sealed class TransportPacker
    {
        private const int BufferSize = 1024 * 1024;

        private readonly IProgress<PackProgress> _progress;
        private readonly CancellationToken _token;
        private readonly PackProgress _state;
        private DateTime _lastReport;

        public TransportPacker(IProgress<PackProgress> progress, CancellationToken token)
        {
            _progress = progress;
            _token = token;
            _state = new PackProgress();
        }

        public PackResult Pack(
            AppConfig config,
            IList<Game> selectedGames,
            string destination,
            bool includeLauncherData)
        {
            var games = (selectedGames ?? new List<Game>())
                .Where(g => g != null)
                .GroupBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (games.Count == 0) throw new InvalidOperationException("Select at least one game to pack.");
            if (config == null) throw new ArgumentNullException("config");

            destination = PathUtil.Normalize(destination);
            if (string.IsNullOrWhiteSpace(destination)) throw new InvalidOperationException("Choose a destination folder.");

            ValidateDestination(destination, config, games);

            foreach (var game in games)
            {
                if (string.IsNullOrWhiteSpace(game.InstallDir) || !Directory.Exists(game.InstallDir))
                {
                    throw new InvalidOperationException("Game folder not found:\n" + game.InstallDir);
                }
            }

            var artwork = GetArtworkForGames(games);
            var launcherSource = includeLauncherData ? config.Sources.LauncherDataDir : null;
            var launcherInfo = GetLauncherDataInfo(launcherSource);
            var total = EstimateBytes(games, artwork, launcherInfo);
            _state.BytesTotal = Math.Max(1, total);
            _state.BytesDone = 0;
            _state.FilesDone = 0;
            _state.Phase = "Preparing";
            _state.CurrentFile = string.Empty;
            Report(true);

            CheckFreeSpace(destination, _state.BytesTotal);

            var created = false;
            try
            {
                Directory.CreateDirectory(destination);
                created = true;

                _state.Phase = "Copying LegacyShelf";
                CopyFile(AppPaths.ExePath, Path.Combine(destination, "LegacyShelf.exe"));
                CopyIfExists(AppPaths.ExePath + ".config", Path.Combine(destination, "LegacyShelf.exe.config"));
                CopyIfExists(Path.Combine(AppPaths.ExeDir, "README.md"), Path.Combine(destination, "README.md"));
                CopyIfExists(Path.Combine(AppPaths.ExeDir, "LICENSE"), Path.Combine(destination, "LICENSE"));
                File.WriteAllText(Path.Combine(destination, "LegacyShelf.portable"), string.Empty, new UTF8Encoding(false));

                var dataDir = Path.Combine(destination, "data");
                Directory.CreateDirectory(dataDir);
                CopyArtwork(dataDir, artwork);
                WriteArtworkManifest(dataDir, games, artwork);
                CopyIfExists(AppPaths.LocalCoverIndexPath, Path.Combine(dataDir, "cover-index.local.json"));

                _state.Phase = "Copying games";
                var folderNames = MakeUniqueFolderNames(games);
                for (var i = 0; i < games.Count; i++)
                {
                    _token.ThrowIfCancellationRequested();
                    var game = games[i];
                    var gameDest = Path.Combine(destination, "Games", folderNames[i]);
                    CopyDirectory(game.InstallDir, gameDest);
                }

                var includedLauncherData = false;
                if (!string.IsNullOrWhiteSpace(launcherSource) && Directory.Exists(launcherSource))
                {
                    _state.Phase = "Copying launcher data";
                    includedLauncherData = CopyLauncherData(destination, launcherSource, games);
                }

                _state.Phase = "Writing settings";
                WritePackConfig(dataDir, config, includedLauncherData);
                WritePortableGames(dataDir, games, folderNames);
                WriteTransportReadme(destination);

                _state.Phase = "Finishing";
                WriteTransportManifest(destination, games, folderNames, includedLauncherData);
                _state.BytesTotal = Math.Max(_state.BytesTotal, _state.BytesDone);
                Report(true);

                Log.Info(string.Format(
                    "Transport pack created: {0} games, {1:0.0} MB, launcher data: {2}",
                    games.Count,
                    _state.BytesDone / (1024.0 * 1024.0),
                    includedLauncherData));

                return new PackResult
                {
                    Destination = destination,
                    GameCount = games.Count,
                    TotalBytes = _state.BytesDone,
                    IncludedLauncherData = includedLauncherData
                };
            }
            catch
            {
                if (created)
                {
                    try
                    {
                        Directory.Delete(destination, true);
                    }
                    catch
                    {
                        // Leave the partial folder for the user to inspect/delete.
                    }
                }
                throw;
            }
        }

        private void ValidateDestination(string destination, AppConfig config, IList<Game> games)
        {
            if (File.Exists(destination))
            {
                throw new InvalidOperationException("The destination is a file. Choose a new folder path.");
            }

            if (Directory.Exists(destination))
            {
                throw new InvalidOperationException(
                    "The destination already exists. Choose a new folder so the pack cannot mix with other data.");
            }

            if (PathUtil.IsUnder(destination, AppPaths.BaseDir))
            {
                throw new InvalidOperationException("Choose a destination outside LegacyShelf's data folder.");
            }

            foreach (var root in config.Sources.InstallRoots)
            {
                if (!string.IsNullOrWhiteSpace(root) && PathUtil.IsUnder(destination, root))
                {
                    throw new InvalidOperationException(
                        "Choose a destination outside your game library:\n" + root);
                }
            }

            foreach (var game in games)
            {
                if (!string.IsNullOrWhiteSpace(game.InstallDir) && PathUtil.IsUnder(destination, game.InstallDir))
                {
                    throw new InvalidOperationException(
                        "The destination cannot be inside a game folder:\n" + game.InstallDir);
                }
            }
        }

        private List<ArtworkToCopy> GetArtworkForGames(IList<Game> games)
        {
            var result = new List<ArtworkToCopy>();
            ArtworkManifest manifest;
            try
            {
                manifest = JsonUtil.Load<ArtworkManifest>(AppPaths.ArtworkManifestPath);
            }
            catch
            {
                manifest = null;
            }

            if (manifest == null || manifest.Overrides == null) return result;

            foreach (var game in games)
            {
                ArtworkEntry entry;
                if (!manifest.Overrides.TryGetValue(game.Id, out entry) || entry == null ||
                    string.IsNullOrWhiteSpace(entry.File))
                {
                    continue;
                }

                var source = Path.Combine(AppPaths.ArtworkDir, entry.File);
                if (!File.Exists(source)) continue;

                result.Add(new ArtworkToCopy
                {
                    Entry = entry,
                    SourcePath = source
                });
            }

            return result;
        }

        private void CopyArtwork(string dataDir, IList<ArtworkToCopy> artwork)
        {
            if (artwork == null || artwork.Count == 0) return;

            var artworkDir = Path.Combine(dataDir, "artwork");
            Directory.CreateDirectory(artworkDir);

            foreach (var item in artwork)
            {
                _token.ThrowIfCancellationRequested();
                CopyFile(item.SourcePath, Path.Combine(artworkDir, item.Entry.File));
            }
        }

        private void WriteArtworkManifest(string dataDir, IList<Game> games, IList<ArtworkToCopy> artwork)
        {
            if (artwork == null || artwork.Count == 0) return;

            var live = JsonUtil.Load<ArtworkManifest>(AppPaths.ArtworkManifestPath);
            if (live == null || live.Overrides == null) return;

            var filtered = new ArtworkManifest();
            foreach (var game in games)
            {
                ArtworkEntry entry;
                if (live.Overrides.TryGetValue(game.Id, out entry) && entry != null &&
                    !string.IsNullOrWhiteSpace(entry.File) &&
                    File.Exists(Path.Combine(dataDir, "artwork", entry.File)))
                {
                    filtered.Overrides[game.Id] = entry;
                }
            }

            if (filtered.Overrides.Count > 0)
            {
                JsonUtil.Save(Path.Combine(dataDir, "artwork.json"), filtered, pretty: true);
            }
        }

        private LauncherDataInfo GetLauncherDataInfo(string launcherDir)
        {
            var info = new LauncherDataInfo();
            if (string.IsNullOrWhiteSpace(launcherDir) || !Directory.Exists(launcherDir)) return info;

            var appState = Path.Combine(launcherDir, "app-state.json");
            if (File.Exists(appState))
            {
                info.HasAppState = true;
                info.Bytes += new FileInfo(appState).Length;
            }

            var cacheDir = Path.Combine(launcherDir, "Cache", "Cache_Data");
            if (Directory.Exists(cacheDir))
            {
                foreach (var file in Directory.GetFiles(cacheDir, "f_*"))
                {
                    try
                    {
                        info.HasCache = true;
                        info.Bytes += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Ignore unreadable files during estimation.
                    }
                }
            }

            return info;
        }

        private bool CopyLauncherData(string destination, string launcherDir, IList<Game> games)
        {
            var info = GetLauncherDataInfo(launcherDir);
            if (!info.HasAppState && !info.HasCache) return false;

            var target = Path.Combine(destination, "LauncherData");
            Directory.CreateDirectory(target);

            var appState = Path.Combine(launcherDir, "app-state.json");
            if (File.Exists(appState))
            {
                var launcherData = LauncherDataReader.Read(launcherDir);
                var uuids = new HashSet<string>(
                    games.Where(g => !string.IsNullOrWhiteSpace(g.Uuid)).Select(g => g.Uuid),
                    StringComparer.OrdinalIgnoreCase);

                var sanitized = new List<Dictionary<string, object>>();
                foreach (var game in launcherData.Games.Values)
                {
                    if (uuids.Count > 0 && !uuids.Contains(game.Uuid ?? string.Empty)) continue;
                    var item = new Dictionary<string, object>
                    {
                        { "installer_uuid", game.Uuid },
                        { "game_name", game.Name },
                        { "game_description", game.Description },
                        { "game_coverart", game.CoverUrl },
                        { "game_installed_size", game.InstalledSize }
                    };
                    sanitized.Add(item);
                }

                var document = new
                {
                    settings = new { gameLibraryPath = new string[0] },
                    siteData = new { catalog = new[] { new { games = sanitized } } }
                };

                JsonUtil.Save(Path.Combine(target, "app-state.json"), document, pretty: true);
                AddWrittenBytes(Path.Combine(target, "app-state.json"));
            }

            var cacheSource = Path.Combine(launcherDir, "Cache", "Cache_Data");
            if (Directory.Exists(cacheSource))
            {
                var cacheTarget = Path.Combine(target, "Cache", "Cache_Data");
                Directory.CreateDirectory(cacheTarget);
                foreach (var file in Directory.GetFiles(cacheSource, "f_*"))
                {
                    _token.ThrowIfCancellationRequested();
                    try
                    {
                        CopyFile(file, Path.Combine(cacheTarget, Path.GetFileName(file)));
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Skipping launcher cache file '" + file + "': " + ex.Message);
                    }
                }
            }

            return true;
        }

        private void WritePackConfig(string dataDir, AppConfig config, bool includeLauncherData)
        {
            var packConfig = ConfigStore.Clone(config);
            packConfig.Sources.InstallRoots = new List<string> { "Games" };
            packConfig.Sources.LauncherDataDir = includeLauncherData ? "LauncherData" : null;
            packConfig.Sources.FetchMissingCovers = false;
            packConfig.Ui.Fullscreen = false;
            packConfig.Games.ExeOverrides = new Dictionary<string, string>();

            JsonUtil.Save(Path.Combine(dataDir, "config.json"), packConfig, pretty: true);
            AddWrittenBytes(Path.Combine(dataDir, "config.json"));
        }

        private void WritePortableGames(string dataDir, IList<Game> games, IList<string> folderNames)
        {
            var manifest = new PortableGamesManifest
            {
                Version = 1,
                CreatedAt = DateTime.UtcNow.ToString("o")
            };

            for (var i = 0; i < games.Count; i++)
            {
                var game = games[i];
                manifest.Games.Add(new PortableGameEntry
                {
                    Id = game.Id,
                    Uuid = game.Uuid,
                    Name = game.Name,
                    FolderName = folderNames[i],
                    RelativeDir = Path.Combine("Games", folderNames[i]),
                    ExeName = string.IsNullOrWhiteSpace(game.ExePath)
                        ? game.ExeName
                        : Path.GetFileName(game.ExePath)
                });
            }

            var path = Path.Combine(dataDir, "portable-games.json");
            JsonUtil.Save(path, manifest, pretty: true);
            AddWrittenBytes(path);
        }

        private void WriteTransportManifest(
            string destination,
            IList<Game> games,
            IList<string> folderNames,
            bool includeLauncherData)
        {
            var manifest = new TransportManifest
            {
                Version = 1,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                LegacyShelfVersion = GetVersion(),
                GameCount = games.Count,
                TotalBytes = _state.BytesDone,
                IncludeLauncherData = includeLauncherData
            };

            for (var i = 0; i < games.Count; i++)
            {
                manifest.Games.Add(new TransportGameEntry
                {
                    Name = games[i].Name,
                    FolderName = folderNames[i],
                    Uuid = games[i].Uuid,
                    RelativeDir = Path.Combine("Games", folderNames[i]),
                    ExeName = string.IsNullOrWhiteSpace(games[i].ExePath)
                        ? games[i].ExeName
                        : Path.GetFileName(games[i].ExePath)
                });
            }

            var path = Path.Combine(destination, "transport-manifest.json");
            JsonUtil.Save(path, manifest, pretty: true);
            AddWrittenBytes(path);
        }

        private void WriteTransportReadme(string destination)
        {
            var readme =
                "LegacyShelf transport pack\r\n" +
                "==========================\r\n\r\n" +
                "This folder is a complete, portable LegacyShelf set. No installer is required.\r\n\r\n" +
                "1. Copy the whole folder to the new PC (same folder structure).\r\n" +
                "2. Run LegacyShelf.exe. Windows 10/11 includes the required .NET Framework 4.8.\r\n" +
                "3. Keep Games\\, data\\, and LegacyShelf.exe together. Paths are relative to this folder.\r\n" +
                "4. Your selected game folders are in Games\\. LegacyShelf settings and cover art are in data\\.\r\n" +
                "5. Original launcher metadata and cover cache may be in LauncherData\\ (sanitized; no account data).\r\n" +
                "6. Saves stored inside the game folders are included. Saves stored elsewhere (Documents, AppData) are not.\r\n" +
                "7. Do not publish this folder: it contains game files and copyrighted cover art.\r\n\r\n" +
                "LegacyShelf is unofficial and is not affiliated with Legacy Games.\r\n";

            var path = Path.Combine(destination, "README-TRANSPORT.txt");
            File.WriteAllText(path, readme, new UTF8Encoding(false));
            AddWrittenBytes(path);
        }

        private void CopyDirectory(string source, string destination)
        {
            _token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destination);

            foreach (var dir in Directory.GetDirectories(source))
            {
                _token.ThrowIfCancellationRequested();
                if (IsReparsePoint(dir))
                {
                    Log.Warn("Skipping reparse-point directory: " + dir);
                    continue;
                }
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
            }

            foreach (var file in Directory.GetFiles(source))
            {
                _token.ThrowIfCancellationRequested();
                if (IsReparsePoint(file))
                {
                    Log.Warn("Skipping reparse-point file: " + file);
                    continue;
                }
                CopyFile(file, Path.Combine(destination, Path.GetFileName(file)));
            }
        }

        private void CopyIfExists(string source, string destination)
        {
            if (File.Exists(source)) CopyFile(source, destination);
        }

        private void CopyFile(string source, string destination)
        {
            _token.ThrowIfCancellationRequested();
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

            _state.CurrentFile = destination;
            Report(false);

            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    _token.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    _state.BytesDone += read;
                    Report(false);
                }
            }

            try
            {
                File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
            }
            catch
            {
                // Timestamps are a nicety.
            }

            _state.FilesDone++;
        }

        private void AddWrittenBytes(string path)
        {
            try
            {
                _state.BytesDone += new FileInfo(path).Length;
            }
            catch
            {
                // Ignore.
            }
            _state.FilesDone++;
            Report(true);
        }

        private void Report(bool force)
        {
            var now = DateTime.UtcNow;
            if (!force && (now - _lastReport).TotalMilliseconds < 180) return;
            _lastReport = now;

            if (_progress == null) return;
            _progress.Report(new PackProgress
            {
                BytesDone = _state.BytesDone,
                BytesTotal = _state.BytesTotal,
                FilesDone = _state.FilesDone,
                FilesTotal = _state.FilesTotal,
                Phase = _state.Phase,
                CurrentFile = _state.CurrentFile
            });
        }

        private static long EstimateBytes(
            IList<Game> games,
            IList<ArtworkToCopy> artwork,
            LauncherDataInfo launcherInfo)
        {
            long total = 5 * 1024 * 1024; // exe, config, manifests, readme margin
            foreach (var game in games)
            {
                total += DirectorySize(game.InstallDir);
            }
            foreach (var item in artwork)
            {
                try { total += new FileInfo(item.SourcePath).Length; }
                catch { /* ignore */ }
            }
            if (launcherInfo != null) total += launcherInfo.Bytes;
            return total;
        }

        private static long DirectorySize(string path)
        {
            long total = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (IsReparsePoint(file)) continue;
                        total += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Ignore unreadable files.
                    }
                }
            }
            catch
            {
                // Let the actual copy report the error.
            }
            return total;
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static void CheckFreeSpace(string destination, long needed)
        {
            try
            {
                var root = Path.GetPathRoot(destination);
                if (string.IsNullOrWhiteSpace(root)) return;

                var drive = new DriveInfo(root);
                if (!drive.IsReady) return;

                var required = needed + 50L * 1024 * 1024;
                if (drive.AvailableFreeSpace < required)
                {
                    throw new InvalidOperationException(string.Format(
                        "Not enough free space on {0}.\n\nNeeded: {1:0.0} GB\nAvailable: {2:0.0} GB",
                        root,
                        required / (1024.0 * 1024.0 * 1024.0),
                        drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0)));
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // UNC paths or unusual volumes: let the copy fail with a normal IO error.
            }
        }

        private static List<string> MakeUniqueFolderNames(IList<Game> games)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new List<string>();
            foreach (var game in games)
            {
                var name = string.IsNullOrWhiteSpace(game.FolderName)
                    ? PathUtil.SafeFileStem(game.Name)
                    : game.FolderName;
                name = name.Trim().TrimEnd('.');
                if (string.IsNullOrWhiteSpace(name)) name = "Game";

                var candidate = name;
                var counter = 2;
                while (!used.Add(candidate))
                {
                    candidate = name + "-" + counter;
                    counter++;
                }
                names.Add(candidate);
            }
            return names;
        }

        private static string GetVersion()
        {
            try
            {
                return Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            }
            catch
            {
                return "0.1.0";
            }
        }

        private sealed class ArtworkToCopy
        {
            public ArtworkEntry Entry { get; set; }
            public string SourcePath { get; set; }
        }

        private sealed class LauncherDataInfo
        {
            public bool HasAppState { get; set; }
            public bool HasCache { get; set; }
            public long Bytes { get; set; }
        }
    }
}
