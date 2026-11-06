using System;

namespace LegacyShelf.Services
{
    public static class FormatUtil
    {
        public static string Bytes(long bytes)
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

        public static string Count(int count, string singular, string plural = null)
        {
            return count + " " + (count == 1 ? singular : (plural ?? singular + "s"));
        }
    }
}
