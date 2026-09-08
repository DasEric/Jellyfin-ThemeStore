using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.ThemeStore.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThemeStore
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <summary>
        /// The current config schema version. Increment this whenever a new
        /// field is added to PluginConfiguration, then add a migration case
        /// in the switch block below to apply safe defaults for that version.
        /// </summary>
        private const int CurrentConfigVersion = 7;
        private const string MenuLinkUrl = "#/theme-store";
        private readonly IApplicationPaths _applicationPaths;

        public const string DefaultCatalogUrl = "https://daseric.github.io/Jellyfin-ThemeStore/catalog.json";

        private const string LegacyCatalogUrl = "https://raw.githubusercontent.com/Jellyfin-PG/Skin-Manager-Themes/refs/heads/main/skins.json";

        public override string Name => "Theme Store";

        public override Guid Id => Guid.Parse("4e75cc9d-4fcf-473a-bf35-e61484324d12");

        public override string Description => "Per-user CSS theme store for Jellyfin Web.";

        public static Plugin Instance { get; private set; }

        public IServiceProvider ServiceProvider { get; private set; }

        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILogger<Plugin> logger,
            IServiceProvider serviceProvider)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _applicationPaths = applicationPaths;
            ServiceProvider = serviceProvider;
            MigrateConfig();
            UpdateIndexHtml(true);
            UpdateMenuLink(Configuration.AllowUserThemes);
        }

        /// <summary>
        /// Applies incremental migrations for any config version behind the current one.
        /// Each case sets safe defaults for fields introduced in that version.
        /// Never removes or renames existing fields — only adds missing ones.
        /// </summary>
        private void MigrateConfig()
        {
            bool dirty = false;

            for (int v = Configuration.ConfigVersion + 1; v <= CurrentConfigVersion; v++)
            {
                switch (v)
                {
                    case 1:
                        break;

                    case 2:
                        if (string.IsNullOrWhiteSpace(Configuration.ThemeVars))
                            Configuration.ThemeVars = "{}";
                        break;

                    case 3:
                        if (string.IsNullOrWhiteSpace(Configuration.SelectedVersion))
                            Configuration.SelectedVersion = string.Empty;
                        break;

                    case 4:
                        break;

                    case 5:
                        // AllowUserThemes defaults to false — no migration needed.
                        break;

                    case 6:
                        if (string.IsNullOrWhiteSpace(Configuration.ThemeCatalogUrl))
                            Configuration.ThemeCatalogUrl = !string.IsNullOrWhiteSpace(Configuration.SkinUrl)
                                ? Configuration.SkinUrl
                                : "https://raw.githubusercontent.com/Jellyfin-PG/Skin-Manager-Themes/refs/heads/main/skins.json";

                        if (!string.IsNullOrWhiteSpace(Configuration.SelectedCssUrl) && Configuration.SelectedCssUrl != "Default")
                        {
                            Configuration.DefaultThemeMode = "Catalog";
                            Configuration.DefaultThemeName = string.IsNullOrWhiteSpace(Configuration.Skin) ? "Imported theme" : Configuration.Skin;
                        }
                        break;

                    case 7:
                        Configuration.ThemeCatalogUrl = MigrateCatalogUrl(Configuration.ThemeCatalogUrl);
                        Configuration.SkinUrl = MigrateCatalogUrl(Configuration.SkinUrl);
                        break;
                }

                Configuration.ConfigVersion = v;
                dirty = true;
            }

            if (dirty)
                SaveConfiguration();
        }

        public static string MigrateCatalogUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value.Trim(), LegacyCatalogUrl, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value.Trim(), "https://raw.githubusercontent.com/Jellyfin-PG/Skin-Manager-Themes/main/skins.json", StringComparison.OrdinalIgnoreCase))
                return DefaultCatalogUrl;

            return value.Trim();
        }

        /// <summary>
        /// Invalidates the injection cache whenever the admin saves new settings,
        /// so the very next page request picks up the updated configuration.
        /// </summary>
        public override void SaveConfiguration()
        {
            Services.SkinInjector.InvalidateInjectionCache();
            base.SaveConfiguration();
            UpdateIndexHtml(true);
            UpdateMenuLink(Configuration.AllowUserThemes);
        }

        public override void OnUninstalling()
        {
            UpdateIndexHtml(false);
            UpdateMenuLink(false);
            base.OnUninstalling();
        }

        private void UpdateMenuLink(bool enabled)
        {
            try
            {
                string path = Path.Combine(_applicationPaths.WebPath, "config.json");
                Services.WebConfigMenuLink.UpdateFile(path, "Theme Store", "palette", MenuLinkUrl, enabled);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or Newtonsoft.Json.JsonException)
            {
                // The legacy/mobile DOM integration remains available when web config is read-only.
                Console.Error.WriteLine("[ThemeStore] Could not update jellyfin-web/config.json: " + exception.Message);
            }
        }

        private void UpdateIndexHtml(bool enabled)
        {
            try
            {
                string webPath = _applicationPaths.WebPath;
                if (string.IsNullOrWhiteSpace(webPath))
                    return;

                string path = Path.Combine(webPath, "index.html");
                if (!File.Exists(path))
                    return;

                string original = File.ReadAllText(path);
                string updated = Services.SkinInjector.ApplyToHtml(original, enabled);
                if (string.Equals(original, updated, StringComparison.Ordinal))
                    return;

                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory))
                    return;

                string temporary = Path.Combine(directory, Path.GetRandomFileName());
                try
                {
                    File.WriteAllText(temporary, updated);
                    File.Move(temporary, path, true);
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Web integration is best effort and must never prevent server startup.
                Console.Error.WriteLine("[ThemeStore] Could not update jellyfin-web/index.html. Grant Jellyfin write access or inject the Theme Store script manually: " + exception.Message);
            }
        }

        public IEnumerable<PluginPageInfo> GetPages()
            => CreatePages();

        public static IReadOnlyList<PluginPageInfo> CreatePages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "ThemeStoreSettings",
                    DisplayName = "Theme Store Settings",
                    EnableInMainMenu = true,
                    MenuSection = "server",
                    MenuIcon = "settings",
                    EmbeddedResourcePath = $"{typeof(Plugin).Namespace}.Configuration.configPage.html"
                }
            };
        }
    }
}
