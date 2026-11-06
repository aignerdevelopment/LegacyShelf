using System;
using System.IO;

namespace LegacyShelf.Services
{
    public static class AppPaths
    {
        private static string _baseDir;

        public static string BaseDir
        {
            get
            {
                if (_baseDir != null) return _baseDir;

                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                var portableFlag = Path.Combine(exeDir, "LegacyShelf.portable");
                if (File.Exists(portableFlag))
                {
                    _baseDir = Path.Combine(exeDir, "data");
                }
                else
                {
                    _baseDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "LegacyGamesLauncher");
                }

                Directory.CreateDirectory(_baseDir);
                return _baseDir;
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

        public static string ExeDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }
    }
}
