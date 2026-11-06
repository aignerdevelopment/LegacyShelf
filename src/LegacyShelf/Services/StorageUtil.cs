using System;
using System.IO;

namespace LegacyShelf.Services
{
    public static class StorageUtil
    {
        /// <summary>
        /// Returns available free bytes, or 0 when the volume cannot be inspected (UNC, etc.).
        /// </summary>
        public static long GetAvailableFreeSpace(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return 0;
                var root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrWhiteSpace(root)) return 0;
                var drive = new DriveInfo(root);
                return drive.IsReady ? drive.AvailableFreeSpace : 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public sealed class NotEnoughSpaceException : IOException
    {
        public long RequiredBytes { get; private set; }
        public long AvailableBytes { get; private set; }
        public string TargetPath { get; private set; }

        public NotEnoughSpaceException(
            string message,
            string targetPath,
            long requiredBytes,
            long availableBytes)
            : base(message)
        {
            TargetPath = targetPath;
            RequiredBytes = requiredBytes;
            AvailableBytes = availableBytes;
        }
    }
}
