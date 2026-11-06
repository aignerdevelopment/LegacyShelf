using System.Collections.Generic;

namespace LegacyShelf.Models
{
    public sealed class CoverIndex
    {
        public int Version { get; set; }
        public string Source { get; set; }
        public List<CoverIndexEntry> Covers { get; set; }

        public CoverIndex()
        {
            Version = 1;
            Covers = new List<CoverIndexEntry>();
        }
    }

    public sealed class CoverIndexEntry
    {
        public string Uuid { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string Sha256 { get; set; }
    }

    public sealed class CatalogGameInfo
    {
        public string Uuid { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string CoverUrl { get; set; }
        public string InstalledSize { get; set; }
    }
}
