using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LegacyShelf.Models;
using LegacyShelf.Services;

namespace LegacyShelf.Controls
{
    public sealed class GameItem : INotifyPropertyChanged
    {
        private ImageBrush _coverBrush;
        private bool _isIconArt;
        private bool _isPlaceholder = true;

        public Game Game { get; private set; }

        public GameItem(Game game)
        {
            Game = game;
        }

        public string Name
        {
            get { return Game.Name; }
        }

        public string FolderName
        {
            get { return Game.FolderName; }
        }

        public string Id
        {
            get { return Game.Id; }
        }

        public bool IsMissing
        {
            get { return Game.IsMissing; }
        }

        public string StatusText
        {
            get { return Game.IsMissing ? "Missing files" : "Installed"; }
        }

        public string Initials
        {
            get
            {
                var source = string.IsNullOrWhiteSpace(Game.Name) ? Game.FolderName : Game.Name;
                if (string.IsNullOrWhiteSpace(source)) return "?";
                var words = source.Split(new[] { ' ', ':', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) return "?";
                if (words.Length == 1) return words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant();
                return (words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpperInvariant();
            }
        }

        public string SearchText
        {
            get
            {
                return PathUtil.NormalizeName(
                    (Game.Name ?? string.Empty) + " " +
                    (Game.FolderName ?? string.Empty) + " " +
                    (Game.ExeName ?? string.Empty));
            }
        }

        public string ToolTipText
        {
            get
            {
                var lines = new System.Collections.Generic.List<string>();
                lines.Add(Game.Name ?? Game.FolderName ?? "Game");
                if (!string.IsNullOrWhiteSpace(Game.Description)) lines.Add(Game.Description);
                lines.Add("");
                lines.Add("Folder: " + (Game.InstallDir ?? "?"));
                lines.Add("Executable: " + (Game.ExeName ?? "?"));
                lines.Add("Status: " + StatusText);
                if (Game.PlaySeconds > 0)
                {
                    var span = TimeSpan.FromSeconds(Game.PlaySeconds);
                    lines.Add("Played: " + (span.TotalHours >= 1
                        ? string.Format("{0:0}h {1:00}m", span.TotalHours, span.Minutes)
                        : string.Format("{0}m", Math.Max(1, span.Minutes))));
                }
                return string.Join("\n", lines);
            }
        }

        public ImageBrush CoverBrush
        {
            get { return _coverBrush; }
        }

        public bool IsIconArt
        {
            get { return _isIconArt; }
        }

        public bool IsPlaceholder
        {
            get { return _isPlaceholder; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public override string ToString()
        {
            return Name ?? FolderName ?? "Game";
        }

        public void PrepareCover()
        {
            _coverBrush = null;
            _isPlaceholder = true;
            _isIconArt = string.Equals(Game.ArtworkSource, "icon", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Game.ArtworkSource, "placeholder", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(Game.CoverPath) || !File.Exists(Game.CoverPath)) return;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(Game.CoverPath, UriKind.Absolute);
                bitmap.DecodePixelWidth = _isIconArt ? 360 : 520;
                bitmap.EndInit();
                bitmap.Freeze();

                var brush = new ImageBrush(bitmap)
                {
                    Stretch = _isIconArt ? Stretch.Uniform : Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };

                if (_isIconArt)
                {
                    brush.Viewbox = new System.Windows.Rect(0.05, 0.05, 0.90, 0.90);
                    brush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
                }

                brush.Freeze();
                _coverBrush = brush;
                _isPlaceholder = false;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not load artwork for '" + Name + "': " + ex.Message);
            }
        }

        public void NotifyCover()
        {
            OnPropertyChanged("CoverBrush");
            OnPropertyChanged("IsIconArt");
            OnPropertyChanged("IsPlaceholder");
        }

        public void RefreshStatus()
        {
            OnPropertyChanged("Name");
            OnPropertyChanged("IsMissing");
            OnPropertyChanged("StatusText");
            OnPropertyChanged("ToolTipText");
            OnPropertyChanged("SearchText");
            OnPropertyChanged("Initials");
        }

        public void UpdateGame(Game game)
        {
            var sameArtwork = string.Equals(Game.CoverPath, game.CoverPath, StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(Game.ArtworkSource, game.ArtworkSource, StringComparison.OrdinalIgnoreCase);
            Game = game;
            if (!sameArtwork)
            {
                PrepareCover();
                NotifyCover();
            }
            RefreshStatus();
        }

        public void RefreshAfterArtwork(string coverPath, string source)
        {
            Game.CoverPath = coverPath;
            Game.ArtworkSource = source;
            PrepareCover();
            NotifyCover();
            OnPropertyChanged("ToolTipText");
        }

        public void RefreshAfterExecutable(string exePath)
        {
            Game.ExePath = exePath;
            Game.ExeName = Path.GetFileName(exePath);
            Game.Status = File.Exists(exePath) ? "ok" : "missing";
            RefreshStatus();
        }

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
