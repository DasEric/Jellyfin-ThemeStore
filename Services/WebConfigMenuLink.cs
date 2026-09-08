using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.ThemeStore.Services
{
    /// <summary>Registers a custom link in Jellyfin 12's native web navigation.</summary>
    public static class WebConfigMenuLink
    {
        private const int LockTimeoutSeconds = 5;

        public static bool UpdateFile(string path, string name, string icon, string url, bool enabled)
        {
            if (!File.Exists(path))
                return false;

            string fullPath = Path.GetFullPath(path);
            string mutexName = "JellyfinPluginMenuLinks-" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
            using var mutex = new Mutex(false, mutexName);
            bool ownsMutex = false;
            try
            {
                try
                {
                    ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(LockTimeoutSeconds));
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                if (!ownsMutex)
                    throw new IOException("Timed out while waiting to update Jellyfin web navigation.");

                string original = File.ReadAllText(fullPath);
                string updated = ApplyToJson(original, name, icon, url, enabled);
                if (string.Equals(original, updated, StringComparison.Ordinal))
                    return false;

                string directory = Path.GetDirectoryName(fullPath)
                    ?? throw new IOException("The Jellyfin web configuration path has no parent directory.");
                string temporary = Path.Combine(directory, Path.GetRandomFileName());
                try
                {
                    File.WriteAllText(temporary, updated);
                    File.Move(temporary, fullPath, true);
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }

                return true;
            }
            finally
            {
                if (ownsMutex)
                    mutex.ReleaseMutex();
            }
        }

        public static string ApplyToJson(string source, string name, string icon, string url, bool enabled)
        {
            JObject root = JObject.Parse(source);
            JToken menuLinksToken = root["menuLinks"];
            if (menuLinksToken != null && menuLinksToken.Type != JTokenType.Null && menuLinksToken is not JArray)
                throw new InvalidDataException("Jellyfin web config property 'menuLinks' is not an array.");

            JArray menuLinks = menuLinksToken as JArray;
            JObject[] matches = menuLinks?
                .OfType<JObject>()
                .Where(item => string.Equals((string)item["url"], url, StringComparison.Ordinal))
                .ToArray() ?? Array.Empty<JObject>();

            if (!enabled)
            {
                if (matches.Length == 0)
                    return source;

                foreach (JObject match in matches)
                    match.Remove();

                return root.ToString(Formatting.Indented) + Environment.NewLine;
            }

            var desired = new JObject
            {
                ["name"] = name,
                ["icon"] = icon,
                ["url"] = url,
            };

            if (matches.Length == 1 && JToken.DeepEquals(matches[0], desired))
                return source;

            menuLinks ??= new JArray();
            if (menuLinksToken == null || menuLinksToken.Type == JTokenType.Null)
                root["menuLinks"] = menuLinks;

            int insertionIndex = matches.Length > 0 ? menuLinks.IndexOf(matches[0]) : menuLinks.Count;
            foreach (JObject match in matches)
                match.Remove();

            menuLinks.Insert(Math.Min(insertionIndex, menuLinks.Count), desired);
            return root.ToString(Formatting.Indented) + Environment.NewLine;
        }
    }
}
