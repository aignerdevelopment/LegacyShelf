using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LegacyShelf.Services
{
    public static class FolderScanner
    {
        private static readonly string[] SkipDirectoryNames =
        {
            "_CommonRedist", "CommonRedist", "MonoBleedingEdge", "DirectX", "DotNet", "vcredist",
            "Redist", "Support", "Docs", "Documentation", "Manual", "Sound", "Music", "Movies",
            "Cache", "logs", "Crashpad", "Engine", "bin", "Plugins", "Resources", "SaveData",
            "Profile", "Saves", "Screenshots", "__Installer", "UnityCrashHandler"
        };

        private static readonly string[] ExeBlocklist =
        {
            "unins", "uninstall", "unitycrashhandler", "crashpad_handler", "crashhandler",
            "vc_redist", "vcredist", "dxsetup", "directx", "dotnet", "python", "ffmpeg",
            "setup", "installer", "launcher_installer", "update", "updater"
        };

        public static List<FolderGame> Scan(IEnumerable<string> roots, int maxDepth = 2)
        {
            var results = new List<FolderGame>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawRoot in roots ?? Enumerable.Empty<string>())
            {
                var root = PathUtil.Normalize(rawRoot);
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                foreach (var dir in SafeDirectories(root))
                {
                    if (ShouldSkip(dir)) continue;

                    string exe;
                    if (TryFindExe(dir, out exe))
                    {
                        Add(results, seen, dir, exe);
                        continue;
                    }

                    if (maxDepth <= 1) continue;

                    foreach (var child in SafeDirectories(dir))
                    {
                        if (ShouldSkip(child)) continue;
                        if (TryFindExe(child, out exe)) Add(results, seen, child, exe);
                    }
                }
            }

            Log.Info("Folder scan: found " + results.Count + " candidate game folders.");
            return results;
        }

        public static int CountGames(string root)
        {
            return Scan(new[] { root }).Count;
        }

        public static bool TryFindExe(string directory, out string exePath)
        {
            exePath = null;
            if (!Directory.Exists(directory)) return false;

            List<FileInfo> exes;
            try
            {
                exes = Directory.GetFiles(directory, "*.exe")
                    .Select(p => new FileInfo(p))
                    .Where(f => f.Exists && f.Length > 0)
                    .Where(f => !IsBlocked(f.Name))
                    .ToList();
            }
            catch
            {
                return false;
            }

            if (exes.Count == 0) return false;

            var folder = Path.GetFileName(directory);
            var best = exes.FirstOrDefault(f => PathUtil.LooksLikeGameName(folder, f.Name));
            if (best == null) best = exes.OrderByDescending(f => f.Length).First();

            exePath = best.FullName;
            return true;
        }

        public static bool IsBlocked(string exeName)
        {
            var name = (exeName ?? string.Empty).ToLowerInvariant();
            foreach (var blocked in ExeBlocklist)
            {
                if (name.Contains(blocked)) return true;
            }
            return false;
        }

        private static void Add(List<FolderGame> results, HashSet<string> seen, string dir, string exe)
        {
            var key = PathUtil.Key(exe);
            if (!seen.Add(key)) return;

            results.Add(new FolderGame
            {
                InstallDir = PathUtil.Normalize(dir),
                FolderName = Path.GetFileName(dir),
                ExePath = exe
            });
        }

        private static IEnumerable<string> SafeDirectories(string root)
        {
            try
            {
                var dirs = Directory.GetDirectories(root);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                return dirs;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not enumerate '" + root + "': " + ex.Message);
                return Enumerable.Empty<string>();
            }
        }

        private static bool ShouldSkip(string dir)
        {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(name)) return true;
            if (name.StartsWith(".", StringComparison.Ordinal)) return true;
            if (name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase)) return true;

            foreach (var skip in SkipDirectoryNames)
            {
                if (string.Equals(name, skip, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public sealed class FolderGame
        {
            public string InstallDir { get; set; }
            public string FolderName { get; set; }
            public string ExePath { get; set; }
        }
    }
}
