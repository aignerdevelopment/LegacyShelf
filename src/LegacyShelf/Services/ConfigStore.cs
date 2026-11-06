using System;
using System.IO;
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
            return config;
        }

        public static void Save(AppConfig config)
        {
            if (config == null) return;
            config.EnsureDefaults();
            try
            {
                JsonUtil.Save(AppPaths.ConfigPath, config, pretty: true);
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
