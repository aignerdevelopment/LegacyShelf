using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    public partial class GameZipDialog : Window
    {
        private readonly ObservableCollection<GameZipItem> _items;
        private readonly List<GameZipCandidate> _candidates;
        private CancellationTokenSource _cts;
        private bool _unpacking;

        public GameZipDialog(IList<GameZipCandidate> candidates)
        {
            InitializeComponent();

            _candidates = (candidates ?? new List<GameZipCandidate>()).ToList();
            _items = new ObservableCollection<GameZipItem>();
            foreach (var candidate in _candidates)
            {
                var item = new GameZipItem(candidate, true);
                item.PropertyChanged += Item_PropertyChanged;
                _items.Add(item);
            }

            ZipList.ItemsSource = _items;
            UpdateSummary();
            PreviewKeyDown += OnPreviewKeyDown;
            Closing += OnClosing;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_unpacking)
            {
                DialogResult = false;
                Close();
            }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_unpacking)
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
            if (e.PropertyName == "IsSelected") UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (_items == null || UnpackButton == null) return;
            var selected = _items.Count(i => i.IsSelected);
            var bytes = _items.Where(i => i.IsSelected).Sum(i => i.Candidate.ZipBytes);
            SelectionText.Text = string.Format("{0} of {1} selected", selected, _items.Count);
            TotalText.Text = string.Format("{0} zip(s) · {1}", selected, FormatBytes(bytes));
            UnpackButton.IsEnabled = selected > 0 && !_unpacking;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items) item.IsSelected = true;
            UpdateSummary();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items) item.IsSelected = false;
            UpdateSummary();
        }

        private async void Unpack_Click(object sender, RoutedEventArgs e)
        {
            if (_unpacking) return;

            var selected = _items.Where(i => i.IsSelected).Select(i => i.Candidate).ToList();
            if (selected.Count == 0)
            {
                ThemedDialog.Info(this, "Unpack game zips", "Select a zip",
                    "Choose at least one game zip to unpack.");
                return;
            }

            UnpackButton.IsEnabled = false;
            UnpackButton.Content = "Checking…";

            long required;
            try
            {
                required = await Task.Run(() => GameZipService.GetExtractionBytes(selected));
            }
            catch (Exception ex)
            {
                UnpackButton.Content = "Unpack selected";
                UpdateSummary();
                ThemedDialog.Error(this, "Unpack game zips", "Could not read a zip",
                    ex.Message);
                return;
            }

            UnpackButton.Content = "Unpack selected";
            UpdateSummary();

            var available = StorageUtil.GetAvailableFreeSpace(AppPaths.ExeDir);
            if (available > 0 && required > available)
            {
                ThemedDialog.ShowWontFit(this, AppPaths.ExeDir, required, available, false);
                return;
            }

            _unpacking = true;
            _cts = new CancellationTokenSource();
            ProgressOverlay.Visibility = Visibility.Visible;
            Progress.Value = 0;
            PhaseText.Text = "Unpacking…";
            CurrentFileText.Text = "";
            BytesText.Text = "";
            UnpackButton.IsEnabled = false;

            var progress = new Progress<PackProgress>(p =>
            {
                Progress.Value = p.Percent;
                PhaseText.Text = p.Phase;
                CurrentFileText.Text = ShortenPath(p.CurrentFile);
                BytesText.Text = string.Format("{0} of {1}", FormatBytes(p.BytesDone), FormatBytes(Math.Max(p.BytesTotal, p.BytesDone)));
            });

            try
            {
                var service = new GameZipService();
                await Task.Run(() => service.Extract(selected, progress, _cts.Token));

                _unpacking = false;
                ProgressOverlay.Visibility = Visibility.Collapsed;
                UpdateSummary();

                var allUnpacked = _items.All(i => Directory.Exists(i.Candidate.TargetDir));
                if (allUnpacked)
                {
                    var delete = ThemedDialog.Show(
                        this,
                        "Unpack complete",
                        "All game zips are unpacked",
                        _items.Count == 1
                            ? "The game zip is unpacked. Keep it as a backup, or delete it now?"
                            : "All " + _items.Count + " game zips are unpacked. Keep them as a backup, or delete them now?",
                        null,
                        "Delete zips",
                        "Keep zips",
                        DialogTone.Info);

                    if (delete)
                    {
                        GameZipService.DeleteZips(_candidates);
                    }
                }

                DialogResult = true;
                Close();
            }
            catch (OperationCanceledException)
            {
                _unpacking = false;
                ProgressOverlay.Visibility = Visibility.Collapsed;
                UpdateSummary();
                ThemedDialog.Info(this, "Unpack cancelled", "Unpacking was cancelled",
                    "Partial game folders created by this run were removed.");
            }
            catch (Exception ex)
            {
                _unpacking = false;
                ProgressOverlay.Visibility = Visibility.Collapsed;
                UpdateSummary();
                Log.Error("Game zip extraction failed.", ex);
                ThemedDialog.Error(
                    this,
                    "Unpack failed",
                    "Could not unpack the game zip(s)",
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

        private void CancelUnpack_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                PhaseText.Text = "Cancelling…";
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_unpacking)
            {
                if (_cts != null) _cts.Cancel();
                PhaseText.Text = "Cancelling…";
                return;
            }

            DialogResult = false;
            Close();
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

