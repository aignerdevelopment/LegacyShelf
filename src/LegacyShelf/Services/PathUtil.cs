using System;
using System.IO;
using System.Text;

namespace LegacyShelf.Services
{
    public static class PathUtil
    {
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                var full = Path.GetFullPath(path.Trim().Trim('"'));
                return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        public static string Key(string path)
        {
            return Normalize(path).ToUpperInvariant();
        }

        public static bool IsUnder(string path, string root)
        {
            var p = Key(path);
            var r = Key(root);
            if (p.Length == 0 || r.Length == 0) return false;
            if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return true;
            return p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static bool SamePath(string a, string b)
        {
            return string.Equals(Key(a), Key(b), StringComparison.OrdinalIgnoreCase);
        }

        public static string SafeFileStem(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "game";
            var sb = new StringBuilder(id.Length);
            foreach (var c in id)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else sb.Append('-');
            }
            var result = sb.ToString().Trim('-');
            return string.IsNullOrEmpty(result) ? "game" : result;
        }

        public static string NormalizeName(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var normalized = value.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                else if (char.IsWhiteSpace(c)) sb.Append(' ');
            }
            return sb.ToString().Trim();
        }

        public static string CompactName(string value)
        {
            var normalized = NormalizeName(value);
            return normalized.Replace(" ", string.Empty);
        }

        public static bool LooksLikeGameName(string folder, string exeName)
        {
            var a = CompactName(folder);
            var b = CompactName(Path.GetFileNameWithoutExtension(exeName ?? string.Empty));
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            if (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal)) return true;

            // Compare with common suffixes removed (CE, HD, etc.).
            foreach (var suffix in new[] { "ce", "hd", "game", "win64", "win32", "x64", "x86" })
            {
                if (a.EndsWith(suffix, StringComparison.Ordinal) && a.Substring(0, a.Length - suffix.Length) == b) return true;
                if (b.EndsWith(suffix, StringComparison.Ordinal) && b.Substring(0, b.Length - suffix.Length) == a) return true;
            }

            return false;
        }

        public static string CommonParent(System.Collections.Generic.IEnumerable<string> paths)
        {
            string common = null;
            foreach (var path in paths)
            {
                var normalized = Normalize(path);
                if (normalized.Length == 0) continue;
                if (common == null)
                {
                    common = normalized;
                    continue;
                }

                while (common.Length > 0 && !IsUnder(normalized, common))
                {
                    var parent = Path.GetDirectoryName(common);
                    if (string.IsNullOrEmpty(parent) || parent == common) return string.Empty;
                    common = parent;
                }
            }
            return common ?? string.Empty;
        }
    }
}
