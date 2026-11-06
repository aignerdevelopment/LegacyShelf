using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using LegacyShelf.Models;

namespace LegacyShelf.Services
{
    public sealed class ArtworkImportResult
    {
        public ArtworkManifest Manifest { get; set; }
        public int FromCache { get; set; }
        public int Fetched { get; set; }
        public int Icons { get; set; }
        public int Placeholders { get; set; }
    }

    public sealed class ArtworkImporter
    {
        private const int MaxImportDimension = 800;
        private const long MaxCacheFileBytes = 32L * 1024 * 1024;

        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly string[] FolderArtNames =
        {
            "cover.jpg", "cover.jpeg", "cover.png",
            "folder.jpg", "folder.jpeg", "folder.png",
            "poster.jpg", "poster.jpeg", "poster.png",
            "boxart.jpg", "boxart.jpeg", "boxart.png",
            "artwork.jpg", "artwork.jpeg", "artwork.png"
        };

        private readonly CoverIndexService _index;

        public ArtworkImporter(CoverIndexService index)
        {
            _index = index;
        }

        public ArtworkImportResult Import(
            IList<Game> games,
            string launcherDir,
            IDictionary<string, CatalogGameInfo> catalog,
            bool fetchMissing,
            IProgress<ScanProgress> progress,
            CancellationToken token)
        {
            var result = new ArtworkImportResult
            {
                Manifest = LoadManifest()
            };

            var hashToCache = BuildCacheHashIndex(launcherDir, token);
            var total = Math.Max(1, games.Count);
            var done = 0;

            foreach (var game in games)
            {
                token.ThrowIfCancellationRequested();
                done++;

                var percent = 30 + (int)(55.0 * done / total);
                if (progress != null)
                {
                    progress.Report(new ScanProgress(percent, "Artwork: " + game.Name));
                }

                try
                {
                    ImportOne(game, result, hashToCache, catalog, fetchMissing, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Warn("Artwork failed for '" + game.Name + "': " + ex.Message);
                    game.ArtworkSource = "placeholder";
                    game.CoverPath = null;
                    result.Placeholders++;
                }
            }

            SaveManifest(result.Manifest);
            Log.Info(string.Format(
                "Artwork import: {0} from cache, {1} fetched, {2} icons, {3} placeholders.",
                result.FromCache, result.Fetched, result.Icons, result.Placeholders));

            return result;
        }

        public ArtworkEntry SetOverride(Game game, string sourceFile, ArtworkManifest manifest)
        {
            if (manifest == null) manifest = LoadManifest();
            var bytes = File.ReadAllBytes(sourceFile);
            var sha = Sha256(bytes);
            var entry = ImportBytes(game, bytes, "override", null, sha, false);
            manifest.Overrides[game.Id] = entry;
            SaveManifest(manifest);

            game.CoverPath = Path.Combine(AppPaths.ArtworkDir, entry.File);
            game.ArtworkSource = "override";
            return entry;
        }

        private void ImportOne(
            Game game,
            ArtworkImportResult result,
            IDictionary<string, string> hashToCache,
            IDictionary<string, CatalogGameInfo> catalog,
            bool fetchMissing,
            CancellationToken token)
        {
            ArtworkEntry existing;
            if (result.Manifest.Overrides.TryGetValue(game.Id, out existing) && existing != null)
            {
                var existingPath = ResolveArtwork(existing.File);
                var isOverride = string.Equals(existing.Source, "override", StringComparison.OrdinalIgnoreCase);
                if (existingPath != null && (isOverride || !string.Equals(existing.Source, "icon", StringComparison.OrdinalIgnoreCase)))
                {
                    game.CoverPath = existingPath;
                    game.ArtworkSource = existing.Source;
                    if (string.Equals(existing.Source, "cache", StringComparison.OrdinalIgnoreCase)) result.FromCache++;
                    if (string.Equals(existing.Source, "fetch", StringComparison.OrdinalIgnoreCase)) result.Fetched++;
                    return;
                }
            }

            var folderArt = FindFolderArt(game.InstallDir);
            if (folderArt != null)
            {
                var entry = ImportBytes(game, File.ReadAllBytes(folderArt), "folder", null, Sha256File(folderArt), false);
                result.Manifest.Overrides[game.Id] = entry;
                game.CoverPath = Path.Combine(AppPaths.ArtworkDir, entry.File);
                game.ArtworkSource = "folder";
                return;
            }

            CoverIndexEntry indexEntry = _index.FindByUuid(game.Uuid);
            if (indexEntry != null && !string.IsNullOrWhiteSpace(indexEntry.Sha256))
            {
                string cacheFile;
                if (hashToCache.TryGetValue(indexEntry.Sha256, out cacheFile) && File.Exists(cacheFile))
                {
                    var entry = ImportBytes(game, File.ReadAllBytes(cacheFile), "cache", indexEntry.Url, indexEntry.Sha256, false);
                    result.Manifest.Overrides[game.Id] = entry;
                    game.CoverPath = Path.Combine(AppPaths.ArtworkDir, entry.File);
                    game.ArtworkSource = "cache";
                    result.FromCache++;
                    return;
                }
            }

            if (fetchMissing)
            {
                var url = indexEntry != null && !string.IsNullOrWhiteSpace(indexEntry.Url)
                    ? indexEntry.Url
                    : game.CoverUrl;

                CatalogGameInfo info;
                if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(game.Uuid) &&
                    catalog != null && catalog.TryGetValue(game.Uuid, out info))
                {
                    url = info.CoverUrl;
                }

                if (!string.IsNullOrWhiteSpace(url))
                {
                    var bytes = TryDownload(url, token);
                    if (bytes != null)
                    {
                        var sha = Sha256(bytes);
                        var entry = ImportBytes(game, bytes, "fetch", url, sha, false);
                        result.Manifest.Overrides[game.Id] = entry;
                        game.CoverPath = Path.Combine(AppPaths.ArtworkDir, entry.File);
                        game.ArtworkSource = "fetch";
                        result.Fetched++;

                        _index.Remember(new CoverIndexEntry
                        {
                            Uuid = game.Uuid,
                            Name = game.Name,
                            Url = url,
                            Sha256 = sha
                        });
                        return;
                    }
                }
            }

            if (TryImportIcon(game, out var iconEntry))
            {
                result.Manifest.Overrides[game.Id] = iconEntry;
                game.CoverPath = Path.Combine(AppPaths.ArtworkDir, iconEntry.File);
                game.ArtworkSource = "icon";
                result.Icons++;
                return;
            }

            result.Manifest.Overrides[game.Id] = new ArtworkEntry
            {
                File = null,
                Source = "placeholder"
            };
            game.CoverPath = null;
            game.ArtworkSource = "placeholder";
            result.Placeholders++;
        }

        private bool TryImportIcon(Game game, out ArtworkEntry entry)
        {
            entry = null;
            Bitmap bitmap = null;

            try
            {
                var iconPath = Path.Combine(game.InstallDir ?? string.Empty, "icon.ico");
                if (File.Exists(iconPath))
                {
                    using (var icon = new Icon(iconPath, 256, 256))
                    {
                        bitmap = icon.ToBitmap();
                    }
                }

                if (bitmap == null && !string.IsNullOrWhiteSpace(game.ExePath) && File.Exists(game.ExePath))
                {
                    using (var icon = Icon.ExtractAssociatedIcon(game.ExePath))
                    {
                        if (icon != null) bitmap = icon.ToBitmap();
                    }
                }

                if (bitmap == null) return false;

                using (bitmap)
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Png);
                    entry = ImportBytes(game, ms.ToArray(), "icon", null, null, true);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not import icon for '" + game.Name + "': " + ex.Message);
                if (bitmap != null) bitmap.Dispose();
                return false;
            }
        }

        private ArtworkEntry ImportBytes(
            Game game,
            byte[] bytes,
            string source,
            string url,
            string sha,
            bool forcePng)
        {
            Directory.CreateDirectory(AppPaths.ArtworkDir);
            var stem = PathUtil.SafeFileStem(game.Id);
            var extension = forcePng ? ".png" : ".jpg";

            using (var input = new MemoryStream(bytes))
            using (var image = Image.FromStream(input, false, true))
            {
                if (!forcePng && Image.IsAlphaPixelFormat(image.PixelFormat)) extension = ".png";

                var scale = Math.Min(1.0, MaxImportDimension / (double)Math.Max(image.Width, image.Height));
                var width = Math.Max(1, (int)Math.Round(image.Width * scale));
                var height = Math.Max(1, (int)Math.Round(image.Height * scale));

                using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CompositingMode = CompositingMode.SourceOver;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.Clear(Color.Transparent);
                    graphics.DrawImage(image, 0, 0, width, height);

                    var dest = Path.Combine(AppPaths.ArtworkDir, stem + extension);
                    var temp = dest + ".tmp";
                    if (File.Exists(temp)) File.Delete(temp);

                    if (extension == ".png")
                    {
                        bitmap.Save(temp, ImageFormat.Png);
                    }
                    else
                    {
                        var codec = GetEncoder(ImageFormat.Jpeg);
                        using (var parameters = new EncoderParameters(1))
                        {
                            parameters.Param[0] = new EncoderParameter(Encoder.Quality, 92L);
                            bitmap.Save(temp, codec, parameters);
                        }
                    }

                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(temp, dest);
                }
            }

            // Remove an old copy with the other extension.
            var other = Path.Combine(AppPaths.ArtworkDir, stem + (extension == ".png" ? ".jpg" : ".png"));
            try
            {
                if (File.Exists(other)) File.Delete(other);
            }
            catch
            {
                // Ignore.
            }

            return new ArtworkEntry
            {
                File = stem + extension,
                Source = source,
                Url = url,
                Sha256 = sha,
                FocusX = 0.5,
                FocusY = 0.5
            };
        }

        private static IDictionary<string, string> BuildCacheHashIndex(string launcherDir, CancellationToken token)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(launcherDir)) return map;

            var cacheDir = Path.Combine(launcherDir, "Cache", "Cache_Data");
            if (!Directory.Exists(cacheDir))
            {
                Log.Warn("No Cache\\Cache_Data directory under " + launcherDir);
                return map;
            }

            try
            {
                foreach (var file in Directory.GetFiles(cacheDir, "f_*"))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length <= 0 || info.Length > MaxCacheFileBytes) continue;

                        var bytes = File.ReadAllBytes(file);
                        if (!IsImage(bytes)) continue;

                        var sha = Sha256(bytes);
                        if (!map.ContainsKey(sha)) map[sha] = file;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Skipping cache file '" + file + "': " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not scan cache artwork: " + ex.Message);
            }

            Log.Info("Artwork cache: hashed " + map.Count + " image file(s).");
            return map;
        }

        private static string FindFolderArt(string installDir)
        {
            if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) return null;
            foreach (var name in FolderArtNames)
            {
                var path = Path.Combine(installDir, name);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private static string ResolveArtwork(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;
            var path = Path.Combine(AppPaths.ArtworkDir, file);
            return File.Exists(path) ? path : null;
        }

        private static ArtworkManifest LoadManifest()
        {
            try
            {
                var manifest = JsonUtil.Load<ArtworkManifest>(AppPaths.ArtworkManifestPath);
                if (manifest == null) manifest = new ArtworkManifest();
                if (manifest.Overrides == null)
                {
                    manifest.Overrides = new Dictionary<string, ArtworkEntry>();
                }
                return manifest;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read artwork.json; starting fresh: " + ex.Message);
                return new ArtworkManifest();
            }
        }

        private static void SaveManifest(ArtworkManifest manifest)
        {
            try
            {
                JsonUtil.Save(AppPaths.ArtworkManifestPath, manifest, pretty: true);
            }
            catch (Exception ex)
            {
                Log.Error("Could not save artwork.json.", ex);
            }
        }

        private static byte[] TryDownload(string url, CancellationToken token)
        {
            try
            {
                Log.Info("Fetching cover: " + url);
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.UserAgent.ParseAdd("LegacyShelf/0.1");
                    using (var response = Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token)
                        .GetAwaiter().GetResult())
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            Log.Warn("Cover fetch returned " + (int)response.StatusCode + ": " + url);
                            return null;
                        }

                        var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                        if (bytes == null || bytes.Length == 0 || !IsImage(bytes))
                        {
                            Log.Warn("Cover fetch returned a non-image: " + url);
                            return null;
                        }
                        return bytes;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn("Cover fetch failed for '" + url + "': " + ex.Message);
                return null;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
                // Ignore.
            }

            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(25);
            return client;
        }

        public static bool IsImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12) return false;
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return true;
            if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true;
            if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00) return true;
            if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return true;
            return false;
        }

        public static string Sha256(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return ToHex(sha.ComputeHash(bytes));
            }
        }

        public static string Sha256File(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var chars = new char[bytes.Length * 2];
            const string hex = "0123456789abcdef";
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = hex[bytes[i] >> 4];
                chars[i * 2 + 1] = hex[bytes[i] & 0xF];
            }
            return new string(chars);
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            return ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(codec => codec.FormatID == format.Guid);
        }
    }
}

