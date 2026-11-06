using System;
using System.IO;
using System.Linq;
using LegacyShelf.Models;

namespace LegacyShelf.Services
{
    public static class ConfigStore
    {
        public static AppConfig Load()
        {
            AppConfig config = null;
            try
            {
                config = JsonUtil.Load<AppConfig>(AppPaths.ConfigPath);
            }
            catch (Exception ex)
            {
                Log.Error("Could not read config; using defaults.", ex);
                TryBackupBadConfig();
            }

            if (config == null) config = AppConfig.CreateDefault();
            config.EnsureDefaults();

            // Stored paths may be relative to the executable (portable/transport packs).
            for (var i = 0; i < config.Sources.InstallRoots.Count; i++)
            {
                config.Sources.InstallRoots[i] =
                    AppPaths.ResolveStoredPath(config.Sources.InstallRoots[i]);
            }
            config.Sources.LauncherDataDir =
                AppPaths.ResolveStoredPath(config.Sources.LauncherDataDir);

            return config;
        }

        public static void Save(AppConfig config)
        {
            if (config == null) return;
            config.EnsureDefaults();

            try
            {
                // Store paths under the app folder relativistically so the whole folder can move.
                var save = Clone(config);
                save.Sources.InstallRoots = config.Sources.InstallRoots
                    .Select(AppPaths.ToStoredPath)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();
                save.Sources.LauncherDataDir = AppPaths.ToStoredPath(config.Sources.LauncherDataDir);

                JsonUtil.Save(AppPaths.ConfigPath, save, pretty: true);
            }
            catch (Exception ex)
            {
                Log.Error("Could not save config.", ex);
            }
        }

        public static AppConfig Clone(AppConfig config)
        {
            if (config == null) return AppConfig.CreateDefault();
            return JsonUtil.Clone(config);
        }

        private static void TryBackupBadConfig()
        {
            try
            {
                if (!File.Exists(AppPaths.ConfigPath)) return;
                var backup = Path.Combine(
                    AppPaths.BaseDir,
                    "config.bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                File.Copy(AppPaths.ConfigPath, backup, true);
                Log.Warn("Backed up unreadable config to " + backup);
            }
            catch
            {
                // Ignore.
            }
        }
    }
}
