using System;
using System.Collections.Generic;
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
        private const int CurrentConfigVersion = 6;

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
            ServiceProvider = serviceProvider;
            MigrateConfig();
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
                }

                Configuration.ConfigVersion = v;
                dirty = true;
            }

            if (dirty)
                SaveConfiguration();
        }

        /// <summary>
        /// Invalidates the injection cache whenever the admin saves new settings,
        /// so the very next page request picks up the updated configuration.
        /// </summary>
        public override void SaveConfiguration()
        {
            Services.SkinInjector.InvalidateInjectionCache();
            base.SaveConfiguration();
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
