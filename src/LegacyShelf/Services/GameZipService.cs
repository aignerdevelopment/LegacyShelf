using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using LegacyShelf.Models;

namespace LegacyShelf.Services
{
    /// <summary>
    /// Finds game zips in the launcher folder, reads their embedded metadata, and unpacks them
    /// next to LegacyShelf.exe. Each zip contains one game folder at its root.
    /// </summary>
    public sealed class GameZipService
    {
        private const string MetadataFileName = ".legacyshelf-game.json";
        private const int BufferSize = 1024 * 1024;

        public List<GameZipCandidate> FindCandidates()
        {
            var result = new List<GameZipCandidate>();
            var manifest = PortableGamesStore.Load() ?? new PortableGamesManifest();
            if (manifest.Games == null) manifest.Games = new List<PortableGameEntry>();
            var changed = false;

            try
            {
                foreach (var zipPath in Directory.GetFiles(AppPaths.ExeDir, "*.zip", SearchOption.TopDirectoryOnly))
                {
                    var zipName = Path.GetFileName(zipPath);
                    if (IsLauncherZip(zipName)) continue;

                    var entry = manifest.Games.FirstOrDefault(g =>
                        !string.IsNullOrWhiteSpace(g.ZipName) &&
                        string.Equals(g.ZipName, zipName, StringComparison.OrdinalIgnoreCase));

                    if (entry == null)
                    {
                        entry = ReadEmbeddedEntry(zipPath);
                        if (entry != null)
                        {
                            entry.ZipName = zipName;
                            var existing = manifest.Games.FirstOrDefault(g =>
                                (!string.IsNullOrWhiteSpace(entry.RelativeDir) &&
                                 string.Equals(g.RelativeDir, entry.RelativeDir, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrWhiteSpace(entry.Id) &&
                                 string.Equals(g.Id, entry.Id, StringComparison.OrdinalIgnoreCase)));
                            if (existing != null)
                            {
                                existing.ZipName = zipName;
                                entry = existing;
                            }
                            else
                            {
                                manifest.Games.Add(entry);
                            }
                            changed = true;
                        }
                    }

                    if (entry == null || string.IsNullOrWhiteSpace(entry.RelativeDir)) continue;

                    var target = AppPaths.ResolveStoredPath(entry.RelativeDir);
                    if (string.IsNullOrWhiteSpace(target)) continue;

                    // Already unpacked (or at least the folder exists); leave it alone.
                    if (Directory.Exists(target)) continue;

                    result.Add(new GameZipCandidate
                    {
                        ZipPath = zipPath,
                        ZipName = zipName,
                        Entry = entry,
                        TargetDir = target,
                        ZipBytes = new FileInfo(zipPath).Length
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not inspect game zips: " + ex.Message);
            }

            if (changed) PortableGamesStore.Save(manifest);
            Log.Info("Game zips: " + result.Count + " candidate(s) not unpacked.");
            return result;
        }

        public static long GetExtractionBytes(IEnumerable<GameZipCandidate> candidates)
        {
            long total = 0;
            foreach (var candidate in candidates ?? Enumerable.Empty<GameZipCandidate>())
            {
                using (var archive = ZipFile.OpenRead(candidate.ZipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (!string.IsNullOrEmpty(entry.Name))
                        {
                            total += entry.Length;
                        }
                    }
                }
            }
            return total;
        }

        public void Extract(
            IList<GameZipCandidate> candidates,
            IProgress<PackProgress> progress,
            CancellationToken token)
        {
            if (candidates == null || candidates.Count == 0) return;

            var state = new PackProgress
            {
                BytesTotal = Math.Max(1, candidates.Sum(c => c.ZipBytes)),
                Phase = "Unpacking games"
            };
            var createdDirs = new List<string>();
            Report(progress, state, true);

            try
            {
                foreach (var candidate in candidates)
                {
                    token.ThrowIfCancellationRequested();
                    if (Directory.Exists(candidate.TargetDir)) continue;

                    var expectedRoot = Path.GetFileName(candidate.TargetDir.TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    state.Phase = "Unpacking " + candidate.ZipName;
                    state.CurrentFile = candidate.ZipName;
                    Report(progress, state, true);

                    var extractionRoot = Path.GetDirectoryName(candidate.TargetDir);
                    if (string.IsNullOrWhiteSpace(extractionRoot))
                    {
                        throw new InvalidOperationException("Invalid extraction target.");
                    }
                    createdDirs.Add(candidate.TargetDir);

                    using (var archive = ZipFile.OpenRead(candidate.ZipPath))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            token.ThrowIfCancellationRequested();
                            var name = (entry.FullName ?? string.Empty).Replace('\\', '/');

                            if (name.EndsWith("/", StringComparison.Ordinal))
                            {
                                var directory = SafeDestination(extractionRoot, name.TrimEnd('/'));
                                Directory.CreateDirectory(directory);
                                continue;
                            }

                            var segments = name.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                            if (segments.Length == 0) continue;
                            if (!string.Equals(segments[0], expectedRoot, StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidDataException(
                                    "The zip root does not match the expected game folder '" + expectedRoot + "'.");
                            }

                            var destination = SafeDestination(extractionRoot, name);
                            var parent = Path.GetDirectoryName(destination);
                            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                            if (File.Exists(destination))
                            {
                                throw new IOException("The destination file already exists: " + destination);
                            }

                            state.CurrentFile = destination;
                            using (var input = entry.Open())
                            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
                            {
                                var buffer = new byte[BufferSize];
                                int read;
                                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    token.ThrowIfCancellationRequested();
                                    output.Write(buffer, 0, read);
                                    state.BytesDone += read;
                                    Report(progress, state, false);
                                }
                            }

                            try
                            {
                                File.SetLastWriteTimeUtc(destination, entry.LastWriteTime.UtcDateTime);
                            }
                            catch
                            {
                                // Timestamps are optional.
                            }

                            state.FilesDone++;
                        }
                    }

                    if (!Directory.Exists(candidate.TargetDir))
                    {
                        throw new InvalidDataException(
                            "The zip did not create the expected folder: " + candidate.TargetDir);
                    }
                }

                state.BytesTotal = Math.Max(state.BytesTotal, state.BytesDone);
                Report(progress, state, true);
            }
            catch
            {
                foreach (var dir in createdDirs)
                {
                    try
                    {
                        if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Could not remove partial extraction '" + dir + "': " + ex.Message);
                    }
                }
                throw;
            }
        }

        public static void DeleteZips(IEnumerable<GameZipCandidate> candidates)
        {
            foreach (var candidate in candidates ?? Enumerable.Empty<GameZipCandidate>())
            {
                try
                {
                    if (File.Exists(candidate.ZipPath)) File.Delete(candidate.ZipPath);
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not delete zip '" + candidate.ZipPath + "': " + ex.Message);
                }
            }
        }

        private static PortableGameEntry ReadEmbeddedEntry(string zipPath)
        {
            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        var name = (entry.FullName ?? string.Empty).Replace('\\', '/');
                        if (!name.EndsWith("/" + MetadataFileName, StringComparison.OrdinalIgnoreCase)) continue;

                        var segments = name.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        if (segments.Length != 2) continue;

                        using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                        {
                            var metadata = JsonUtil.Deserialize<PortableGameEntry>(reader.ReadToEnd());
                            if (metadata == null) continue;

                            metadata.RelativeDir = segments[0];
                            if (string.IsNullOrWhiteSpace(metadata.FolderName)) metadata.FolderName = segments[0];
                            if (string.IsNullOrWhiteSpace(metadata.Name)) metadata.Name = metadata.FolderName;
                            if (string.IsNullOrWhiteSpace(metadata.Id) && !string.IsNullOrWhiteSpace(metadata.Uuid))
                            {
                                metadata.Id = "uuid:" + metadata.Uuid;
                            }
                            metadata.ZipName = Path.GetFileName(zipPath);
                            return metadata;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read metadata from '" + zipPath + "': " + ex.Message);
            }
            return null;
        }

        private static string SafeDestination(string extractionRoot, string entryName)
        {
            var normalized = (entryName ?? string.Empty).Replace('\\', '/');
            var segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(s => s == ".." || s == "."))
            {
                throw new InvalidDataException("Unsafe zip entry: " + entryName);
            }

            var relative = string.Join("\\", segments);
            var destination = Path.GetFullPath(Path.Combine(extractionRoot, relative));
            if (!PathUtil.IsUnder(destination, AppPaths.ExeDir))
            {
                throw new InvalidDataException("Zip entry escapes the launcher folder: " + entryName);
            }
            return destination;
        }

        private static bool IsLauncherZip(string name)
        {
            return name.StartsWith("LegacyShelf-Launcher", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("LegacyShelf-Portable", StringComparison.OrdinalIgnoreCase);
        }

        private static void Report(IProgress<PackProgress> progress, PackProgress state, bool force)
        {
            if (progress == null) return;
            progress.Report(new PackProgress
            {
                BytesDone = state.BytesDone,
                BytesTotal = state.BytesTotal,
                FilesDone = state.FilesDone,
                Phase = state.Phase,
                CurrentFile = state.CurrentFile
            });
        }
    }
}
