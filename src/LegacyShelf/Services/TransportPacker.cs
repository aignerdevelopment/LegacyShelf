using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using LegacyShelf.Models;

namespace LegacyShelf.Services
{
    /// <summary>
    /// Creates a transport set: one launcher zip plus one zip per selected game.
    /// The launcher zip extracts with LegacyShelf.exe at its root; each game zip extracts
    /// with exactly one game folder at its root.
    /// </summary>
    public sealed class TransportPacker
    {
        private const int BufferSize = 1024 * 1024;
        private const string LauncherZipName = "LegacyShelf-Launcher.zip";
        private const string GameMetadataName = ".legacyshelf-game.json";

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

            var folderNames = MakeUniqueFolderNames(games);
            var zipNames = folderNames.Select(n => n + ".zip").ToList();
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
            var stage = Path.Combine(Path.GetTempPath(), "LegacyShelf-Launcher-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(destination);
                created = true;

                _state.Phase = "Building launcher";
                Directory.CreateDirectory(stage);
                CopyFile(AppPaths.ExePath, Path.Combine(stage, "LegacyShelf.exe"));
                CopyIfExists(AppPaths.ExePath + ".config", Path.Combine(stage, "LegacyShelf.exe.config"));
                CopyIfExists(Path.Combine(AppPaths.ExeDir, "README.md"), Path.Combine(stage, "README.md"));
                CopyIfExists(Path.Combine(AppPaths.ExeDir, "LICENSE"), Path.Combine(stage, "LICENSE"));
                File.WriteAllText(Path.Combine(stage, "LegacyShelf.portable"), string.Empty, new UTF8Encoding(false));

                var dataDir = Path.Combine(stage, "data");
                Directory.CreateDirectory(dataDir);
                CopyArtwork(dataDir, artwork);
                WriteArtworkManifest(dataDir, games, artwork);
                CopyIfExists(AppPaths.LocalCoverIndexPath, Path.Combine(dataDir, "cover-index.local.json"));

                var includedLauncherData = false;
                if (!string.IsNullOrWhiteSpace(launcherSource) && Directory.Exists(launcherSource))
                {
                    _state.Phase = "Copying launcher data";
                    includedLauncherData = CopyLauncherData(stage, launcherSource, games);
                }

                _state.Phase = "Writing launcher settings";
                WritePackConfig(dataDir, config, includedLauncherData);
                WritePortableGames(dataDir, games, folderNames, zipNames);
                WriteTransportReadme(stage);

                _state.Phase = "Creating launcher zip";
                ZipFile.CreateFromDirectory(
                    stage,
                    Path.Combine(destination, LauncherZipName),
                    CompressionLevel.Optimal,
                    false);

                _state.Phase = "Zipping games";
                for (var i = 0; i < games.Count; i++)
                {
                    _token.ThrowIfCancellationRequested();
                    CreateGameZip(
                        games[i],
                        folderNames[i],
                        zipNames[i],
                        Path.Combine(destination, zipNames[i]));
                }

                _state.Phase = "Finishing";
                WriteTransportReadme(destination);
                var outputBytes = new DirectoryInfo(destination)
                    .GetFiles("*.zip")
                    .Sum(f => f.Length);

                WriteTransportManifest(
                    destination,
                    games,
                    folderNames,
                    zipNames,
                    includedLauncherData,
                    outputBytes);

                _state.BytesTotal = Math.Max(_state.BytesTotal, _state.BytesDone);
                Report(true);

                Log.Info(string.Format(
                    "Transport pack created: {0} game zip(s), launcher zip, {1:0.0} MB output, launcher data: {2}",
                    games.Count,
                    outputBytes / (1024.0 * 1024.0),
                    includedLauncherData));

                return new PackResult
                {
                    Destination = destination,
                    GameCount = games.Count,
                    TotalBytes = outputBytes,
                    IncludedLauncherData = includedLauncherData,
                    LauncherZip = LauncherZipName
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
                        // Leave the partial folder for inspection.
                    }
                }
                throw;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stage)) Directory.Delete(stage, true);
                }
                catch
                {
                    // Ignore temporary staging cleanup failures.
                }
            }
        }

        private void CreateGameZip(
            Game game,
            string folderName,
            string zipName,
            string zipPath)
        {
            _state.CurrentFile = zipName;
            Report(true);

            using (var stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                AddDirectoryEntries(archive, game.InstallDir, game.InstallDir, folderName);
                AddGameFiles(archive, game.InstallDir, game.InstallDir, folderName);

                var metadata = new PortableGameEntry
                {
                    Id = game.Id,
                    Uuid = game.Uuid,
                    Name = game.Name,
                    FolderName = folderName,
                    RelativeDir = folderName,
                    ExeName = string.IsNullOrWhiteSpace(game.ExePath) ? game.ExeName : Path.GetFileName(game.ExePath),
                    ZipName = zipName
                };

                var metadataEntry = archive.CreateEntry(
                    folderName + "/" + GameMetadataName,
                    CompressionLevel.Fastest);
                using (var writer = new StreamWriter(metadataEntry.Open(), new UTF8Encoding(false)))
                {
                    writer.Write(JsonUtil.Serialize(metadata, pretty: true));
                }
            }

            _state.FilesDone++;
            Report(true);
        }

        private void AddDirectoryEntries(ZipArchive archive, string root, string current, string folderName)
        {
            foreach (var dir in Directory.GetDirectories(current))
            {
                _token.ThrowIfCancellationRequested();
                if (IsReparsePoint(dir)) continue;

                var relative = RelativePath(root, dir).Replace('\\', '/');
                if (!HasFiles(dir))
                {
                    archive.CreateEntry(folderName + "/" + relative + "/", CompressionLevel.NoCompression);
                }
                AddDirectoryEntries(archive, root, dir, folderName);
            }
        }

        private void AddGameFiles(ZipArchive archive, string root, string current, string folderName)
        {
            foreach (var file in Directory.GetFiles(current))
            {
                _token.ThrowIfCancellationRequested();
                if (IsReparsePoint(file)) continue;

                var relative = RelativePath(root, file);
                if (string.Equals(relative, GameMetadataName, StringComparison.OrdinalIgnoreCase)) continue;

                var entryName = folderName + "/" + relative.Replace('\\', '/');
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                try
                {
                    entry.LastWriteTime = File.GetLastWriteTime(file);
                }
                catch
                {
                    // Some timestamps cannot be represented in zip.
                }

                _state.CurrentFile = file;
                Report(false);

                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize))
                using (var output = entry.Open())
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
            }

            foreach (var dir in Directory.GetDirectories(current))
            {
                _token.ThrowIfCancellationRequested();
                if (IsReparsePoint(dir)) continue;
                AddGameFiles(archive, root, dir, folderName);
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

            ArtworkManifest live;
            try
            {
                live = JsonUtil.Load<ArtworkManifest>(AppPaths.ArtworkManifestPath);
            }
            catch
            {
                return;
            }
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
                        // Ignore unreadable files.
                    }
                }
            }

            return info;
        }

        private bool CopyLauncherData(string stage, string launcherDir, IList<Game> games)
        {
            var info = GetLauncherDataInfo(launcherDir);
            if (!info.HasAppState && !info.HasCache) return false;

            var target = Path.Combine(stage, "LauncherData");
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
                    sanitized.Add(new Dictionary<string, object>
                    {
                        { "installer_uuid", game.Uuid },
                        { "game_name", game.Name },
                        { "game_description", game.Description },
                        { "game_coverart", game.CoverUrl },
                        { "game_installed_size", game.InstalledSize }
                    });
                }

                var document = new
                {
                    settings = new { gameLibraryPath = new string[0] },
                    siteData = new { catalog = new[] { new { games = sanitized } } }
                };

                var path = Path.Combine(target, "app-state.json");
                JsonUtil.Save(path, document, pretty: true);
                AddWrittenBytes(path);
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
            packConfig.Sources.InstallRoots = new List<string> { "." };
            packConfig.Sources.LauncherDataDir = includeLauncherData ? "LauncherData" : null;
            packConfig.Sources.FetchMissingCovers = false;
            packConfig.Ui.Fullscreen = false;
            packConfig.Games.ExeOverrides = new Dictionary<string, string>();

            var path = Path.Combine(dataDir, "config.json");
            JsonUtil.Save(path, packConfig, pretty: true);
            AddWrittenBytes(path);
        }

        private void WritePortableGames(
            string dataDir,
            IList<Game> games,
            IList<string> folderNames,
            IList<string> zipNames)
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
                    RelativeDir = folderNames[i],
                    ExeName = string.IsNullOrWhiteSpace(game.ExePath) ? game.ExeName : Path.GetFileName(game.ExePath),
                    ZipName = zipNames[i]
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
            IList<string> zipNames,
            bool includeLauncherData,
            long outputBytes)
        {
            var manifest = new TransportManifest
            {
                Version = 1,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                LegacyShelfVersion = GetVersion(),
                GameCount = games.Count,
                TotalBytes = outputBytes,
                IncludeLauncherData = includeLauncherData,
                LauncherZip = LauncherZipName
            };

            for (var i = 0; i < games.Count; i++)
            {
                manifest.Games.Add(new TransportGameEntry
                {
                    Name = games[i].Name,
                    FolderName = folderNames[i],
                    Uuid = games[i].Uuid,
                    RelativeDir = folderNames[i],
                    ExeName = string.IsNullOrWhiteSpace(games[i].ExePath) ? games[i].ExeName : Path.GetFileName(games[i].ExePath),
                    ZipName = zipNames[i]
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
                "This pack contains LegacyShelf-Launcher.zip and one zip per game.\r\n" +
                "No installer is required. Windows 10/11 includes .NET Framework 4.8.\r\n\r\n" +
                "1. Create a folder for your games, for example:  D:\\LegacyGames\r\n" +
                "2. Extract LegacyShelf-Launcher.zip into that folder.\r\n" +
                "   LegacyShelf.exe is at the root of the launcher zip.\r\n" +
                "3. Extract any game zips into the same folder.\r\n" +
                "   Each game zip contains its game folder at the zip root, e.g.\r\n" +
                "     100 Doors Escape from School\\100 Doors Escape From School.exe\r\n" +
                "4. Run LegacyShelf.exe.\r\n\r\n" +
                "Games that are not extracted are hidden from the shelf but kept in the launcher's metadata.\r\n" +
                "You can extract them later. If the zips are copied next to LegacyShelf.exe, the launcher's\r\n" +
                "Unzip button can extract them for you and will offer to delete the zips afterwards.\r\n\r\n" +
                "Saves stored inside the game folders are included. Saves stored in Documents or AppData\r\n" +
                "are not automatically copied. Keep the pack private: it contains game files and\r\n" +
                "copyrighted cover art. LegacyShelf is unofficial and not affiliated with Legacy Games.\r\n";

            var path = Path.Combine(destination, "README-TRANSPORT.txt");
            File.WriteAllText(path, readme, new UTF8Encoding(false));
            AddWrittenBytes(path);
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
                // Optional.
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
            long total = 5 * 1024 * 1024;
            foreach (var game in games) total += DirectorySize(game.InstallDir);
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

        private static bool HasFiles(string path)
        {
            try
            {
                return Directory.EnumerateFiles(path).Any();
            }
            catch
            {
                return false;
            }
        }

        private static string RelativePath(string root, string path)
        {
            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return path.Substring(normalizedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
