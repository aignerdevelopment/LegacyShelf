using System.Collections.Generic;

namespace LegacyShelf.Models
{
    public sealed class ArtworkManifest
    {
        public int Version { get; set; }
        public Dictionary<string, ArtworkEntry> Overrides { get; set; }

        public ArtworkManifest()
        {
            Version = 1;
            Overrides = new Dictionary<string, ArtworkEntry>();
        }
    }

    public sealed class ArtworkEntry
    {
        public string File { get; set; }
        public string Source { get; set; }
        public string Sha256 { get; set; }
        public string Url { get; set; }
        public double FocusX { get; set; }
        public double FocusY { get; set; }

        public ArtworkEntry()
        {
            FocusX = 0.5;
            FocusY = 0.5;
            Source = "unknown";
        }
    }
}
