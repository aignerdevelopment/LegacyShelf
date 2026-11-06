using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace LegacyShelf.Services
{
    public static class RegistryScanner
    {
        private const string RootKey = @"Software\Legacy Games";

        public static List<RegistryGame> ReadInstalled()
        {
            var games = new List<RegistryGame>();

            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(RootKey))
                {
                    if (root == null)
                    {
                        Log.Info("No HKCU\\Software\\Legacy Games registry key found.");
                        return games;
                    }

                    foreach (var subKeyName in root.GetSubKeyNames())
                    {
                        try
                        {
                            using (var key = root.OpenSubKey(subKeyName))
                            {
                                if (key == null) continue;

                                var installDir = ReadString(key, "InstDir");
                                var productName = ReadString(key, "ProductName");
                                var exeName = NormalizeExeName(ReadString(key, "GameExe"));
                                var uuid = ReadString(key, "InstallerUUID");

                                if (string.IsNullOrWhiteSpace(installDir) && !string.IsNullOrWhiteSpace(productName))
                                {
                                    installDir = null;
                                }

                                games.Add(new RegistryGame
                                {
                                    KeyName = subKeyName,
                                    InstallDir = PathUtil.Normalize(installDir),
                                    ProductName = string.IsNullOrWhiteSpace(productName) ? subKeyName : productName,
                                    ExeName = exeName,
                                    Uuid = uuid
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("Could not read registry entry '" + subKeyName + "': " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not enumerate the Legacy Games registry key.", ex);
            }

            Log.Info("Registry: found " + games.Count + " installed game entries.");
            return games;
        }

        public static string DetectCommonRoot()
        {
            var dirs = new List<string>();
            foreach (var game in ReadInstalled())
            {
                if (!string.IsNullOrWhiteSpace(game.InstallDir)) dirs.Add(game.InstallDir);
            }
            return PathUtil.CommonParent(dirs);
        }

        /// <summary>
        /// Returns a repaired install directory when a registry entry points at the parent folder
        /// instead of the game folder (observed with "Lilas Sky Ark").
        /// </summary>
        public static string RepairInstallDir(string installDir, string exeName)
        {
            installDir = PathUtil.Normalize(installDir);
            if (string.IsNullOrWhiteSpace(installDir)) return null;

            if (!string.IsNullOrWhiteSpace(exeName))
            {
                var direct = Path.Combine(installDir, exeName);
                if (File.Exists(direct)) return installDir;
            }

            if (!Directory.Exists(installDir)) return installDir;

            try
            {
                foreach (var child in Directory.GetDirectories(installDir))
                {
                    var candidate = Path.Combine(child, exeName);
                    if (!string.IsNullOrWhiteSpace(exeName) && File.Exists(candidate))
                    {
                        Log.Info("Repaired registry install path: " + installDir + " -> " + child);
                        return child;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not inspect install path '" + installDir + "': " + ex.Message);
            }

            return installDir;
        }

        private static string ReadString(RegistryKey key, string name)
        {
            try
            {
                var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                return value == null ? null : value.ToString().Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeExeName(string exeName)
        {
            if (string.IsNullOrWhiteSpace(exeName)) return null;
            var value = exeName.Trim().Trim('"');
            if (value.StartsWith(@".\", StringComparison.Ordinal)) value = value.Substring(2);
            if (value.StartsWith("./", StringComparison.Ordinal)) value = value.Substring(2);
            return value;
        }

        public sealed class RegistryGame
        {
            public string KeyName { get; set; }
            public string InstallDir { get; set; }
            public string ProductName { get; set; }
            public string ExeName { get; set; }
            public string Uuid { get; set; }
        }
    }
}
