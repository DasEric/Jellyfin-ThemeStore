using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThemeStore.Services
{
    public static class SkinResourceProxy
    {
        private const int MaxCssBytes = 5 * 1024 * 1024;
        private const string CacheFormatVersion = "2";
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly SemaphoreSlim Semaphore = new(1, 1);
        private static long _cacheGeneration;

        public static long CacheGeneration => Interlocked.Read(ref _cacheGeneration);

        public static async Task<string> GetResourceAsync(string url, string version = null, ILogger logger = null, CancellationToken cancellationToken = default)
        {
            if (!ThemeCatalogService.TryValidatePublicHttpUrl(url, out Uri uri, out string validationError))
            {
                logger?.LogWarning("[ThemeStore] Blocked CSS URL {Url}: {Reason}", url, validationError);
                return string.Empty;
            }

            try
            {
                string cacheDir = Path.Combine(Plugin.Instance.DataFolderPath, "Cache");
                Directory.CreateDirectory(cacheDir);
                string hash = GetHashString(CacheFormatVersion + "|" + url + "|" + (version ?? "0"));
                string filePath = Path.Combine(cacheDir, hash + ".css");
                if (IsFresh(filePath))
                    return await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);

                await Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (IsFresh(filePath))
                        return await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);

                    string content = await DownloadCssAsync(uri, cancellationToken).ConfigureAwait(false);
                    string temporaryPath = filePath + ".tmp";
                    await File.WriteAllTextAsync(temporaryPath, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
                    File.Move(temporaryPath, filePath, true);
                    return content;
                }
                finally
                {
                    Semaphore.Release();
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is TaskCanceledException || ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is SocketException)
            {
                logger?.LogError(ex, "[ThemeStore] Could not fetch CSS resource {Url}.", url);
                return string.Empty;
            }
        }

        public static async Task ClearCacheAsync(ILogger logger = null, CancellationToken cancellationToken = default)
        {
            await Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string dataFolder = Plugin.Instance?.DataFolderPath;
                if (string.IsNullOrWhiteSpace(dataFolder))
                    return;

                string cacheDir = Path.Combine(dataFolder, "Cache");
                if (!Directory.Exists(cacheDir))
                    return;

                foreach (string file in Directory.EnumerateFiles(cacheDir, "*.css*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException)
                    {
                        // A concurrent request may still be reading the cache entry.
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                logger?.LogWarning(ex, "[ThemeStore] Could not completely clear the CSS cache.");
            }
            finally
            {
                Interlocked.Increment(ref _cacheGeneration);
                Semaphore.Release();
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                AllowAutoRedirect = false
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-ThemeStore/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/css,*/*;q=0.5");
            return client;
        }

        private static async Task<string> DownloadCssAsync(Uri initialUri, CancellationToken cancellationToken)
        {
            Uri current = initialUri;
            for (int redirect = 0; redirect <= 3; redirect++)
            {
                if (!ThemeCatalogService.TryValidatePublicHttpUrl(current.AbsoluteUri, out current, out string error))
                    throw new InvalidOperationException(error);

                IPAddress[] addresses = await Dns.GetHostAddressesAsync(current.Host, cancellationToken).ConfigureAwait(false);
                if (addresses.Length == 0 || addresses.Any(address => !ThemeCatalogService.IsPublicAddress(address)))
                    throw new InvalidOperationException("The CSS URL resolves to a private or unsupported address.");

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location != null)
                {
                    current = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxCssBytes)
                    throw new InvalidOperationException("The CSS file is larger than 5 MiB.");

                byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (bytes.Length > MaxCssBytes)
                    throw new InvalidOperationException("The CSS file is larger than 5 MiB.");

                return CssUrlRewriter.Rewrite(Encoding.UTF8.GetString(bytes), current);
            }

            throw new InvalidOperationException("The CSS URL redirected too many times.");
        }

        private static string GetHashString(string input)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static bool IsFresh(string path)
            => File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < CacheLifetime;
    }
}
