using System.Collections.Generic;

namespace LegacyShelf.Models
{
    public sealed class TransportManifest
    {
        public int Version { get; set; }
        public string CreatedAt { get; set; }
        public string LegacyShelfVersion { get; set; }
        public int GameCount { get; set; }
        public long TotalBytes { get; set; }
        public bool IncludeLauncherData { get; set; }
        public string LauncherZip { get; set; }
        public List<TransportGameEntry> Games { get; set; }

        public TransportManifest()
        {
            Version = 1;
            Games = new List<TransportGameEntry>();
        }
    }

    public sealed class TransportGameEntry
    {
        public string Name { get; set; }
        public string FolderName { get; set; }
        public string Uuid { get; set; }
        public string RelativeDir { get; set; }
        public string ExeName { get; set; }
        public string ZipName { get; set; }
    }
}

