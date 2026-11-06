using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LegacyShelf.Models;

namespace LegacyShelf.Services
{
    public sealed class GameExitEventArgs : EventArgs
    {
        public Game Game { get; set; }
        public int ExitCode { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public sealed class GameLauncher
    {
        private readonly object _gate = new object();
        private readonly Dictionary<int, RunningGame> _running = new Dictionary<int, RunningGame>();

        public event EventHandler<GameExitEventArgs> GameExited;

        public int RunningCount
        {
            get
            {
                lock (_gate) return _running.Count;
            }
        }

        public bool Launch(Game game, out string error)
        {
            error = null;
            if (game == null)
            {
                error = "No game selected.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(game.ExePath) || !File.Exists(game.ExePath))
            {
                error = "The game executable was not found:\n" +
                        (game.ExePath ?? "(no executable)") +
                        "\n\nUse “Locate executable…” from the right-click menu.";
                return false;
            }

            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = game.ExePath,
                    WorkingDirectory = Directory.Exists(game.InstallDir)
                        ? game.InstallDir
                        : Path.GetDirectoryName(game.ExePath),
                    UseShellExecute = true
                };

                var process = Process.Start(start);
                if (process == null)
                {
                    error = "Windows did not return a process for this game.";
                    return false;
                }

                var running = new RunningGame
                {
                    Game = game,
                    Process = process,
                    StartedUtc = DateTime.UtcNow
                };

                lock (_gate)
                {
                    _running[process.Id] = running;
                }

                try
                {
                    process.EnableRaisingEvents = true;
                    process.Exited += (sender, args) => OnProcessExited(running);
                }
                catch
                {
                    // If the process is already gone, the Exited event may not fire.
                    OnProcessExited(running);
                }

                Log.Info("Launched '" + game.Name + "' (PID " + SafePid(process) + ") from " + game.ExePath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("Could not launch '" + game.Name + "'.", ex);
                return false;
            }
        }

        public static void OpenFolder(Game game)
        {
            try
            {
                var dir = Directory.Exists(game.InstallDir)
                    ? game.InstallDir
                    : Path.GetDirectoryName(game.ExePath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "\"" + dir + "\"",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not open game folder: " + ex.Message);
            }
        }

        private void OnProcessExited(RunningGame running)
        {
            if (running == null) return;

            var exitCode = -1;
            try
            {
                exitCode = running.Process.ExitCode;
            }
            catch
            {
                // Ignore.
            }

            var duration = DateTime.UtcNow - running.StartedUtc;
            lock (_gate)
            {
                _running.Remove(running.Process.Id);
            }

            try
            {
                running.Process.Dispose();
            }
            catch
            {
                // Ignore.
            }

            Log.Info("Game exited: '" + running.Game.Name + "', code " + exitCode + ", " + duration.TotalSeconds.ToString("0") + "s");

            var handler = GameExited;
            if (handler != null)
            {
                handler(this, new GameExitEventArgs
                {
                    Game = running.Game,
                    ExitCode = exitCode,
                    Duration = duration
                });
            }
        }

        private static string SafePid(Process process)
        {
            try
            {
                return process.Id.ToString();
            }
            catch
            {
                return "?";
            }
        }

        private sealed class RunningGame
        {
            public Game Game { get; set; }
            public Process Process { get; set; }
            public DateTime StartedUtc { get; set; }
        }
    }
}
