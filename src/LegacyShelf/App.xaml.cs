using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LegacyShelf.Models;
using LegacyShelf.Services;
using LegacyShelf.Views;

namespace LegacyShelf
{
    public partial class App : Application
    {
        public AppConfig Config { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Log.Init(AppPaths.LogPath);
            Log.Info("LegacyShelf " + GetVersion() + " starting.");

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            Config = ConfigStore.Load();

            var reportPath = GetArgument(e.Args, "--scan-report");
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                RunScanReport(e.Args, reportPath);
                return;
            }

            if (Config.Sources.InstallRoots.Count == 0)
            {
                // The welcome window is the first window; do not let WPF shut the app down
                // when it closes to make way for the main shelf.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var welcome = new WelcomeWindow(Config);
                if (welcome.ShowDialog() != true)
                {
                    Shutdown(0);
                    return;
                }

                Config = welcome.Config;
                Config.Sources.FetchMissingCovers = welcome.FetchMissingCovers;
                ConfigStore.Save(Config);

                var firstRun = new MainWindow(Config, welcome.FetchMissingCovers, true);
                MainWindow = firstRun;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                firstRun.Show();
                return;
            }

            var window = new MainWindow(Config, Config.Sources.FetchMissingCovers, false);
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
        }

        private void RunScanReport(string[] args, string reportPath)
        {
            try
            {
                var roots = GetArguments(args, "--install-root").ToList();
                if (roots.Count == 0) roots.AddRange(Config.Sources.InstallRoots);

                if (roots.Count == 0)
                {
                    var detected = LauncherDataReader.DetectDefaultLauncherDir();
                    var data = LauncherDataReader.Read(detected);
                    roots.AddRange(data.GameLibraryPaths);
                    if (roots.Count == 0)
                    {
                        var common = RegistryScanner.DetectCommonRoot();
                        if (!string.IsNullOrWhiteSpace(common)) roots.Add(common);
                    }
                }

                var launcherDir = GetArgument(args, "--launcher-dir");
                if (string.IsNullOrWhiteSpace(launcherDir)) launcherDir = Config.Sources.LauncherDataDir;
                if (string.IsNullOrWhiteSpace(launcherDir)) launcherDir = LauncherDataReader.DetectDefaultLauncherDir();

                var fetch = args.Any(a => string.Equals(a, "--fetch-covers", StringComparison.OrdinalIgnoreCase));
                var noArt = args.Any(a => string.Equals(a, "--no-art", StringComparison.OrdinalIgnoreCase));

                var scanner = new LibraryScanner();
                var progress = new Progress<ScanProgress>(p =>
                {
                    if (!string.IsNullOrWhiteSpace(p.Message)) Log.Info("Scan: " + p.Percent + "% " + p.Message);
                });

                var result = scanner.Scan(new ScanOptions
                {
                    Roots = roots,
                    LauncherDataDir = launcherDir,
                    FetchMissingCovers = fetch,
                    ImportArtwork = !noArt,
                    ExeOverrides = Config.Games.ExeOverrides
                }, progress, CancellationToken.None);

                var cache = new LibraryCache
                {
                    ScannedAt = result.ScannedAt,
                    Roots = result.Roots,
                    Games = result.Games
                };
                JsonUtil.Save(AppPaths.LibraryPath, cache, pretty: true);

                var report = new
                {
                    scannedAt = result.ScannedAt,
                    roots = result.Roots,
                    count = result.Games.Count,
                    missing = result.Games.Count(g => g.IsMissing),
                    artwork = new
                    {
                        fromCache = result.Artwork.FromCache,
                        fetched = result.Artwork.Fetched,
                        icons = result.Artwork.Icons,
                        placeholders = result.Artwork.Placeholders
                    },
                    games = result.Games.Select(g => new
                    {
                        g.Id,
                        g.Name,
                        g.FolderName,
                        g.InstallDir,
                        g.ExeName,
                        g.ExePath,
                        g.Uuid,
                        g.Status,
                        g.CoverPath,
                        g.ArtworkSource,
                        g.CoverUrl
                    }).ToList()
                };

                var dir = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(reportPath, JsonUtil.Serialize(report, pretty: true));
                Log.Info("Scan report written to " + reportPath);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Log.Error("Scan report failed.", ex);
                try
                {
                    File.WriteAllText(reportPath + ".error.txt", ex.ToString());
                }
                catch
                {
                    // Ignore.
                }
                Shutdown(1);
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("Unhandled UI exception.", e.Exception);
            MessageBox.Show(
                "LegacyShelf hit an unexpected error.\n\n" + e.Exception.Message +
                "\n\nDetails were written to:\n" + AppPaths.LogPath,
                "LegacyShelf",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            Log.Error("Unhandled exception.", ex);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Log.Error("Unobserved task exception.", e.Exception);
            e.SetObserved();
        }

        private static string GetArgument(string[] args, string name)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1];
                }

                var prefix = name + "=";
                if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i].Substring(prefix.Length);
                }
            }
            return null;
        }

        private static IEnumerable<string> GetArguments(string[] args, string name)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    yield return args[i + 1];
                }
                else
                {
                    var prefix = name + "=";
                    if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return args[i].Substring(prefix.Length);
                    }
                }
            }
        }

        private static string GetVersion()
        {
            try
            {
                return typeof(App).Assembly.GetName().Version.ToString(3);
            }
            catch
            {
                return "0.1.0";
            }
        }
    }
}
