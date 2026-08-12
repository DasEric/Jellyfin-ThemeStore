using System;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ThemeStore.Services
{
    public static class CssUrlRewriter
    {
        private static readonly Regex UrlFunction = new(
            @"url\(\s*(?<quote>['""]?)(?<url>[^'""\)]+)\k<quote>\s*\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex BareImport = new(
            @"@import\s+(?<quote>['""])(?<url>[^'""]+)\k<quote>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Rewrite(string css, Uri sourceUri)
        {
            if (string.IsNullOrEmpty(css) || sourceUri == null)
                return css ?? string.Empty;

            css = UrlFunction.Replace(css, match =>
            {
                string original = match.Groups["url"].Value.Trim();
                string resolved = Resolve(original, sourceUri);
                return resolved == null ? match.Value : $"url(\"{resolved}\")";
            });

            return BareImport.Replace(css, match =>
            {
                string original = match.Groups["url"].Value.Trim();
                string resolved = Resolve(original, sourceUri);
                return resolved == null ? match.Value : $"@import \"{resolved}\"";
            });
        }

        private static string Resolve(string value, Uri sourceUri)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.StartsWith("#", StringComparison.Ordinal)
                || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
                || value.Contains("{{", StringComparison.Ordinal))
                return null;

            if (!Uri.TryCreate(sourceUri, value, out Uri resolved))
                return null;

            return resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps
                ? resolved.AbsoluteUri
                : null;
        }
    }
}
