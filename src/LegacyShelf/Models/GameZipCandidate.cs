namespace LegacyShelf.Models
{
    public sealed class GameZipCandidate
    {
        public string ZipPath { get; set; }
        public string ZipName { get; set; }
        public PortableGameEntry Entry { get; set; }
        public string TargetDir { get; set; }
        public long ZipBytes { get; set; }

        public string DisplayName
        {
            get
            {
                if (Entry != null && !string.IsNullOrWhiteSpace(Entry.Name)) return Entry.Name;
                if (Entry != null && !string.IsNullOrWhiteSpace(Entry.FolderName)) return Entry.FolderName;
                return ZipName;
            }
        }

        public string FolderName
        {
            get
            {
                if (Entry != null && !string.IsNullOrWhiteSpace(Entry.RelativeDir)) return Entry.RelativeDir;
                if (Entry != null && !string.IsNullOrWhiteSpace(Entry.FolderName)) return Entry.FolderName;
                return ZipName;
            }
        }
    }
}
