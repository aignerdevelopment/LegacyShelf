using System;

namespace LegacyShelf.Models
{
    public sealed class Game
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string FolderName { get; set; }
        public string InstallDir { get; set; }
        public string ExeName { get; set; }
        public string ExePath { get; set; }
        public string Uuid { get; set; }
        public string CatalogName { get; set; }
        public string Description { get; set; }
        public string InstalledSize { get; set; }
        public string Status { get; set; }
        public string CoverPath { get; set; }
        public string ArtworkSource { get; set; }
        public string CoverUrl { get; set; }
        public string LastPlayedUtc { get; set; }
        public long PlaySeconds { get; set; }

        public bool IsMissing
        {
            get { return string.Equals(Status, "missing", StringComparison.OrdinalIgnoreCase); }
        }

        public string SortName
        {
            get { return string.IsNullOrWhiteSpace(Name) ? FolderName : Name; }
        }
    }
}
