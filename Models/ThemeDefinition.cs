using System.Collections.Generic;

namespace Jellyfin.Plugin.ThemeStore.Models
{
    public sealed class ThemeDefinition
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Author { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Version { get; set; } = "1";

        public string Jellyfin { get; set; } = string.Empty;

        public string SourceUrl { get; set; } = string.Empty;

        public string License { get; set; } = string.Empty;

        public string CssUrl { get; set; } = string.Empty;

        /// <summary>
        /// Ordered CSS sources for a complete theme variant. The first item is
        /// normally the base theme and later items are variants or add-ons.
        /// CssUrl remains populated with the first source for compatibility
        /// with older clients and catalogs.
        /// </summary>
        public List<string> CssUrls { get; set; } = new();

        public List<string> PreviewUrls { get; set; } = new();

        public List<string> Tags { get; set; } = new();

        public List<string> Preconnect { get; set; } = new();

        public List<ThemeVariableDefinition> Vars { get; set; } = new();
    }

    public sealed class ThemeVariableDefinition
    {
        public string Key { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Type { get; set; } = "text";

        public string Default { get; set; } = string.Empty;

        public bool AllowGradient { get; set; } = true;

        public List<ThemeVariableOption> Options { get; set; } = new();
    }

    public sealed class ThemeVariableOption
    {
        public string Name { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;
    }

    public sealed class ThemeCatalogResult
    {
        public string SourceUrl { get; set; } = string.Empty;

        public List<ThemeDefinition> Themes { get; set; } = new();

        public List<string> Warnings { get; set; } = new();
    }
}
