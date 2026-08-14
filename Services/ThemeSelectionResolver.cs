using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.ThemeStore.Configuration;
using Jellyfin.Plugin.ThemeStore.Models;

namespace Jellyfin.Plugin.ThemeStore.Services
{
    public static class ThemeSelectionResolver
    {
        public static bool RequiresCatalog(PluginConfiguration configuration, UserThemePreference preference)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            return string.Equals(configuration.DefaultThemeMode, "Catalog", StringComparison.OrdinalIgnoreCase)
                || (configuration.AllowUserThemes && !string.IsNullOrWhiteSpace(preference?.ThemeId));
        }

        public static ResolvedThemeSelection Resolve(
            PluginConfiguration configuration,
            UserThemePreference preference,
            IReadOnlyList<ThemeDefinition> themes)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            preference ??= new UserThemePreference();
            themes ??= Array.Empty<ThemeDefinition>();

            ThemeDefinition personalTheme = configuration.AllowUserThemes
                ? FindTheme(themes, preference.ThemeId)
                : null;
            if (personalTheme != null)
                return CreateCatalogSelection(personalTheme, preference.Variables, configuration, preference);

            if (string.Equals(configuration.DefaultThemeMode, "CustomCss", StringComparison.OrdinalIgnoreCase))
            {
                return new ResolvedThemeSelection
                {
                    ThemeId = "custom",
                    Version = "custom",
                    Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    StateToken = ComputeToken(configuration, preference, "custom", "custom", Array.Empty<string>(), null)
                };
            }

            if (string.Equals(configuration.DefaultThemeMode, "Catalog", StringComparison.OrdinalIgnoreCase))
            {
                ThemeDefinition defaultTheme = FindTheme(themes, configuration.DefaultThemeId);
                if (defaultTheme == null && string.IsNullOrWhiteSpace(configuration.DefaultThemeId))
                    defaultTheme = FindLegacyDefaultTheme(themes, configuration.SelectedCssUrl, configuration.DefaultThemeName);
                if (defaultTheme != null)
                {
                    Dictionary<string, string> variables = ParseVariables(configuration.DefaultThemeVariablesJson);
                    return CreateCatalogSelection(defaultTheme, variables, configuration, preference);
                }
            }

            return new ResolvedThemeSelection
            {
                ThemeId = string.Empty,
                Version = "1",
                Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                StateToken = ComputeToken(configuration, preference, string.Empty, "1", Array.Empty<string>(), null)
            };
        }

        private static ResolvedThemeSelection CreateCatalogSelection(
            ThemeDefinition theme,
            IDictionary<string, string> variables,
            PluginConfiguration configuration,
            UserThemePreference preference)
        {
            var safeVariables = variables != null
                ? new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> cssUrls = theme.CssUrls != null && theme.CssUrls.Count > 0
                ? theme.CssUrls
                : new[] { theme.CssUrl ?? string.Empty };
            string version = string.IsNullOrWhiteSpace(theme.Version) ? "1" : theme.Version;
            return new ResolvedThemeSelection
            {
                ThemeId = theme.Id,
                Version = version,
                Variables = safeVariables,
                StateToken = ComputeToken(configuration, preference, theme.Id, version, cssUrls, safeVariables)
            };
        }

        private static ThemeDefinition FindTheme(IEnumerable<ThemeDefinition> themes, string id)
            => string.IsNullOrWhiteSpace(id)
                ? null
                : themes.FirstOrDefault(theme => string.Equals(theme.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

        private static ThemeDefinition FindLegacyDefaultTheme(IEnumerable<ThemeDefinition> themes, string cssUrl, string name)
        {
            string selectedUrl = cssUrl?.Trim() ?? string.Empty;
            if (selectedUrl.Length > 0 && !string.Equals(selectedUrl, "Default", StringComparison.OrdinalIgnoreCase))
            {
                ThemeDefinition byUrl = themes.FirstOrDefault(theme =>
                    string.Equals(theme.CssUrl, selectedUrl, StringComparison.OrdinalIgnoreCase)
                    || (theme.CssUrls?.Any(url => string.Equals(url, selectedUrl, StringComparison.OrdinalIgnoreCase)) ?? false));
                if (byUrl != null)
                    return byUrl;
            }

            string selectedName = name?.Trim() ?? string.Empty;
            return selectedName.Length == 0 || string.Equals(selectedName, "Jellyfin Default", StringComparison.OrdinalIgnoreCase)
                ? null
                : themes.FirstOrDefault(theme => string.Equals(theme.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        }

        private static Dictionary<string, string> ParseVariables(string json)
        {
            try
            {
                return string.IsNullOrWhiteSpace(json)
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (System.Text.Json.JsonException)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string ComputeToken(
            PluginConfiguration configuration,
            UserThemePreference preference,
            string activeId,
            string version,
            IEnumerable<string> cssUrls,
            IDictionary<string, string> variables)
        {
            var input = new StringBuilder();
            input.Append(configuration.AllowUserThemes.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append(configuration.DefaultThemeMode ?? string.Empty).Append('\n')
                .Append(configuration.DefaultThemeId ?? string.Empty).Append('\n')
                .Append(configuration.CustomCss ?? string.Empty).Append('\n')
                .Append(SkinResourceProxy.CacheGeneration.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append(preference.ThemeId ?? string.Empty).Append('\n')
                .Append(activeId ?? string.Empty).Append('\n')
                .Append(version ?? string.Empty).Append('\n');
            foreach (string cssUrl in cssUrls ?? Array.Empty<string>())
                input.Append(cssUrl ?? string.Empty).Append('\n');
            foreach (KeyValuePair<string, string> pair in (variables ?? new Dictionary<string, string>()).OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                input.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString()));
            return Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
        }
    }

    public sealed class ResolvedThemeSelection
    {
        public string ThemeId { get; set; } = string.Empty;

        public string Version { get; set; } = "1";

        public Dictionary<string, string> Variables { get; set; } = new();

        public string StateToken { get; set; } = string.Empty;
    }
}
