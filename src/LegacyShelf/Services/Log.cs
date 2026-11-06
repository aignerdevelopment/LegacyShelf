using System;
using System.IO;
using System.Text;

namespace LegacyShelf.Services
{
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;

        public static void Init(string path)
        {
            lock (Gate)
            {
                _path = path;
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    RollIfNeeded(path);
                }
                catch
                {
                    // Logging must never take the application down.
                    _path = null;
                }
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            var text = message;
            if (ex != null) text += Environment.NewLine + ex;
            Write("ERROR", text);
        }

        private static void Write(string level, string message)
        {
            var line = string.Format(
                "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}{3}",
                DateTime.Now,
                level,
                message,
                Environment.NewLine);

            lock (Gate)
            {
                if (string.IsNullOrEmpty(_path)) return;
                try
                {
                    RollIfNeeded(_path);
                    File.AppendAllText(_path, line, Encoding.UTF8);
                }
                catch
                {
                    // Ignore logging failures.
                }
            }
        }

        private static void RollIfNeeded(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length < 1024 * 1024) return;

                var old1 = path + ".1";
                var old2 = path + ".2";
                if (File.Exists(old2)) File.Delete(old2);
                if (File.Exists(old1)) File.Move(old1, old2);
                File.Move(path, old1);
            }
            catch
            {
                // Ignore.
            }
        }
    }
}
