using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ThemeStore.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThemeStore.Services
{
    public sealed class ThemeCatalogService
    {
        private const int MaxCatalogBytes = 2 * 1024 * 1024;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
        private readonly HttpClient _httpClient;
        private readonly ILogger<ThemeCatalogService> _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private string _cachedUrl = string.Empty;
        private ThemeCatalogResult _cachedCatalog;
        private DateTimeOffset _cachedAt;

        public ThemeCatalogService(ILogger<ThemeCatalogService> logger)
        {
            _logger = logger;
            _httpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                AllowAutoRedirect = false
            })
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-ThemeStore/1.0");
        }

        public async Task<ThemeCatalogResult> GetCatalogAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            string source = Plugin.Instance?.Configuration?.ThemeCatalogUrl ?? string.Empty;
            if (!TryValidatePublicHttpUrl(source, out Uri sourceUri, out string error))
                return new ThemeCatalogResult { SourceUrl = source, Warnings = new List<string> { error } };

            if (!forceRefresh && _cachedCatalog != null && string.Equals(_cachedUrl, source, StringComparison.Ordinal) && DateTimeOffset.UtcNow - _cachedAt < CacheLifetime)
                return _cachedCatalog;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!forceRefresh && _cachedCatalog != null && string.Equals(_cachedUrl, source, StringComparison.Ordinal) && DateTimeOffset.UtcNow - _cachedAt < CacheLifetime)
                    return _cachedCatalog;

                string content = await DownloadTextAsync(sourceUri, MaxCatalogBytes, cancellationToken).ConfigureAwait(false);
                ThemeCatalogResult parsed = ThemeCatalogParser.Parse(content, sourceUri);
                _cachedUrl = source;
                _cachedCatalog = parsed;
                _cachedAt = DateTimeOffset.UtcNow;
                return parsed;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException)
            {
                _logger.LogWarning(ex, "[ThemeStore] Failed to load theme catalog {CatalogUrl}.", source);
                if (_cachedCatalog != null && string.Equals(_cachedUrl, source, StringComparison.Ordinal))
                {
                    var stale = new ThemeCatalogResult
                    {
                        SourceUrl = _cachedCatalog.SourceUrl,
                        Themes = _cachedCatalog.Themes,
                        Warnings = new List<string>(_cachedCatalog.Warnings)
                    };
                    stale.Warnings.Add("The catalog could not be refreshed. Showing the last cached version.");
                    return stale;
                }

                return new ThemeCatalogResult { SourceUrl = source, Warnings = new List<string> { ex.Message } };
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<ThemeDefinition> FindThemeAsync(string themeId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(themeId))
                return null;

            ThemeCatalogResult catalog = await GetCatalogAsync(false, cancellationToken).ConfigureAwait(false);
            return catalog.Themes.FirstOrDefault(theme => string.Equals(theme.Id, themeId, StringComparison.OrdinalIgnoreCase));
        }

        public static bool TryValidatePublicHttpUrl(string value, out Uri uri, out string error)
        {
            uri = null;
            error = string.Empty;
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                error = "Only absolute HTTP or HTTPS catalog URLs are supported.";
                return false;
            }

            if (!string.IsNullOrEmpty(parsed.UserInfo) || parsed.IsLoopback || IsLocalHostName(parsed.Host))
            {
                error = "Loopback, local and credential-bearing URLs are not allowed.";
                return false;
            }

            if (IPAddress.TryParse(parsed.Host, out IPAddress address) && !IsPublicAddress(address))
            {
                error = "Private, link-local and unspecified IP addresses are not allowed.";
                return false;
            }

            uri = parsed;
            return true;
        }

        private async Task<string> DownloadTextAsync(Uri initialUri, int maxBytes, CancellationToken cancellationToken)
        {
            Uri current = initialUri;
            for (int redirect = 0; redirect <= 3; redirect++)
            {
                if (!TryValidatePublicHttpUrl(current.AbsoluteUri, out current, out string error))
                    throw new InvalidOperationException(error);

                IPAddress[] addresses = await Dns.GetHostAddressesAsync(current.Host, cancellationToken).ConfigureAwait(false);
                if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
                    throw new InvalidOperationException("The URL resolves to a private or unsupported address.");

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location != null)
                {
                    current = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength > maxBytes)
                    throw new InvalidOperationException($"The remote file exceeds the {maxBytes} byte size limit.");

                byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (bytes.Length > maxBytes)
                    throw new InvalidOperationException($"The remote file exceeds the {maxBytes} byte size limit.");

                return System.Text.Encoding.UTF8.GetString(bytes);
            }

            throw new InvalidOperationException("The remote URL redirected too many times.");
        }

        private static bool IsLocalHostName(string host)
            => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

        public static bool IsPublicAddress(IPAddress address)
        {
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None))
                return false;

            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            byte[] bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return !(bytes[0] == 10
                    || bytes[0] == 127
                    || bytes[0] == 0
                    || (bytes[0] == 169 && bytes[1] == 254)
                    || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                    || (bytes[0] == 192 && bytes[1] == 168)
                    || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                    || bytes[0] >= 224);
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return !(address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal || (bytes[0] & 0xFE) == 0xFC);
            }

            return false;
        }
    }
}
