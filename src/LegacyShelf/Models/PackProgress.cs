using System;

namespace LegacyShelf.Models
{
    public sealed class PackProgress
    {
        public long BytesDone { get; set; }
        public long BytesTotal { get; set; }
        public int FilesDone { get; set; }
        public int FilesTotal { get; set; }
        public string Phase { get; set; }
        public string CurrentFile { get; set; }

        public int Percent
        {
            get
            {
                if (BytesTotal <= 0) return 0;
                return Math.Max(0, Math.Min(100, (int)(BytesDone * 100L / BytesTotal)));
            }
        }

        public PackProgress()
        {
            Phase = string.Empty;
            CurrentFile = string.Empty;
        }
    }

    public sealed class PackResult
    {
        public string Destination { get; set; }
        public int GameCount { get; set; }
        public long TotalBytes { get; set; }
        public bool IncludedLauncherData { get; set; }
        public string LauncherZip { get; set; }
    }
}

