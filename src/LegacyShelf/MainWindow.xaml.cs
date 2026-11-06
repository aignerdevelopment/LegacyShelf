using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LegacyShelf.Controls;
using LegacyShelf.Models;
using LegacyShelf.Services;
using LegacyShelf.Views;
using Microsoft.Win32;

namespace LegacyShelf
{
    public partial class MainWindow : Window
    {
        public static readonly DependencyProperty TileSizeProperty = DependencyProperty.Register(
            "TileSize", typeof(double), typeof(MainWindow), new PropertyMetadata(220.0));

        private const uint MonitorDefaultToNearest = 2;

        private AppConfig _config;
        private readonly bool _firstRun;
        private readonly ObservableCollection<GameItem> _games;
        private readonly ICollectionView _view;
        private readonly LibraryScanner _scanner;
        private readonly GameLauncher _launcher;
        private readonly DispatcherTimer _toastTimer;

        private CancellationTokenSource _scanCts;
        private bool _scanning;
        private bool _isFullscreen;
        private bool _loaded;
        private WindowState _preFullscreenState;
        private Rect _preFullscreenBounds;
        private string _lastScannedAt;
        private string _lastArtworkSummary;
        private string _searchText;
        private bool _zipPrompted;

        public MainWindow(AppConfig config, bool fetchMissing, bool firstRun)
        {
            InitializeComponent();

            WindowPlacement.CenterOnPrimary(this);

            _config = ConfigStore.Clone(config);
            _config.EnsureDefaults();
            _firstRun = firstRun;
            _games = new ObservableCollection<GameItem>();
            _scanner = new LibraryScanner();
            _launcher = new GameLauncher();
            _searchText = string.Empty;

            TileSize = _config.Ui.TileSize;

            _view = CollectionViewSource.GetDefaultView(_games);
            _view.Filter = FilterGame;
            _view.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));

            GamesList.ItemsSource = _view;

            _launcher.GameExited += OnGameExited;

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                Toast.Visibility = Visibility.Collapsed;
            };

            Loaded += OnLoaded;
            Closing += OnClosing;
            PreviewKeyDown += OnPreviewKeyDown;
            PreviewMouseWheel += OnPreviewMouseWheel;
            StateChanged += OnStateChanged;
        }

        public double TileSize
        {
            get { return (double)GetValue(TileSizeProperty); }
            set { SetValue(TileSizeProperty, value); }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loaded = true;

            if (_config.Ui.Fullscreen)
            {
                EnterFullscreen(true);
            }

            if (_config.Sources.FetchMissingCovers && !_firstRun)
            {
                // Keep the setting from the first-run choice; it is only used when covers are missing.
            }

            if (_firstRun)
            {
                BeginScan(true);
                return;
            }

            if (LoadLibraryCache())
            {
                UpdateCount();
                _ = PrepareCoversAsync();

                // Refresh quietly after the cached shelf is visible.
                _ = Dispatcher.BeginInvoke(new Action(RefreshLater), DispatcherPriority.Background);
            }
            else
            {
                BeginScan(true);
            }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            try
            {
                if (_scanCts != null) _scanCts.Cancel();
                _config.Ui.Fullscreen = _isFullscreen;
                _config.Ui.TileSize = TileSize;
                ConfigStore.Save(_config);
                SaveLibraryCache();
            }
            catch (Exception ex)
            {
                Log.Warn("Could not save state on close: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- scanning

        private async void BeginScan(bool showOverlay)
        {
            if (_scanning) return;

            _scanning = true;
            RescanButton.IsEnabled = false;
            _scanCts = new CancellationTokenSource();

            if (showOverlay)
            {
                ScanOverlay.Visibility = Visibility.Visible;
                ScanProgress.Value = 0;
                ScanMessage.Text = "Scanning your library…";
                GamesList.IsEnabled = false;
            }
            else
            {
                CountText.Text = "Refreshing…";
            }

            var progress = new Progress<ScanProgress>(p =>
            {
                if (showOverlay)
                {
                    ScanProgress.Value = Math.Max(0, Math.Min(100, p.Percent));
                    if (!string.IsNullOrWhiteSpace(p.Message)) ScanMessage.Text = p.Message;
                }
                else if (!string.IsNullOrWhiteSpace(p.Message))
                {
                    CountText.Text = p.Message;
                }
            });

            try
            {
                var options = new ScanOptions
                {
                    Roots = _config.Sources.InstallRoots.ToList(),
                    LauncherDataDir = _config.Sources.LauncherDataDir,
                    FetchMissingCovers = _config.Sources.FetchMissingCovers,
                    ImportArtwork = true,
                    ExeOverrides = _config.Games.ExeOverrides
                };

                var result = await Task.Run(() => _scanner.Scan(options, progress, _scanCts.Token));
                ApplyScanResult(result);

                if (!showOverlay)
                {
                    ShowToast(string.Format(
                        "Library refreshed · {0} games · {1} covers",
                        result.Games.Count,
                        result.Artwork.FromCache + result.Artwork.Fetched));
                }
            }
            catch (OperationCanceledException)
            {
                Log.Info("Scan cancelled.");
            }
            catch (Exception ex)
            {
                Log.Error("Library scan failed.", ex);
                if (showOverlay)
                {
                    MessageBox.Show(this,
                        "The library scan failed.\n\n" + ex.Message +
                        "\n\nDetails were written to:\n" + AppPaths.LogPath,
                        "LegacyShelf",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    ShowToast("Refresh failed — see the log");
                }
            }
            finally
            {
                _scanning = false;
                RescanButton.IsEnabled = true;

                if (showOverlay)
                {
                    ScanOverlay.Visibility = Visibility.Collapsed;
                    GamesList.IsEnabled = true;
                }

                if (_scanCts != null)
                {
                    _scanCts.Dispose();
                    _scanCts = null;
                }

                UpdateCount();
            }
        }

        private void ApplyScanResult(ScanResult result)
        {
            if (result == null) return;

            _lastScannedAt = result.ScannedAt;
            _lastArtworkSummary = string.Format(
                "{0} cache · {1} fetched · {2} icons",
                result.Artwork.FromCache,
                result.Artwork.Fetched,
                result.Artwork.Icons);

            var selectedId = (GamesList.SelectedItem as GameItem)?.Id;
            var oldItems = _games.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
            var newItems = new List<GameItem>();

            var scroll = FindScrollViewer(GamesList);

            _games.Clear();
            foreach (var game in result.Games)
            {
                GameItem item;
                if (oldItems.TryGetValue(game.Id, out item))
                {
                    item.UpdateGame(game);
                }
                else
                {
                    item = new GameItem(game);
                    newItems.Add(item);
                }
                _games.Add(item);
            }

            _view.Refresh();

            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                var selected = _games.FirstOrDefault(i => string.Equals(i.Id, selectedId, StringComparison.OrdinalIgnoreCase));
                if (selected != null) GamesList.SelectedItem = selected;
            }

            if (newItems.Count > 0) _ = PrepareNewCovers(newItems);

            SaveLibraryCache();
            UpdateCount();

            if (scroll != null)
            {
                var offset = scroll.VerticalOffset;
                Dispatcher.BeginInvoke(new Action(() => scroll.ScrollToVerticalOffset(offset)), DispatcherPriority.Background);
            }

            var absent = result.Games.Count(g => g.IsAbsent);
            if (absent > 0 && !_config.Ui.ShowAbsentGames)
            {
                ShowToast(string.Format(
                    "{0} packed game{1} not unpacked — use Unzip",
                    absent,
                    absent == 1 ? "" : "s"));
            }
            else
            {
                ShowToast(string.Format(
                    "Library ready · {0} games · {1} covers",
                    result.Games.Count,
                    result.Artwork.FromCache + result.Artwork.Fetched));
            }

            _ = PromptForGameZipsAsync();
        }

        private async Task PromptForGameZipsAsync()
        {
            if (_zipPrompted) return;
            _zipPrompted = true;

            await Task.Delay(900);
            for (var i = 0; i < 20 && _scanning; i++) await Task.Delay(300);
            if (_scanning) return;

            List<GameZipCandidate> candidates;
            try
            {
                candidates = await Task.Run(() => new GameZipService().FindCandidates());
            }
            catch (Exception ex)
            {
                Log.Warn("Could not check game zips: " + ex.Message);
                return;
            }

            if (candidates.Count == 0) return;
            ShowGameZipDialog(candidates);
        }

        private async void Unzip_Click(object sender, RoutedEventArgs e)
        {
            _zipPrompted = true;

            List<GameZipCandidate> candidates;
            try
            {
                candidates = await Task.Run(() => new GameZipService().FindCandidates());
            }
            catch (Exception ex)
            {
                Log.Error("Could not check game zips.", ex);
                MessageBox.Show(this,
                    "Could not check the game zips.\n\n" + ex.Message,
                    "LegacyShelf",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (candidates.Count == 0)
            {
                ShowToast("No unpacked game zips found in this folder");
                return;
            }

            ShowGameZipDialog(candidates);
        }

        private void ShowGameZipDialog(IList<GameZipCandidate> candidates)
        {
            var dialog = new GameZipDialog(candidates)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                BeginScan(true);
            }
        }

        private async void RefreshLater()
        {
            await Task.Delay(900);
            if (_loaded && !_scanning && IsVisible) BeginScan(false);
        }

        private async Task PrepareCoversAsync()
        {
            var snapshot = _games.ToList();
            if (snapshot.Count == 0) return;

            await Task.Run(() =>
            {
                foreach (var item in snapshot) item.PrepareCover();
            });

            foreach (var item in snapshot) item.NotifyCover();
            UpdateCount();
        }

        private async Task PrepareNewCovers(List<GameItem> items)
        {
            if (items == null || items.Count == 0) return;

            await Task.Run(() =>
            {
                foreach (var item in items) item.PrepareCover();
            });

            foreach (var item in items) item.NotifyCover();
        }

        private bool LoadLibraryCache()
        {
            try
            {
                var cache = JsonUtil.Load<LibraryCache>(AppPaths.LibraryPath);
                if (cache == null || cache.Games == null || cache.Games.Count == 0) return false;

                _lastScannedAt = cache.ScannedAt;
                _games.Clear();
                foreach (var game in cache.Games)
                {
                    _games.Add(new GameItem(game));
                }
                _view.Refresh();
                UpdateCount();
                Log.Info("Loaded library cache with " + _games.Count + " games.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read library cache: " + ex.Message);
                return false;
            }
        }

        private void SaveLibraryCache()
        {
            try
            {
                var cache = new LibraryCache
                {
                    ScannedAt = string.IsNullOrWhiteSpace(_lastScannedAt)
                        ? DateTime.UtcNow.ToString("o")
                        : _lastScannedAt,
                    Roots = _config.Sources.InstallRoots.ToList(),
                    Games = _games.Select(i => i.Game).ToList()
                };
                JsonUtil.Save(AppPaths.LibraryPath, cache, pretty: true);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not save library cache: " + ex.Message);
            }
        }

        private bool FilterGame(object value)
        {
            var item = value as GameItem;
            if (item == null) return false;

            if (!_config.Ui.ShowHidden && _config.Games.Hidden.Contains(item.Id)) return false;
            if (item.Game.IsAbsent && !_config.Ui.ShowAbsentGames) return false;

            if (string.IsNullOrWhiteSpace(_searchText)) return true;
            return item.SearchText.Contains(_searchText);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = PathUtil.NormalizeName(SearchBox.Text);
            if (_view != null)
            {
                _view.Refresh();
                UpdateCount();
            }
        }

        private void UpdateCount()
        {
            if (_view == null) return;

            var absent = _games.Count(i => i.Game.IsAbsent);
            var availableTotal = _games.Count(i => !i.Game.IsAbsent || _config.Ui.ShowAbsentGames);
            var visible = _view.Cast<object>().Count();

            CountText.Text = visible == availableTotal
                ? string.Format("{0} game{1}", availableTotal, availableTotal == 1 ? "" : "s")
                : string.Format("{0} of {1}", visible, availableTotal);

            EmptySourcesButton.Visibility = Visibility.Visible;
            EmptyUnzipButton.Visibility = Visibility.Collapsed;

            if (visible == 0)
            {
                EmptyPanel.Visibility = Visibility.Visible;
                if (availableTotal == 0 && absent > 0 && !_config.Ui.ShowAbsentGames)
                {
                    EmptyTitle.Text = "No games unpacked yet";
                    EmptyText.Text = "Game zips are waiting in this folder. Extract them here or use Unzip.";
                    EmptySourcesButton.Visibility = Visibility.Collapsed;
                    EmptyUnzipButton.Visibility = Visibility.Visible;
                }
                else if (availableTotal == 0)
                {
                    EmptyTitle.Text = "No games found";
                    EmptyText.Text = "Add the folder where your games are installed.";
                }
                else
                {
                    EmptyTitle.Text = "No matches";
                    EmptyText.Text = "Try a different search, or clear the search box.";
                    EmptySourcesButton.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                EmptyPanel.Visibility = Visibility.Collapsed;
            }

            HintText.Visibility = availableTotal > 0 ? Visibility.Visible : Visibility.Collapsed;
            RescanButton.IsEnabled = !_scanning;
        }

        private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Selection is used by the keyboard and the context menu.
        }

        private void GameTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;

            var item = (sender as FrameworkElement)?.DataContext as GameItem;
            if (item == null) return;

            GamesList.SelectedItem = item;
            LaunchGame(item);
            e.Handled = true;
        }

        // ---------------------------------------------------------------- launching

        private void LaunchGame(GameItem item)
        {
            if (item == null) return;

            if (item.Game.IsMissing)
            {
                var locate = MessageBox.Show(this,
                    "This game's executable is missing.\n\n" + (item.Game.ExePath ?? "(no executable)") +
                    "\n\nLocate the executable now?",
                    "LegacyShelf",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (locate == MessageBoxResult.Yes) LocateExecutable(item);
                if (item.Game.IsMissing) return;
            }

            string error;
            if (!_launcher.Launch(item.Game, out error))
            {
                MessageBox.Show(this,
                    "Could not start this game.\n\n" + error +
                    "\n\nDetails were written to:\n" + AppPaths.LogPath,
                    "LegacyShelf",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (_config.Launch.MinimizeWhilePlaying)
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void LaunchSelected()
        {
            var item = GamesList.SelectedItem as GameItem;
            if (item == null) item = _view.Cast<object>().OfType<GameItem>().FirstOrDefault();
            if (item == null) return;

            GamesList.SelectedItem = item;
            LaunchGame(item);
        }

        private void OnGameExited(object sender, GameExitEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var item = _games.FirstOrDefault(i => string.Equals(i.Id, e.Game.Id, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    item.Game.PlaySeconds += (long)Math.Max(0, e.Duration.TotalSeconds);
                    item.Game.LastPlayedUtc = DateTime.UtcNow.ToString("o");
                    item.RefreshStatus();
                    SaveLibraryCache();
                }

                if (_launcher.RunningCount == 0 &&
                    _config.Launch.RestoreOnExit &&
                    WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                    Activate();
                    GamesList.Focus();
                }
            }), DispatcherPriority.Background);
        }

        // ---------------------------------------------------------------- context menu

        private static GameItem GetMenuItemGame(object sender)
        {
            var menuItem = sender as MenuItem;
            if (menuItem == null) return null;
            return menuItem.Tag as GameItem ?? menuItem.DataContext as GameItem;
        }

        private void PlayMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var item = GetMenuItemGame(sender);
            if (item == null) return;
            GamesList.SelectedItem = item;
            LaunchGame(item);
        }

        private void OpenFolderMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var item = GetMenuItemGame(sender);
            if (item == null) return;
            GameLauncher.OpenFolder(item.Game);
        }

        private void ChangeArtworkMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var item = GetMenuItemGame(sender);
            if (item == null) return;

            var dialog = new OpenFileDialog
            {
                Title = "Choose cover art for " + item.Name,
                Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true) return;

            try
            {
                var importer = new ArtworkImporter(new CoverIndexService());
                var entry = importer.SetOverride(item.Game, dialog.FileName, null);
                item.RefreshAfterArtwork(Path.Combine(AppPaths.ArtworkDir, entry.File), "override");
                SaveLibraryCache();
                ShowToast("Artwork updated");
            }
            catch (Exception ex)
            {
                Log.Error("Could not set artwork.", ex);
                MessageBox.Show(this,
                    "Could not use that image.\n\n" + ex.Message,
                    "LegacyShelf",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LocateExecutableMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var item = GetMenuItemGame(sender);
            if (item != null) LocateExecutable(item);
        }

        private void LocateExecutable(GameItem item)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Locate the executable for " + item.Name,
                Filter = "Programs|*.exe|All files|*.*",
                CheckFileExists = true,
                FileName = item.Game.ExeName
            };

            if (Directory.Exists(item.Game.InstallDir)) dialog.InitialDirectory = item.Game.InstallDir;
            if (dialog.ShowDialog(this) != true) return;

            _config.Games.ExeOverrides[item.Id] = dialog.FileName;
            ConfigStore.Save(_config);
            item.RefreshAfterExecutable(dialog.FileName);
            SaveLibraryCache();
            ShowToast("Executable updated");
        }

        private void HideGameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var item = GetMenuItemGame(sender);
            if (item == null) return;

            if (!_config.Games.Hidden.Contains(item.Id)) _config.Games.Hidden.Add(item.Id);
            ConfigStore.Save(_config);
            _view.Refresh();
            UpdateCount();
            ShowToast("Hidden · enable “Show hidden games” in sources to restore");
        }

        // ---------------------------------------------------------------- sources / rescan

        private void Pack_Click(object sender, RoutedEventArgs e)
        {
            OpenTransportPack(null);
        }

        private void PackGameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var item = GetMenuItemGame(sender);
            OpenTransportPack(item != null ? item.Id : null);
        }

        private void OpenTransportPack(string preselectGameId)
        {
            if (_scanning)
            {
                ShowToast("Wait for the scan to finish before packing");
                return;
            }

            if (_games.Count == 0)
            {
                ShowToast("Add games before making a transport pack");
                return;
            }

            var dialog = new TransportDialog(
                _config,
                _games.Select(i => i.Game).ToList(),
                preselectGameId)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }

        private void Rescan_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SourcesDialog(_config, BuildScanSummary())
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true) return;

            _config = dialog.Config;
            ConfigStore.Save(_config);
            TileSize = _config.Ui.TileSize;
            _view.Refresh();
            UpdateCount();
            BeginScan(true);
        }

        private string BuildScanSummary()
        {
            var when = "never";
            if (!string.IsNullOrWhiteSpace(_lastScannedAt))
            {
                DateTime parsed;
                if (DateTime.TryParse(_lastScannedAt, out parsed))
                {
                    when = parsed.ToLocalTime().ToString("d MMM yyyy HH:mm");
                }
                else
                {
                    when = _lastScannedAt;
                }
            }

            var location = string.IsNullOrWhiteSpace(_config.Sources.LauncherDataDir)
                ? "no launcher data folder"
                : _config.Sources.LauncherDataDir;

            return string.Format(
                "Last scan: {0} · {1} games · {2}\nLauncher data: {3}",
                when,
                _games.Count,
                string.IsNullOrWhiteSpace(_lastArtworkSummary) ? "artwork not imported yet" : _lastArtworkSummary,
                location);
        }

        // ---------------------------------------------------------------- keyboard / window

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                FocusSearch();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (!string.IsNullOrEmpty(SearchBox.Text))
                {
                    SearchBox.Clear();
                    FocusSearch();
                }
                else if (_isFullscreen)
                {
                    ExitFullscreen();
                }
                e.Handled = true;
                return;
            }

            if ((e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Alt) ||
                (e.Key == Key.System && e.SystemKey == Key.Enter && Keyboard.Modifiers == ModifierKeys.Alt))
            {
                ToggleFullscreen();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F5 && !_scanning)
            {
                BeginScan(true);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && !_scanning)
            {
                if (Keyboard.FocusedElement is Button) return;
                if (Keyboard.FocusedElement == SearchBox)
                {
                    var first = _view.Cast<object>().OfType<GameItem>().FirstOrDefault();
                    if (first != null) GamesList.SelectedItem = first;
                }
                LaunchSelected();
                e.Handled = true;
            }
        }

        private void FocusSearch()
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

            var delta = e.Delta > 0 ? 16 : -16;
            TileSize = Math.Max(150, Math.Min(340, TileSize + delta));
            _config.Ui.TileSize = TileSize;
            e.Handled = true;
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInteractiveSource(e.OriginalSource as DependencyObject)) return;

            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                    // Ignore drag errors.
                }
            }
        }

        private static bool IsInteractiveSource(DependencyObject source)
        {
            var current = source;
            var guard = 0;
            while (current != null && guard++ < 32)
            {
                if (current is TextBox || current is Button || current is ListBox ||
                    current is ListBoxItem || current is ScrollBar || current is MenuItem)
                {
                    return true;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void ToggleMaximize()
        {
            if (_isFullscreen) return;
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (MaximizeButton == null) return;
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
        }

        private void ToggleFullscreen()
        {
            if (_isFullscreen) ExitFullscreen();
            else EnterFullscreen(false);
        }

        private void EnterFullscreen(bool silent)
        {
            if (_isFullscreen) return;

            _preFullscreenState = WindowState;
            _preFullscreenBounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;

            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
                var info = new MonitorInfo { cbSize = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                {
                    var source = PresentationSource.FromVisual(this);
                    if (source != null && source.CompositionTarget != null)
                    {
                        var transform = source.CompositionTarget.TransformFromDevice;
                        var topLeft = transform.Transform(new Point(info.rcMonitor.Left, info.rcMonitor.Top));
                        var bottomRight = transform.Transform(new Point(info.rcMonitor.Right, info.rcMonitor.Bottom));
                        Left = topLeft.X;
                        Top = topLeft.Y;
                        Width = Math.Max(1, bottomRight.X - topLeft.X);
                        Height = Math.Max(1, bottomRight.Y - topLeft.Y);
                    }
                    else
                    {
                        Left = 0;
                        Top = 0;
                        Width = SystemParameters.PrimaryScreenWidth;
                        Height = SystemParameters.PrimaryScreenHeight;
                    }
                }
                else
                {
                    WindowState = WindowState.Maximized;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not enter true full screen; using maximized window. " + ex.Message);
                WindowState = WindowState.Maximized;
            }

            _isFullscreen = true;
            _config.Ui.Fullscreen = true;
            MaximizeButton.Visibility = Visibility.Collapsed;
            GamesList.Focus();

            if (!silent) ShowToast("Full screen  ·  Esc to return");
        }

        private void ExitFullscreen()
        {
            if (!_isFullscreen) return;

            _isFullscreen = false;
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.CanResize;

            if (_preFullscreenBounds.Width > 0 && _preFullscreenBounds.Height > 0)
            {
                Left = _preFullscreenBounds.Left;
                Top = _preFullscreenBounds.Top;
                Width = _preFullscreenBounds.Width;
                Height = _preFullscreenBounds.Height;
            }

            if (_preFullscreenState == WindowState.Maximized) WindowState = WindowState.Maximized;

            MaximizeButton.Visibility = Visibility.Visible;
            _config.Ui.Fullscreen = false;
            ShowToast("Windowed mode");
        }

        // ---------------------------------------------------------------- misc

        private void ShowToast(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            ToastText.Text = text;
            Toast.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            var viewer = root as ScrollViewer;
            if (viewer != null) return viewer;

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect32
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int cbSize;
            public Rect32 rcMonitor;
            public Rect32 rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);
    }
}




