using System.Collections.Generic;

namespace LegacyShelf.Models
{
    public sealed class AppConfig
    {
        public int Version { get; set; }
        public SourceConfig Sources { get; set; }
        public UiConfig Ui { get; set; }
        public LaunchConfig Launch { get; set; }
        public GameConfig Games { get; set; }

        public AppConfig()
        {
            Version = 1;
            Sources = new SourceConfig();
            Ui = new UiConfig();
            Launch = new LaunchConfig();
            Games = new GameConfig();
        }

        public static AppConfig CreateDefault()
        {
            return new AppConfig();
        }

        public void EnsureDefaults()
        {
            if (Version <= 0) Version = 1;
            if (Sources == null) Sources = new SourceConfig();
            if (Ui == null) Ui = new UiConfig();
            if (Launch == null) Launch = new LaunchConfig();
            if (Games == null) Games = new GameConfig();

            if (Sources.InstallRoots == null) Sources.InstallRoots = new List<string>();
            if (Ui.TileSize < 140 || Ui.TileSize > 420) Ui.TileSize = 220;
            if (string.IsNullOrWhiteSpace(Ui.Sort)) Ui.Sort = "name";
            if (Games.Hidden == null) Games.Hidden = new List<string>();
            if (Games.ExeOverrides == null) Games.ExeOverrides = new Dictionary<string, string>();
        }
    }

    public sealed class SourceConfig
    {
        public List<string> InstallRoots { get; set; }
        public string LauncherDataDir { get; set; }
        public bool FetchMissingCovers { get; set; }

        public SourceConfig()
        {
            InstallRoots = new List<string>();
        }
    }

    public sealed class UiConfig
    {
        public bool Fullscreen { get; set; }
        public double TileSize { get; set; }
        public string Sort { get; set; }
        public bool ShowHidden { get; set; }
        public bool ShowAbsentGames { get; set; }

        public UiConfig()
        {
            TileSize = 220;
            Sort = "name";
        }
    }

    public sealed class LaunchConfig
    {
        public bool MinimizeWhilePlaying { get; set; }
        public bool RestoreOnExit { get; set; }

        public LaunchConfig()
        {
            MinimizeWhilePlaying = true;
            RestoreOnExit = true;
        }
    }

    public sealed class GameConfig
    {
        public List<string> Hidden { get; set; }
        public Dictionary<string, string> ExeOverrides { get; set; }

        public GameConfig()
        {
            Hidden = new List<string>();
            ExeOverrides = new Dictionary<string, string>();
        }
    }
}

