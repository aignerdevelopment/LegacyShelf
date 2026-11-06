using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace LegacyShelf.Services
{
    public static class JsonUtil
    {
        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 256
            };
        }

        public static T Load<T>(string path) where T : class
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text)) return null;
            return CreateSerializer().Deserialize<T>(text);
        }

        public static T Load<T>(Stream stream) where T : class
        {
            if (stream == null) return null;
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                var text = reader.ReadToEnd();
                if (string.IsNullOrWhiteSpace(text)) return null;
                return CreateSerializer().Deserialize<T>(text);
            }
        }

        public static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            return CreateSerializer().Deserialize<T>(json);
        }

        public static void Save<T>(string path, T value, bool pretty = false)
        {
            var json = CreateSerializer().Serialize(value);
            if (pretty) json = Format(json);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, null);
                }
                catch
                {
                    // Some non-NTFS volumes do not support ReplaceFile.
                    File.Copy(tmp, path, true);
                    File.Delete(tmp);
                }
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        public static string Serialize(object value, bool pretty = false)
        {
            var json = CreateSerializer().Serialize(value);
            return pretty ? Format(json) : json;
        }

        public static T Clone<T>(T value) where T : class
        {
            var json = CreateSerializer().Serialize(value);
            return CreateSerializer().Deserialize<T>(json);
        }

        /// <summary>
        /// Lightweight pretty-printer for JavaScriptSerializer output.
        /// Leaves string contents untouched.
        /// </summary>
        public static string Format(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;

            var sb = new StringBuilder(json.Length + json.Length / 4);
            var indent = 0;
            var inString = false;
            var escaped = false;

            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];

                if (inString)
                {
                    sb.Append(c);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        sb.Append(c);
                        break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        indent++;
                        sb.AppendLine();
                        sb.Append(' ', indent * 2);
                        break;
                    case '}':
                    case ']':
                        indent = Math.Max(0, indent - 1);
                        sb.AppendLine();
                        sb.Append(' ', indent * 2);
                        sb.Append(c);
                        break;
                    case ',':
                        sb.Append(c);
                        sb.AppendLine();
                        sb.Append(' ', indent * 2);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        if (!char.IsWhiteSpace(c)) sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
