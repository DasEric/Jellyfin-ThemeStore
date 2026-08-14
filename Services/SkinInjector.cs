using System;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ThemeStore.Services
{
    /// <summary>
    /// Adds the Theme Store bootstrap script to Jellyfin Web's index page.
    /// The script applies the active theme and adds the user-facing drawer entry.
    /// </summary>
    public static class SkinInjector
    {
        private const string StartMarker = "<!-- ThemeStore-Start -->";
        private const string EndMarker = "<!-- ThemeStore-End -->";
        private static readonly Regex StripPreviousInjection = new(
            Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker) + @"\n?",
            RegexOptions.Compiled);
        private static readonly Regex StripLegacyInjection = new(
            @"<!-- SkinManager-Start -->[\s\S]*?<!-- SkinManager-End -->\n?",
            RegexOptions.Compiled);
        private static readonly Regex HeadCloseTag = new(@"(</head>)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string InjectTheme(PatchRequestPayload payload)
        {
            try
            {
                string html = payload?.Contents;
                if (string.IsNullOrEmpty(html))
                    return html ?? string.Empty;

                html = StripLegacyInjection.Replace(StripPreviousInjection.Replace(html, string.Empty), string.Empty);
                string version = typeof(Plugin).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                    ?? typeof(Plugin).Assembly.GetName().Version?.ToString()
                    ?? "1";
                string injection = $"<script plugin=\"Theme Store\" src=\"../ThemeStore/InjectionScript?v={Uri.EscapeDataString(version)}\" defer></script>\n";
                string block = "\n" + StartMarker + "\n" + injection + EndMarker + "\n";
                return HeadCloseTag.Replace(html, match => block + match.Value, 1);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[ThemeStore] Injection failed: " + ex.Message);
                return payload?.Contents ?? string.Empty;
            }
        }

        // Retained for configuration-save compatibility. The injected bootstrap
        // is configuration-independent and fetches current settings via the API.
        public static void InvalidateInjectionCache()
        {
        }
    }
}
