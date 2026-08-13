using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ThemeStore.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Tracks the config schema version. Incremented whenever new fields
        /// are added so the migration block in Plugin.cs can apply defaults
        /// safely without wiping the existing config.
        /// </summary>
        public int ConfigVersion { get; set; } = 0;

        /// <summary>
        /// When true, users can override the server-wide theme with their own
        /// choice via the Theme Store entry in Jellyfin's main drawer. Preferences
        /// are stored server-side per Jellyfin user id and follow the account
        /// across supported web-based clients.
        /// Added in config version 5.
        /// </summary>
        public bool AllowUserThemes { get; set; } = true;

        public string DefaultThemeMode { get; set; } = "Jellyfin";

        public string DefaultThemeId { get; set; } = string.Empty;

        public string DefaultThemeName { get; set; } = "Jellyfin Default";

        public string DefaultThemeVariablesJson { get; set; } = "{}";

        /// <summary>
        /// URL pointing to the skins repository JSON file.
        /// </summary>
        public string ThemeCatalogUrl { get; set; } = global::Jellyfin.Plugin.ThemeStore.Plugin.DefaultCatalogUrl;

        public string SkinUrl { get; set; } = string.Empty;

        /// <summary>
        /// The display name of the currently selected skin (e.g. "Ultrachromic").
        /// "Default" means no skin is applied.
        /// </summary>
        public string Skin { get; set; } = "Default";

        /// <summary>
        /// The direct URL to the selected skin's CSS file.
        /// This is what gets injected into index.html by SkinInjector.
        /// </summary>
        public string SelectedCssUrl { get; set; } = string.Empty;

        /// <summary>
        /// The version string of the currently selected skin (e.g. "25.12.31").
        /// Used as part of the browser cache key — when the theme author bumps
        /// the version in skins.json, the old cached CSS is automatically bypassed.
        /// Added in config version 3.
        /// </summary>
        public string SelectedVersion { get; set; } = string.Empty;

        /// <summary>
        /// Optional extra CSS the user wants applied on top of the selected skin.
        /// </summary>
        public string CustomCss { get; set; } = string.Empty;

        /// <summary>
        /// Variable values for the currently active theme, serialized as JSON.
        /// Structure: { "VAR_KEY": "value", ... }
        /// Injected as a :root { --var-key: value; } block after the fetched CSS.
        /// The theme CSS file should reference var(--var-key) for configurable values.
        /// Added in config version 2.
        /// </summary>
        public string ThemeVars { get; set; } = "{}";

        /// <summary>
        /// True if the selected theme CSS contains @sm-import-if addon comments.
        /// Scanned once at save time so the injector never needs to fetch
        /// CSS just to make a routing decision. Added in config version 4.
        /// </summary>
        public bool HasConditionalImports { get; set; } = false;

        /// <summary>
        /// Dynamic links (e.g. Google Fonts) to preconnect early for improved performance.
        /// Stored per server-theme.
        /// </summary>
        public List<string> PreconnectUrls { get; set; } = new();
    }
}
