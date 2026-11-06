using System;
using System.Windows;
using System.Windows.Input;
using LegacyShelf.Services;

namespace LegacyShelf.Views
{
    public enum DialogTone
    {
        Info,
        Warning,
        Error
    }

    public partial class ThemedDialog : Window
    {
        public ThemedDialog()
        {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;
        }

        public static bool Show(
            Window owner,
            string title,
            string heading,
            string message,
            string details,
            string primaryText,
            string secondaryText,
            DialogTone tone)
        {
            var dialog = new ThemedDialog();
            if (owner != null)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.Title = title ?? "LegacyShelf";
            dialog.TitleText.Text = title ?? "LegacyShelf";
            dialog.HeadingText.Text = heading ?? string.Empty;
            dialog.MessageText.Text = message ?? string.Empty;
            dialog.PrimaryButton.Content = string.IsNullOrWhiteSpace(primaryText) ? "OK" : primaryText;
            dialog.SecondaryButton.Content = secondaryText ?? string.Empty;
            dialog.SecondaryButton.Visibility = string.IsNullOrWhiteSpace(secondaryText)
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (string.IsNullOrWhiteSpace(details))
            {
                dialog.DetailsPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                dialog.DetailsPanel.Visibility = Visibility.Visible;
                dialog.DetailsText.Text = details;
            }

            switch (tone)
            {
                case DialogTone.Warning:
                    dialog.ToneIcon.Text = "\uE7BA";
                    dialog.ToneIcon.Foreground = (System.Windows.Media.Brush)dialog.FindResource("AccentBrush");
                    break;
                case DialogTone.Error:
                    dialog.ToneIcon.Text = "\uE783";
                    dialog.ToneIcon.Foreground = (System.Windows.Media.Brush)dialog.FindResource("DangerBrush");
                    break;
                default:
                    dialog.ToneIcon.Text = "\uE946";
                    dialog.ToneIcon.Foreground = (System.Windows.Media.Brush)dialog.FindResource("AccentBrush");
                    break;
            }

            return dialog.ShowDialog() == true;
        }

        public static void Info(Window owner, string title, string heading, string message, string details = null)
        {
            Show(owner, title, heading, message, details, "OK", null, DialogTone.Info);
        }

        public static void Error(Window owner, string title, string heading, string message, string details = null)
        {
            Show(owner, title, heading, message, details, "Close", null, DialogTone.Error);
        }

        public static bool ShowWontFit(
            Window owner,
            string destination,
            long requiredBytes,
            long availableBytes,
            bool allowChooseAnotherFolder)
        {
            var shortfall = Math.Max(0, requiredBytes - availableBytes);
            var details =
                "Needed:   " + FormatUtil.Bytes(requiredBytes) + Environment.NewLine +
                "Available:" + " " + FormatUtil.Bytes(availableBytes) + Environment.NewLine +
                "Short by: " + FormatUtil.Bytes(shortfall) + Environment.NewLine +
                Environment.NewLine +
                destination;

            var message = allowChooseAnotherFolder
                ? "There is not enough free space at the destination. Choose another folder, or free up space and try again."
                : "The launcher folder does not have enough free space to unpack these games. Free up space on that drive and try again.";

            return Show(
                owner,
                "Not enough space",
                "This won't fit",
                message,
                details,
                allowChooseAnotherFolder ? "Choose another folder" : "Close",
                allowChooseAnotherFolder ? "Cancel" : null,
                DialogTone.Warning);
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

        private void Primary_Click(object sender, RoutedEventArgs e)
        {
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
