using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LegacyShelf.Controls;
using LegacyShelf.Models;
using LegacyShelf.Services;

namespace LegacyShelf.Views
{
    public partial class TransportDialog : Window
    {
        private readonly AppConfig _config;
        private readonly ObservableCollection<TransportGameItem> _items;
        private readonly string _launcherDir;
        private readonly bool _launcherAvailable;
        private CancellationTokenSource _cts;
        private bool _packing;
        private bool _sizesComputed;
        private long _launcherBytes;

        public TransportDialog(AppConfig config, System.Collections.Generic.IList<Game> games, string preselectGameId = null)
        {
            InitializeComponent();

            _config = ConfigStore.Clone(config);
            _config.EnsureDefaults();
            _items = new ObservableCollection<TransportGameItem>();

            foreach (var game in (games ?? new System.Collections.Generic.List<Game>())
                .Where(g => g != null)
                .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var selected = string.IsNullOrWhiteSpace(preselectGameId) ||
                               string.Equals(game.Id, preselectGameId, StringComparison.OrdinalIgnoreCase);
                var item = new TransportGameItem(game, selected);
                item.PropertyChanged += Item_PropertyChanged;
                _items.Add(item);
            }

            GamesList.ItemsSource = _items;

            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents)) documents = AppPaths.ExeDir;
            DestinationTextBox.Text = Path.Combine(
                documents,
                "LegacyShelf-Transport-" + DateTime.Now.ToString("yyyyMMdd-HHmm"));

            _launcherDir = config.Sources.LauncherDataDir;
            _launcherAvailable = IsLauncherDataAvailable(_launcherDir);
            IncludeLauncherCheckBox.IsChecked = _launcherAvailable;
            IncludeLauncherCheckBox.IsEnabled = _launcherAvailable;
            LauncherDataStatusText.Text = _launcherAvailable
                ? "Will include sanitized catalog metadata and cover cache from: " + _launcherDir
                : "No original launcher data is configured. The pack will rely on the imported artwork.";

            UpdateDestinationStatus();
            UpdateSelectionSummary();
            PreviewKeyDown += OnPreviewKeyDown;
            Closing += OnClosing;

            _ = ComputeSizesAsync();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_packing)
            {
                DialogResult = false;
                Close();
            }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_packing)
            {
                e.Cancel = true;
                if (_cts != null) _cts.Cancel();
                PhaseText.Text = "Cancelling…";
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsSelected" || e.PropertyName == "SizeText")
            {
                UpdateSelectionSummary();
            }
        }

        private void DestinationTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateDestinationStatus();
            UpdateSelectionSummary();
        }

        private async Task ComputeSizesAsync()
        {
            var items = _items.ToList();
            await Task.Run(() =>
            {
                foreach (var item in items)
                {
                    item.SizeBytes = DirectorySize(item.Game.InstallDir);
                }
                if (_launcherAvailable) _launcherBytes = DirectorySize(_launcherDir);
            });

            foreach (var item in items)
            {
                item.SizeText = FormatBytes(item.SizeBytes);
            }
            _sizesComputed = true;
            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            if (_items == null || PackButton == null || DestinationTextBox == null) return;

            var selected = _items.Where(i => i.IsSelected).ToList();
            var bytes = selected.Sum(i => i.SizeBytes);
            SelectionText.Text = string.Format(
                "{0} of {1} selected{2}",
                selected.Count,
                _items.Count,
                bytes > 0 ? " · about " + FormatBytes(bytes) : "");

            var free = GetFreeSpace(DestinationTextBox.Text);
            var estimate = selected.Count == 0
                ? "Select at least one game."
                : string.Format(
                    "Estimated game files: {0}{1}{2}",
                    bytes > 0 ? FormatBytes(bytes) : "calculating…",
                    IncludeLauncherCheckBox.IsChecked == true && _launcherAvailable && _launcherBytes > 0
                        ? " + launcher data " + FormatBytes(_launcherBytes)
                        : "",
                    free > 0 ? " · destination free: " + FormatBytes(free) : "");

            EstimateText.Text = estimate;
            PackButton.IsEnabled = selected.Count > 0 && !_packing &&
                                   !string.IsNullOrWhiteSpace(DestinationTextBox.Text) &&
                                   !Directory.Exists(DestinationTextBox.Text.Trim().Trim('"'));
        }

        private void UpdateDestinationStatus()
        {
            if (DestinationTextBox == null || DestinationStatusText == null) return;

            var value = DestinationTextBox.Text.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value))
            {
                DestinationStatusText.Text = "Choose an empty/new folder outside your game library.";
                DestinationStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
                return;
            }

            try
            {
                var full = PathUtil.Normalize(value);
                if (Directory.Exists(full))
                {
                    DestinationStatusText.Text = "That folder already exists. Choose a new folder name.";
                    DestinationStatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
                    return;
                }

                var parent = Path.GetDirectoryName(full);
                if (!string.IsNullOrWhiteSpace(parent) && !Directory.Exists(parent))
                {
                    DestinationStatusText.Text = "The parent folder does not exist.";
                    DestinationStatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
                    return;
                }

                var free = GetFreeSpace(full);
                DestinationStatusText.Text = free > 0
                    ? "New folder will be created · " + FormatBytes(free) + " free"
                    : "New folder will be created.";
                DestinationStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
            }
            catch (Exception ex)
            {
                DestinationStatusText.Text = ex.Message;
                DestinationStatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            }
        }

        private void IncludeLauncher_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSelectionSummary();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items) item.IsSelected = true;
            UpdateSelectionSummary();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items) item.IsSelected = false;
            UpdateSelectionSummary();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var initial = Path.GetDirectoryName(DestinationTextBox.Text);
            var picked = FolderPicker.Pick("Choose the parent folder for the transport pack", initial, this);
            if (string.IsNullOrWhiteSpace(picked)) return;

            DestinationTextBox.Text = Path.Combine(
                picked,
                "LegacyShelf-Transport-" + DateTime.Now.ToString("yyyyMMdd-HHmm"));
        }

        private async void Pack_Click(object sender, RoutedEventArgs e)
        {
            if (_packing) return;

            var selectedItems = _items.Where(i => i.IsSelected).ToList();
            var selected = selectedItems.Select(i => i.Game).ToList();
            if (selected.Count == 0)
            {
                ThemedDialog.Info(this, "Pack for transport", "Select a game",
                    "Choose at least one game to include in the transport pack.");
                return;
            }

            var destination = DestinationTextBox.Text.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(destination))
            {
                ThemedDialog.Info(this, "Pack for transport", "Choose a folder",
                    "Choose a new destination folder for the transport zips.");
                return;
            }

            if (Directory.Exists(destination))
            {
                ThemedDialog.Info(this, "Pack for transport", "Folder already exists",
                    "Choose a new folder name so the pack cannot mix with existing data.");
                return;
            }

            var includeLauncher = IncludeLauncherCheckBox.IsChecked == true && _launcherAvailable;
            var required = _sizesComputed
                ? selectedItems.Sum(i => i.SizeBytes) + (includeLauncher ? _launcherBytes : 0) + 32L * 1024 * 1024
                : await Task.Run(() => TransportPacker.EstimateRequiredBytes(_config, selected, includeLauncher));

            while (true)
            {
                var available = StorageUtil.GetAvailableFreeSpace(destination);
                if (available <= 0 || required <= available) break;

                var chooseAnother = ThemedDialog.ShowWontFit(this, destination, required, available, true);
                if (!chooseAnother) return;

                var picked = FolderPicker.Pick(
                    "Choose another destination folder",
                    Path.GetDirectoryName(destination),
                    this);
                if (string.IsNullOrWhiteSpace(picked)) return;

                DestinationTextBox.Text = Path.Combine(
                    picked,
                    "LegacyShelf-Transport-" + DateTime.Now.ToString("yyyyMMdd-HHmm"));
                destination = DestinationTextBox.Text.Trim().Trim('"');

                if (Directory.Exists(destination))
                {
                    ThemedDialog.Info(this, "Pack for transport", "Folder already exists",
                        "Choose a new folder name so the pack cannot mix with existing data.");
                    return;
                }
            }

            _packing = true;
            _cts = new CancellationTokenSource();
            ProgressOverlay.Visibility = Visibility.Visible;
            Progress.Value = 0;
            PhaseText.Text = "Preparing…";
            CurrentFileText.Text = "";
            BytesText.Text = "";
            PackButton.IsEnabled = false;

            var progress = new Progress<PackProgress>(p =>
            {
                Progress.Value = p.Percent;
                PhaseText.Text = p.Phase;
                CurrentFileText.Text = ShortenPath(p.CurrentFile);
                BytesText.Text = string.Format("{0} of {1}",
                    FormatBytes(p.BytesDone),
                    FormatBytes(Math.Max(p.BytesTotal, p.BytesDone)));
            });

            try
            {
                var packer = new TransportPacker(progress, _cts.Token);
                var result = await Task.Run(() => packer.Pack(_config, selected, destination, includeLauncher));

                _packing = false;
                ProgressOverlay.Visibility = Visibility.Collapsed;
                PackButton.IsEnabled = true;

                var open = ThemedDialog.Show(
                    this,
                    "Pack complete",
                    "Transport pack created",
                    string.Format(
                        "Launcher zip + {0} game zip{1}.",
                        result.GameCount,
                        result.GameCount == 1 ? "" : "s"),
                    string.Format(
                        "Total output: {0}{1}{1}{2}",
                        FormatBytes(result.TotalBytes),
                        Environment.NewLine,
                        result.Destination),
                    "Open folder",
                    "Close",
                    DialogTone.Info);

                if (open)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = "\"" + result.Destination + "\"",
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        // Ignore explorer failures.
                    }
                }

                DialogResult = true;
                Close();
            }
            catch (OperationCanceledException)
            {
                _packing = false;
                ProgressOverlay.Visibility = Visibility.Collapsed;
                UpdateSelectionSummary();
                ThemedDialog.Info(this, "Pack cancelled", "Packing was cancelled",
                    "Any partial destination folder was removed.");
            }
            catch (NotEnoughSpaceException ex)
            {
                _packing = false;
                ProgressOverlay.Visibility = Visibility.Collapsed;
                UpdateSelectionSummary();
                ThemedDialog.ShowWontFit(
                    this,
                    ex.TargetPath,
                    ex.RequiredBytes,
                    ex.AvailableBytes,
                    false);
            }
            catch (Exception ex)
            {
                _packing = false;
                ProgressOverlay.Visibility = Visibility.Collapsed;
                UpdateSelectionSummary();
                Log.Error("Transport pack failed.", ex);
                ThemedDialog.Error(
                    this,
                    "Pack failed",
                    "The transport pack failed",
                    ex.Message,
                    "Details were written to:" + Environment.NewLine + AppPaths.LogPath);
            }
            finally
            {
                if (_cts != null)
                {
                    _cts.Dispose();
                    _cts = null;
                }
            }
        }

        private void CancelPacking_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                PhaseText.Text = "Cancelling…";
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_packing)
            {
                if (_cts != null) _cts.Cancel();
                PhaseText.Text = "Cancelling…";
                return;
            }

            DialogResult = false;
            Close();
        }

        private static bool IsLauncherDataAvailable(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
            return File.Exists(Path.Combine(dir, "app-state.json")) ||
                   Directory.Exists(Path.Combine(dir, "Cache", "Cache_Data"));
        }

        private static long DirectorySize(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return 0;

            long total = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                        total += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Ignore unreadable files.
                    }
                }
            }
            catch
            {
                // Keep the partial total.
            }
            return total;
        }

        private static long GetFreeSpace(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return 0;
                var root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrWhiteSpace(root)) return 0;
                var drive = new DriveInfo(root);
                return drive.IsReady ? drive.AvailableFreeSpace : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            var value = (double)bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString(unit >= 3 ? "0.0" : "0") + " " + units[unit];
        }

        private static string ShortenPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            return path.Length <= 90 ? path : "…" + path.Substring(path.Length - 89);
        }
    }
}

