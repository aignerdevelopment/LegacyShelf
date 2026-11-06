using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using LegacyShelf.Models;

namespace LegacyShelf.Services
{
    public sealed class ScanOptions
    {
        public List<string> Roots { get; set; }
        public string LauncherDataDir { get; set; }
        public bool FetchMissingCovers { get; set; }
        public bool ImportArtwork { get; set; }
        public Dictionary<string, string> ExeOverrides { get; set; }

        public ScanOptions()
        {
            Roots = new List<string>();
            ImportArtwork = true;
            ExeOverrides = new Dictionary<string, string>();
        }
    }

    public sealed class LibraryScanner
    {
        public ScanResult Scan(
            ScanOptions options,
            IProgress<ScanProgress> progress,
            CancellationToken token)
        {
            if (options == null) options = new ScanOptions();

            var roots = NormalizeRoots(options.Roots);
            var result = new ScanResult
            {
                ScannedAt = DateTime.UtcNow.ToString("o"),
                Roots = roots
            };

            if (roots.Count == 0)
            {
                Log.Warn("Scan requested with no valid install roots.");
                return result;
            }

            Report(progress, 3, "Reading installed games…");
            var registryGames = RegistryScanner.ReadInstalled();

            Report(progress, 8, "Scanning game folders…");
            var folderGames = FolderScanner.Scan(roots);

            Report(progress, 14, "Reading launcher metadata…");
            var launcherData = LauncherDataReader.Read(options.LauncherDataDir);

            token.ThrowIfCancellationRequested();
            Report(progress, 20, "Matching games…");

            var games = new List<Game>();
            var byExe = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
            var byFolder = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in registryGames)
            {
                token.ThrowIfCancellationRequested();

                var installDir = PathUtil.Normalize(entry.InstallDir);
                var exeName = entry.ExeName;

                if (string.IsNullOrWhiteSpace(installDir))
                {
                    installDir = FindRegistryFolder(roots, entry);
                    if (string.IsNullOrWhiteSpace(installDir)) continue;
                }
                else if (!IsUnderAny(installDir, roots))
                {
                    var repairedRoot = FindRegistryFolder(roots, entry);
                    if (string.IsNullOrWhiteSpace(repairedRoot))
                    {
                        Log.Info("Skipping registry game outside selected roots: " + entry.ProductName);
                        continue;
                    }
                    installDir = repairedRoot;
                }

                var repaired = RegistryScanner.RepairInstallDir(installDir, exeName);
                if (!string.IsNullOrWhiteSpace(repaired)) installDir = repaired;

                var exePath = string.IsNullOrWhiteSpace(exeName) ? null : Path.Combine(installDir, exeName);
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    string found;
                    if (FolderScanner.TryFindExe(installDir, out found)) exePath = found;
                }

                if (string.IsNullOrWhiteSpace(exePath))
                {
                    exePath = string.IsNullOrWhiteSpace(exeName) ? null : Path.Combine(installDir, exeName);
                }

                var game = CreateGame(
                    entry.Uuid,
                    entry.ProductName,
                    installDir,
                    exePath,
                    exeName);

                AddGame(games, byExe, byFolder, game);
            }

            foreach (var folderGame in folderGames)
            {
                token.ThrowIfCancellationRequested();
                var key = PathUtil.Key(folderGame.ExePath);
                if (byExe.ContainsKey(key)) continue;

                var game = CreateGame(
                    null,
                    folderGame.FolderName,
                    folderGame.InstallDir,
                    folderGame.ExePath,
                    Path.GetFileName(folderGame.ExePath));

                AddGame(games, byExe, byFolder, game);
            }

            // Explicit per-game executable overrides win over discovery.
            if (options.ExeOverrides != null)
            {
                foreach (var game in games)
                {
                    string overridePath;
                    if (options.ExeOverrides.TryGetValue(game.Id, out overridePath) &&
                        !string.IsNullOrWhiteSpace(overridePath))
                    {
                        game.ExePath = overridePath;
                        game.ExeName = Path.GetFileName(overridePath);
                        game.Status = File.Exists(overridePath) ? "ok" : "missing";
                    }
                }
            }

            // Catalog metadata supplies proper names, descriptions and cover URLs.
            foreach (var game in games)
            {
                CatalogGameInfo info;
                if (!string.IsNullOrWhiteSpace(game.Uuid) &&
                    launcherData.Games.TryGetValue(game.Uuid, out info))
                {
                    game.CatalogName = info.Name;
                    if (!string.IsNullOrWhiteSpace(info.Name)) game.Name = info.Name;
                    game.Description = info.Description;
                    game.InstalledSize = info.InstalledSize;
                    game.CoverUrl = info.CoverUrl;
                }
            }

            games.Sort((a, b) => string.Compare(
                string.IsNullOrWhiteSpace(a.SortName) ? a.FolderName : a.SortName,
                string.IsNullOrWhiteSpace(b.SortName) ? b.FolderName : b.SortName,
                StringComparison.CurrentCultureIgnoreCase));

            result.Games = games;
            Report(progress, 28, "Preparing artwork…");

            if (options.ImportArtwork)
            {
                try
                {
                    var importer = new ArtworkImporter(new CoverIndexService());
                    var artwork = importer.Import(
                        games,
                        options.LauncherDataDir,
                        launcherData.Games,
                        options.FetchMissingCovers,
                        progress,
                        token);

                    result.Artwork = new ArtworkImportResultSummary
                    {
                        FromCache = artwork.FromCache,
                        Fetched = artwork.Fetched,
                        Icons = artwork.Icons,
                        Placeholders = artwork.Placeholders
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Error("Artwork import failed; continuing with placeholders.", ex);
                }
            }

            Report(progress, 100, "Ready");
            Log.Info(string.Format(
                "Scan complete: {0} games, {1} ok, {2} missing, roots: {3}",
                games.Count,
                games.Count(g => !g.IsMissing),
                games.Count(g => g.IsMissing),
                string.Join("; ", roots)));

            return result;
        }

        private static Game CreateGame(string uuid, string name, string installDir, string exePath, string exeName)
        {
            var folderName = string.IsNullOrWhiteSpace(installDir) ? name : Path.GetFileName(installDir);
            var resolvedExe = !string.IsNullOrWhiteSpace(exePath)
                ? exePath
                : (string.IsNullOrWhiteSpace(exeName) ? null : Path.Combine(installDir ?? string.Empty, exeName));

            var id = !string.IsNullOrWhiteSpace(uuid)
                ? "uuid:" + uuid.Trim()
                : "path:" + ShortHash(PathUtil.Key(resolvedExe ?? installDir ?? name));

            return new Game
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? folderName : name.Trim(),
                FolderName = folderName,
                InstallDir = installDir,
                ExeName = string.IsNullOrWhiteSpace(resolvedExe) ? exeName : Path.GetFileName(resolvedExe),
                ExePath = resolvedExe,
                Uuid = string.IsNullOrWhiteSpace(uuid) ? null : uuid.Trim(),
                Status = !string.IsNullOrWhiteSpace(resolvedExe) && File.Exists(resolvedExe) ? "ok" : "missing",
                PlaySeconds = 0
            };
        }

        private static void AddGame(
            List<Game> games,
            Dictionary<string, Game> byExe,
            Dictionary<string, Game> byFolder,
            Game game)
        {
            var exeKey = string.IsNullOrWhiteSpace(game.ExePath) ? null : PathUtil.Key(game.ExePath);
            if (!string.IsNullOrWhiteSpace(exeKey) && byExe.ContainsKey(exeKey)) return;

            var folderKey = PathUtil.Key(game.InstallDir);
            if (byFolder.ContainsKey(folderKey))
            {
                var existing = byFolder[folderKey];
                if (string.IsNullOrWhiteSpace(existing.Uuid) && !string.IsNullOrWhiteSpace(game.Uuid))
                {
                    existing.Uuid = game.Uuid;
                    existing.Id = game.Id;
                    existing.Name = game.Name;
                    existing.CatalogName = game.CatalogName;
                }
                if (string.IsNullOrWhiteSpace(existing.ExePath) && !string.IsNullOrWhiteSpace(game.ExePath))
                {
                    existing.ExePath = game.ExePath;
                    existing.ExeName = game.ExeName;
                    existing.Status = game.Status;
                }
                return;
            }

            games.Add(game);
            byFolder[folderKey] = game;
            if (!string.IsNullOrWhiteSpace(exeKey)) byExe[exeKey] = game;
        }

        private static string FindRegistryFolder(IEnumerable<string> roots, RegistryScanner.RegistryGame entry)
        {
            foreach (var root in roots)
            {
                try
                {
                    var direct = Path.Combine(root, entry.KeyName ?? string.Empty);
                    if (Directory.Exists(direct))
                    {
                        if (string.IsNullOrWhiteSpace(entry.ExeName) || File.Exists(Path.Combine(direct, entry.ExeName)))
                        {
                            return direct;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(entry.ExeName)) continue;

                    foreach (var dir in Directory.GetDirectories(root))
                    {
                        var candidate = Path.Combine(dir, entry.ExeName);
                        if (File.Exists(candidate)) return dir;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not search root '" + root + "': " + ex.Message);
                }
            }

            return null;
        }

        private static bool IsUnderAny(string path, IEnumerable<string> roots)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return roots.Any(root => PathUtil.IsUnder(path, root));
        }

        private static List<string> NormalizeRoots(IEnumerable<string> roots)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots ?? Enumerable.Empty<string>())
            {
                var normalized = PathUtil.Normalize(root);
                if (string.IsNullOrWhiteSpace(normalized) || !Directory.Exists(normalized)) continue;
                if (seen.Add(PathUtil.Key(normalized))) result.Add(normalized);
            }

            return result;
        }

        private static void Report(IProgress<ScanProgress> progress, int percent, string message)
        {
            if (progress == null) return;
            progress.Report(new ScanProgress(percent, message));
        }

        private static string ShortHash(string value)
        {
            using (var sha = SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var sb = new StringBuilder(16);
                for (var i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
