using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace LegacyShelf.Services
{
    public static class WindowPlacement
    {
        public static void CenterOnPrimary(Window window)
        {
            if (window == null) return;

            try
            {
                var screen = Screen.PrimaryScreen;
                if (screen == null) return;

                var area = screen.WorkingArea;
                double scale = 1.0;
                try
                {
                    using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
                    {
                        if (graphics.DpiX > 0) scale = graphics.DpiX / 96.0;
                    }
                }
                catch
                {
                    // Use 100% if the DPI cannot be read.
                }

                var widthPixels = window.Width * scale;
                var heightPixels = window.Height * scale;

                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = (area.Left + (area.Width - widthPixels) / 2.0) / scale;
                window.Top = (area.Top + (area.Height - heightPixels) / 2.0) / scale;
            }
            catch
            {
                // Keep the WPF default if anything goes wrong.
            }
        }
    }
}
