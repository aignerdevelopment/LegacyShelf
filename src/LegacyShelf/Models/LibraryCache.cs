using System.Collections.Generic;

namespace LegacyShelf.Models
{
    public sealed class LibraryCache
    {
        public string ScannedAt { get; set; }
        public List<string> Roots { get; set; }
        public List<Game> Games { get; set; }

        public LibraryCache()
        {
            Roots = new List<string>();
            Games = new List<Game>();
        }
    }
}
