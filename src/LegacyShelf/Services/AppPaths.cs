using System;
using System.IO;
using System.Reflection;

namespace LegacyShelf.Services
{
    public static class AppPaths
    {
        private static readonly object Gate = new object();
        private static string _baseDir;
        private static bool _portable;
        private static string _storageMessage;

        public static string ExeDir
        {
            get
            {
                return AppDomain.CurrentDomain.BaseDirectory
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        public static string ExePath
        {
            get
            {
                try
                {
                    var assembly = Assembly.GetEntryAssembly();
                    if (assembly != null && !string.IsNullOrWhiteSpace(assembly.Location))
                    {
                        return assembly.Location;
                    }
                }
                catch
                {
                    // Fall through to the conventional name.
                }

                return Path.Combine(ExeDir, "LegacyShelf.exe");
            }
        }

        public static string PortableDataDir
        {
            get { return Path.Combine(ExeDir, "data"); }
        }

        public static string AppDataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "LegacyGamesLauncher");
            }
        }

        /// <summary>
        /// Portable by default: data lives in &lt;exe&gt;\data when that folder is writable.
        /// If it is not writable (for example the app was placed in Program Files), fall back
        /// to %APPDATA%\LegacyGamesLauncher.
        /// </summary>
        public static string BaseDir
        {
            get
            {
                if (_baseDir != null) return _baseDir;

                lock (Gate)
                {
                    if (_baseDir != null) return _baseDir;

                    var portableDir = PortableDataDir;
                    string reason;
                    if (TryPrepareWritable(portableDir, out reason))
                    {
                        _portable = true;
                        _baseDir = portableDir;
                        TryMigrateFromAppData(portableDir);
                    }
                    else
                    {
                        _portable = false;
                        _baseDir = AppDataDir;
                        try
                        {
                            Directory.CreateDirectory(_baseDir);
                        }
                        catch
                        {
                            // The app will report write failures later.
                        }

                        _storageMessage = "Portable data folder is not writable (" + reason +
                                          "). Using " + _baseDir + " instead.";
                    }

                    return _baseDir;
                }
            }
        }

        public static bool IsPortable
        {
            get
            {
                // Force initialization.
                var unused = BaseDir;
                return _portable;
            }
        }

        public static string StorageDescription
        {
            get
            {
                var unused = BaseDir;
                return (_portable ? "portable" : "appdata") + " · " + _baseDir;
            }
        }

        public static string StorageMessage
        {
            get
            {
                var unused = BaseDir;
                return _storageMessage;
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(BaseDir, "config.json"); }
        }

        public static string LibraryPath
        {
            get { return Path.Combine(BaseDir, "library.json"); }
        }

        public static string ArtworkManifestPath
        {
            get { return Path.Combine(BaseDir, "artwork.json"); }
        }

        public static string ArtworkDir
        {
            get
            {
                var dir = Path.Combine(BaseDir, "artwork");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string LogsDir
        {
            get
            {
                var dir = Path.Combine(BaseDir, "logs");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string LogPath
        {
            get { return Path.Combine(LogsDir, "app.log"); }
        }

        public static string LocalCoverIndexPath
        {
            get { return Path.Combine(BaseDir, "cover-index.local.json"); }
        }

        public static string PortableGamesPath
        {
            get { return Path.Combine(BaseDir, "portable-games.json"); }
        }

        /// <summary>
        /// Resolves a path from config. Relative paths are relative to the executable directory
        /// (which is how transport packs store "Games" and "LauncherData").
        /// </summary>
        public static string ResolveStoredPath(string storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath)) return null;

            var value = Environment.ExpandEnvironmentVariables(storedPath.Trim().Trim('"'));
            if (Path.IsPathRooted(value)) return PathUtil.Normalize(value);

            var fromExe = PathUtil.Normalize(Path.Combine(ExeDir, value));
            if (Directory.Exists(fromExe) || File.Exists(fromExe)) return fromExe;

            var fromData = PathUtil.Normalize(Path.Combine(BaseDir, value));
            if (Directory.Exists(fromData) || File.Exists(fromData)) return fromData;

            return fromExe;
        }

        /// <summary>
        /// Stores paths under the executable directory as relative paths so the whole folder
        /// can be moved to another PC or drive.
        /// </summary>
        public static string ToStoredPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            var normalized = PathUtil.Normalize(path);
            if (PathUtil.IsUnder(normalized, ExeDir))
            {
                var relative = GetRelativePath(ExeDir, normalized);
                return string.IsNullOrWhiteSpace(relative) ? "." : relative;
            }

            return normalized;
        }

        private static bool TryPrepareWritable(string directory, out string reason)
        {
            reason = null;
            try
            {
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
                using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.WriteByte(0);
                }
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static void TryMigrateFromAppData(string portableDir)
        {
            try
            {
                var legacyDir = AppDataDir;
                if (PathUtil.SamePath(legacyDir, portableDir)) return;
                if (File.Exists(Path.Combine(portableDir, "config.json"))) return;
                if (!File.Exists(Path.Combine(legacyDir, "config.json"))) return;

                Directory.CreateDirectory(portableDir);

                foreach (var name in new[]
                {
                    "config.json", "library.json", "artwork.json",
                    "cover-index.local.json", "portable-games.json"
                })
                {
                    var source = Path.Combine(legacyDir, name);
                    if (File.Exists(source)) File.Copy(source, Path.Combine(portableDir, name), true);
                }

                var sourceArtwork = Path.Combine(legacyDir, "artwork");
                if (Directory.Exists(sourceArtwork))
                {
                    CopyDirectory(sourceArtwork, Path.Combine(portableDir, "artwork"));
                }

                _storageMessage = "Migrated existing LegacyShelf data from " + legacyDir + " to " + portableDir + ".";
            }
            catch (Exception ex)
            {
                _storageMessage = "Could not migrate existing data to the portable folder: " + ex.Message;
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            }
            foreach (var dir in Directory.GetDirectories(source))
            {
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
            }
        }

        private static string GetRelativePath(string root, string path)
        {
            try
            {
                var rootUri = new Uri(AppendSeparator(root));
                var pathUri = new Uri(AppendSeparator(path));
                if (!string.Equals(rootUri.Scheme, pathUri.Scheme, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }

                var relative = Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString());
                return relative.Replace('/', '\\').TrimEnd('\\');
            }
            catch
            {
                return path;
            }
        }

        private static string AppendSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }
    }
}
