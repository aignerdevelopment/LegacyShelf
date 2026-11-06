using System.Collections.Generic;

namespace LegacyShelf.Models
{
    public sealed class ScanResult
    {
        public string ScannedAt { get; set; }
        public List<string> Roots { get; set; }
        public List<Game> Games { get; set; }
        public ArtworkImportResultSummary Artwork { get; set; }

        public ScanResult()
        {
            Roots = new List<string>();
            Games = new List<Game>();
            Artwork = new ArtworkImportResultSummary();
        }
    }

    public sealed class ArtworkImportResultSummary
    {
        public int FromCache { get; set; }
        public int Fetched { get; set; }
        public int Icons { get; set; }
        public int Placeholders { get; set; }
        public int Total
        {
            get { return FromCache + Fetched + Icons + Placeholders; }
        }
    }
}
