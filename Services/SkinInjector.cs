using System;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.ThemeStore.Configuration;

namespace Jellyfin.Plugin.ThemeStore.Services
{
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
        private static string _cachedKey;
        private static string _cachedInjection = string.Empty;
        private static readonly object InjectionLock = new();

        public static string InjectTheme(PatchRequestPayload payload)
        {
            try
            {
                string html = payload?.Contents;
                if (string.IsNullOrEmpty(html))
                    return html ?? string.Empty;

                html = StripLegacyInjection.Replace(StripPreviousInjection.Replace(html, string.Empty), string.Empty);
                PluginConfiguration config = Plugin.Instance?.Configuration;
                if (config == null)
                    return html;

                string injection = GetInjection(config);
                string block = "\n" + StartMarker + "\n" + injection + EndMarker + "\n";
                return HeadCloseTag.Replace(html, match => block + match.Value, 1);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[ThemeStore] Injection failed: " + ex.Message);
                return payload?.Contents ?? string.Empty;
            }
        }

        public static void InvalidateInjectionCache()
        {
            lock (InjectionLock)
                _cachedKey = null;
        }

        private static string GetInjection(PluginConfiguration config)
        {
            string key = $"{config.AllowUserThemes}|{config.DefaultThemeMode}|{config.DefaultThemeId}|{config.DefaultThemeName}|{config.ThemeCatalogUrl}|{config.CustomCss?.GetHashCode()}";
            if (_cachedKey == key)
                return _cachedInjection;

            lock (InjectionLock)
            {
                if (_cachedKey == key)
                    return _cachedInjection;

                _cachedInjection = BuildInjection();
                _cachedKey = key;
                return _cachedInjection;
            }
        }

        private static string BuildInjection()
            => @"<script id=""theme-store-loader"">
(function () {
    'use strict';
    if (window.__jellyfinThemeStoreLoaded) return;
    window.__jellyfinThemeStoreLoaded = true;

    var STYLE_ID = 'theme-store-user-theme';
    var VARS_ID = 'theme-store-user-vars';
    var MENU_ID = 'theme-store-menu-item';
    var SAFE_ROUTE = /^#\/(?:dashboard|configuration(?:page)?|metadata|wizard|mypreferences[^/?#]*|login[^/?#]*|selectserver[^/?#]*|selectuser[^/?#]*|addserver[^/?#]*|signout[^/?#]*)(?:\/|[?]|$)/;
    var runId = 0;

    function apiUrl(path, query) {
        if (window.ApiClient && ApiClient.getUrl) return ApiClient.getUrl(path, query || {});
        return path + (query ? '?' + new URLSearchParams(query).toString() : '');
    }

    function apiFetch(path, options, query) {
        var url = apiUrl(path, query);
        if (window.ApiClient && ApiClient.fetch) {
            var request = Object.assign({ url: url }, options || {});
            return ApiClient.fetch(request);
        }
        return fetch(url, options || {});
    }

    function clearTheme() {
        ++runId;
        [STYLE_ID, VARS_ID].forEach(function (id) {
            var node = document.getElementById(id);
            if (!node) return;
            if (node.href && node.href.indexOf('blob:') === 0) URL.revokeObjectURL(node.href);
            node.remove();
        });
    }

    function kebab(value) {
        return String(value || '').replace(/^-+/, '').replace(/([a-z])([A-Z])/g, '$1-$2').replace(/_/g, '-').toLowerCase();
    }

    function applyVariables(values) {
        var keys = Object.keys(values || {});
        if (!keys.length) return;
        var css = ':root, body {\n';
        keys.forEach(function (key) { css += '  --' + kebab(key) + ': ' + values[key] + ' !important;\n'; });
        css += '}';
        var style = document.createElement('style');
        style.id = VARS_ID;
        style.textContent = css;
        (document.body || document.head).appendChild(style);
    }

    function applyTheme(themeId, version, variables) {
        clearTheme();
        if (!themeId || themeId === 'jellyfin-default') return;
        var epoch = runId;
        apiFetch('ThemeStore/Theme.css', {}, { id: themeId, v: version || '1' })
            .then(function (response) {
                if (!response.ok) throw new Error('HTTP ' + response.status);
                return response.text();
            })
            .then(function (css) {
                if (epoch !== runId || SAFE_ROUTE.test(window.location.hash)) return;
                Object.keys(variables || {}).forEach(function (key) {
                    css = css.split('{{' + key + '}}').join(variables[key]);
                });
                applyVariables(variables || {});
                var blob = new Blob([css], { type: 'text/css' });
                var link = document.createElement('link');
                link.id = STYLE_ID;
                link.rel = 'stylesheet';
                link.href = URL.createObjectURL(blob);
                (document.body || document.head).appendChild(link);
            })
            .catch(function (error) { console.warn('[ThemeStore] Could not apply theme:', error); });
    }

    function chooseTheme(data) {
        if (data.AllowUserThemes && data.SelectedThemeId) return data.SelectedThemeId;
        if (data.DefaultMode === 'CustomCss') return 'custom';
        if (data.DefaultMode === 'Catalog') return data.DefaultThemeId || '';
        return '';
    }

    function refreshTheme() {
        if (SAFE_ROUTE.test(window.location.hash)) {
            clearTheme();
            return;
        }
        if (!window.ApiClient) {
            setTimeout(refreshTheme, 250);
            return;
        }
        apiFetch('ThemeStore/Catalog')
            .then(function (response) {
                if (!response.ok) throw new Error('HTTP ' + response.status);
                return response.json();
            })
            .then(function (data) {
                var id = chooseTheme(data);
                var theme = (data.Themes || []).find(function (entry) { return entry.Id === id; });
                applyTheme(id, theme ? theme.Version : '1', data.SelectedThemeId === id ? data.Variables : (data.DefaultVariables || {}));
            })
            .catch(function () { clearTheme(); });
    }

    function addMenuItem() {
        if (document.getElementById(MENU_ID)) return;
        var anchor = document.querySelector('.lnkHomePreferences, .lnkDisplayPreferences, .lnkSubtitlePreferences, .lnkControlsPreferences');
        var container = anchor && anchor.closest('.verticalSection');
        if (!container) return;
        var link = document.createElement('a');
        link.id = MENU_ID;
        link.className = 'emby-button listItem-border';
        link.href = apiUrl('ThemeStore/Page');
        link.style.cssText = 'display:block;margin:0;padding:0;';
        var row = document.createElement('div');
        row.className = 'listItem';
        var icon = document.createElement('span');
        icon.className = 'material-icons listItemIcon listItemIcon-transparent palette';
        icon.setAttribute('aria-hidden', 'true');
        var body = document.createElement('div');
        body.className = 'listItemBody';
        var label = document.createElement('div');
        label.className = 'listItemBodyText';
        label.textContent = 'Theme Store';
        body.appendChild(label); row.appendChild(icon); row.appendChild(body); link.appendChild(row); container.appendChild(link);
    }

    var scheduled = false;
    function navigation() {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(function () { scheduled = false; addMenuItem(); refreshTheme(); });
    }
    var observer = new MutationObserver(addMenuItem);
    observer.observe(document.documentElement, { childList: true, subtree: true });
    window.addEventListener('hashchange', navigation);
    window.addEventListener('popstate', navigation);
    window.addEventListener('theme-store:changed', refreshTheme);
    addMenuItem(); refreshTheme();
})();
</script>
";
    }
}
