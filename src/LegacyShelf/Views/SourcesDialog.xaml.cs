using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using LegacyShelf.Models;
using LegacyShelf.Services;

namespace LegacyShelf.Views
{
    public partial class SourcesDialog : Window
    {
        private readonly ObservableCollection<string> _roots;
        private string _launcherDir;
        private readonly string _lastScanText;

        public AppConfig Config { get; private set; }

        public SourcesDialog(AppConfig config, string lastScanText)
        {
            InitializeComponent();

            Config = ConfigStore.Clone(config);
            _roots = new ObservableCollection<string>(Config.Sources.InstallRoots.Where(r => !string.IsNullOrWhiteSpace(r)));
            _launcherDir = Config.Sources.LauncherDataDir;
            _lastScanText = lastScanText;

            RootsList.ItemsSource = _roots;
            ShowHiddenCheckBox.IsChecked = Config.Ui.ShowHidden;
            FetchCoversCheckBox.IsChecked = Config.Sources.FetchMissingCovers;
            LastScanText.Text = lastScanText ?? string.Empty;

            if (_roots.Count > 0) RootsList.SelectedIndex = 0;
            UpdateLauncherText();
            UpdateButtons();
            PreviewKeyDown += OnPreviewKeyDown;
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

        private void RootsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var hasSelection = RootsList.SelectedItem != null;
            EditRootButton.IsEnabled = hasSelection;
            RemoveRootButton.IsEnabled = hasSelection;
        }

        private void AddRoot_Click(object sender, RoutedEventArgs e)
        {
            var picked = FolderPicker.Pick(
                "Add an installed games directory",
                _roots.FirstOrDefault(),
                this);
            if (string.IsNullOrWhiteSpace(picked)) return;

            var normalized = PathUtil.Normalize(picked);
            if (_roots.Any(r => PathUtil.SamePath(r, normalized)))
            {
                MessageBox.Show(this, "That directory is already in the list.", "Library sources",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _roots.Add(normalized);
            RootsList.SelectedItem = normalized;
            UpdateButtons();
        }

        private void EditRoot_Click(object sender, RoutedEventArgs e)
        {
            var selected = RootsList.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selected))
            {
                MessageBox.Show(this, "Select a directory first.", "Library sources",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var picked = FolderPicker.Pick("Edit installed games directory", selected, this);
            if (string.IsNullOrWhiteSpace(picked)) return;

            var normalized = PathUtil.Normalize(picked);
            if (_roots.Any(r => !PathUtil.SamePath(r, selected) && PathUtil.SamePath(r, normalized)))
            {
                MessageBox.Show(this, "That directory is already in the list.", "Library sources",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var index = _roots.IndexOf(selected);
            _roots[index] = normalized;
            RootsList.SelectedIndex = index;
            UpdateButtons();
        }

        private void RemoveRoot_Click(object sender, RoutedEventArgs e)
        {
            var selected = RootsList.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selected)) return;

            var index = _roots.IndexOf(selected);
            _roots.RemoveAt(index);
            if (_roots.Count > 0) RootsList.SelectedIndex = Math.Min(index, _roots.Count - 1);
            UpdateButtons();
        }

        private void EditLauncher_Click(object sender, RoutedEventArgs e)
        {
            var picked = FolderPicker.Pick(
                "Select the original Legacy Games launcher data folder",
                _launcherDir,
                this);
            if (string.IsNullOrWhiteSpace(picked)) return;

            _launcherDir = PathUtil.Normalize(picked);
            UpdateLauncherText();
        }

        private void ClearLauncher_Click(object sender, RoutedEventArgs e)
        {
            _launcherDir = null;
            UpdateLauncherText();
        }

        private void UpdateLauncherText()
        {
            if (string.IsNullOrWhiteSpace(_launcherDir))
            {
                LauncherPathText.Text = "Not set";
                LauncherPathText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
                return;
            }

            LauncherPathText.Text = _launcherDir;
            LauncherPathText.Foreground = Directory.Exists(_launcherDir)
                ? (System.Windows.Media.Brush)FindResource("TextBrush")
                : (System.Windows.Media.Brush)FindResource("DangerBrush");
        }

        private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + AppPaths.BaseDir + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Warn("Could not open data folder: " + ex.Message);
            }
        }

        private void Rescan_Click(object sender, RoutedEventArgs e)
        {
            if (_roots.Count == 0)
            {
                MessageBox.Show(this,
                    "Add at least one installed games directory before rescanning.",
                    "Library sources",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var missing = _roots.FirstOrDefault(r => !Directory.Exists(r));
            if (!string.IsNullOrWhiteSpace(missing))
            {
                var proceed = MessageBox.Show(this,
                    "This directory does not exist:\n\n" + missing +
                    "\n\nRemove it or choose another directory. Scan anyway?",
                    "Library sources",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (proceed != MessageBoxResult.Yes) return;
            }

            Config.Sources.InstallRoots = _roots.Select(PathUtil.Normalize).ToList();
            Config.Sources.LauncherDataDir = _launcherDir;
            Config.Sources.FetchMissingCovers = FetchCoversCheckBox.IsChecked == true;
            Config.Ui.ShowHidden = ShowHiddenCheckBox.IsChecked == true;

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
