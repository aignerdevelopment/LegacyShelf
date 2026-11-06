using System.Collections.Generic;

namespace LegacyShelf.Models
{
    public sealed class PortableGamesManifest
    {
        public int Version { get; set; }
        public string CreatedAt { get; set; }
        public List<PortableGameEntry> Games { get; set; }

        public PortableGamesManifest()
        {
            Version = 1;
            Games = new List<PortableGameEntry>();
        }
    }

    public sealed class PortableGameEntry
    {
        public string Id { get; set; }
        public string Uuid { get; set; }
        public string Name { get; set; }
        public string FolderName { get; set; }
        public string RelativeDir { get; set; }
        public string ExeName { get; set; }
        public string ZipName { get; set; }
    }
}

