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
            @"(?:\r?\n)?" + Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker) + @"\r?\n?",
            RegexOptions.Compiled);
        private static readonly Regex StripLegacyInjection = new(
            @"(?:\r?\n)?<!-- SkinManager-Start -->[\s\S]*?<!-- SkinManager-End -->\r?\n?",
            RegexOptions.Compiled);
        private static readonly Regex HeadCloseTag = new(@"(</head>)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string InjectTheme(PatchRequestPayload payload)
        {
            try
            {
                return ApplyToHtml(payload?.Contents ?? string.Empty, true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[ThemeStore] Injection failed: " + ex.Message);
                return payload?.Contents ?? string.Empty;
            }
        }

        public static string ApplyToHtml(string html, bool enabled)
        {
            if (string.IsNullOrEmpty(html))
                return html ?? string.Empty;

            html = RemoveInjection(html);
            if (!enabled || !HeadCloseTag.IsMatch(html))
                return html;

            string version = typeof(Plugin).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? typeof(Plugin).Assembly.GetName().Version?.ToString()
                ?? "1";
            string injection = $"<script plugin=\"Theme Store\" src=\"../ThemeStore/InjectionScript?v={Uri.EscapeDataString(version)}\" defer></script>\n";
            string block = "\n" + StartMarker + "\n" + injection + EndMarker + "\n";
            return HeadCloseTag.Replace(html, match => block + match.Value, 1);
        }

        public static string RemoveInjection(string html)
            => StripLegacyInjection.Replace(StripPreviousInjection.Replace(html ?? string.Empty, string.Empty), string.Empty);

        // Retained for configuration-save compatibility. The injected bootstrap
        // is configuration-independent and fetches current settings via the API.
        public static void InvalidateInjectionCache()
        {
        }
    }
}
