using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LegacyShelf.Models;
using LegacyShelf.Services;

namespace LegacyShelf.Views
{
    public partial class WelcomeWindow : Window
    {
        private readonly DispatcherTimer _validationTimer;
        private bool _loaded;

        public AppConfig Config { get; private set; }
        public bool FetchMissingCovers { get; private set; }

        public WelcomeWindow(AppConfig config)
        {
            InitializeComponent();
            Config = ConfigStore.Clone(config);
            FetchMissingCovers = false;

            WindowPlacement.CenterOnPrimary(this);

            _validationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _validationTimer.Tick += (s, e) =>
            {
                _validationTimer.Stop();
                ValidateAll();
            };

            Loaded += OnLoaded;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loaded = true;

            var root = Config.Sources.InstallRoots.FirstOrDefault();
            var launcherDir = Config.Sources.LauncherDataDir;

            if (string.IsNullOrWhiteSpace(launcherDir))
            {
                launcherDir = LauncherDataReader.DetectDefaultLauncherDir();
            }

            if (string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(launcherDir))
            {
                var data = LauncherDataReader.Read(launcherDir);
                root = data.GameLibraryPaths.FirstOrDefault(Directory.Exists);
            }

            if (string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(launcherDir))
            {
                var detected = RegistryScanner.DetectCommonRoot();
                if (!string.IsNullOrWhiteSpace(detected) && Directory.Exists(detected)) root = detected;
            }

            InstallRootTextBox.Text = root ?? string.Empty;
            LauncherDirTextBox.Text = launcherDir ?? string.Empty;
            FetchCoversCheckBox.IsChecked = false;

            ValidateAll();
            InstallRootTextBox.Focus();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void InstallRootTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_loaded) return;
            _validationTimer.Stop();
            _validationTimer.Start();
        }

        private void LauncherDirTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_loaded) return;
            _validationTimer.Stop();
            _validationTimer.Start();
        }

        private void BrowseInstall_Click(object sender, RoutedEventArgs e)
        {
            var picked = FolderPicker.Pick(
                "Select the folder that contains your installed games",
                InstallRootTextBox.Text, this);
            if (!string.IsNullOrWhiteSpace(picked)) InstallRootTextBox.Text = picked;
        }

        private void BrowseLauncher_Click(object sender, RoutedEventArgs e)
        {
            var picked = FolderPicker.Pick(
                "Select the original Legacy Games launcher data folder",
                LauncherDirTextBox.Text, this);
            if (!string.IsNullOrWhiteSpace(picked)) LauncherDirTextBox.Text = picked;
        }

        private async void ValidateAll()
        {
            if (!_loaded) return;

            var root = InstallRootTextBox.Text.Trim().Trim('"');
            var launcher = LauncherDirTextBox.Text.Trim().Trim('"');

            if (string.IsNullOrWhiteSpace(root))
            {
                InstallStatusText.Text = "Choose the folder where your games are installed.";
                InstallStatusText.Foreground = (Brush)FindResource("MutedBrush");
            }
            else if (!Directory.Exists(root))
            {
                InstallStatusText.Text = "That folder does not exist.";
                InstallStatusText.Foreground = (Brush)FindResource("DangerBrush");
            }
            else
            {
                var count = await Task.Run(() => FolderScanner.CountGames(root));
                if (!string.Equals(root, InstallRootTextBox.Text.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase)) return;

                if (count > 0)
                {
                    InstallStatusText.Text = "✓ " + count + " game folder" + (count == 1 ? "" : "s") + " detected.";
                    InstallStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0xD1, 0xA0));
                }
                else
                {
                    InstallStatusText.Text = "No game folders detected here. You can still scan, or choose another folder.";
                    InstallStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xB5, 0x63));
                }
            }

            if (string.IsNullOrWhiteSpace(launcher))
            {
                LauncherStatusText.Text = "Optional. You can add this later from the rescan dialog.";
                LauncherStatusText.Foreground = (Brush)FindResource("MutedBrush");
            }
            else if (!Directory.Exists(launcher))
            {
                LauncherStatusText.Text = "That folder does not exist.";
                LauncherStatusText.Foreground = (Brush)FindResource("DangerBrush");
            }
            else
            {
                var hasCatalog = File.Exists(Path.Combine(launcher, "app-state.json"));
                var hasCache = Directory.Exists(Path.Combine(launcher, "Cache", "Cache_Data"));
                if (hasCatalog || hasCache)
                {
                    LauncherStatusText.Text = "✓ Launcher data found" +
                        (hasCache ? " · cover cache available" : " · no cover cache");
                    LauncherStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0xD1, 0xA0));
                }
                else
                {
                    LauncherStatusText.Text = "This folder does not look like the Legacy Games launcher data folder.";
                    LauncherStatusText.Foreground = (Brush)FindResource("DangerBrush");
                }
            }
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            var root = InstallRootTextBox.Text.Trim().Trim('"');
            var launcher = LauncherDirTextBox.Text.Trim().Trim('"');

            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                MessageBox.Show(this,
                    "Please choose the folder that contains your installed games.",
                    "LegacyShelf",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                InstallRootTextBox.Focus();
                return;
            }

            ScanButton.IsEnabled = false;
            ScanButton.Content = "Scanning…";

            var count = await Task.Run(() => FolderScanner.CountGames(root));
            if (count == 0)
            {
                var proceed = MessageBox.Show(this,
                    "No game folders were detected in this folder.\n\n" +
                    "You can still scan, but the shelf may be empty. Continue?",
                    "LegacyShelf",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (proceed != MessageBoxResult.Yes)
                {
                    ScanButton.IsEnabled = true;
                    ScanButton.Content = "Scan library  →";
                    return;
                }
            }

            Config.Sources.InstallRoots.Clear();
            Config.Sources.InstallRoots.Add(PathUtil.Normalize(root));
            Config.Sources.LauncherDataDir = string.IsNullOrWhiteSpace(launcher)
                ? null
                : PathUtil.Normalize(launcher);
            Config.Sources.FetchMissingCovers = FetchCoversCheckBox.IsChecked == true;
            FetchMissingCovers = Config.Sources.FetchMissingCovers;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

