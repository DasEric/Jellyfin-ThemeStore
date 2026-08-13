(function () {
  'use strict';

  if (window.__jellyfinThemeStoreLoaded) return;
  window.__jellyfinThemeStoreLoaded = true;

  const MENU_ID = 'theme-store-sidebar';
  const MODAL_ID = 'theme-store-modal';
  const STYLE_ID = 'theme-store-user-theme';
  const VARS_ID = 'theme-store-user-vars';
  const SAFE_ROUTE = /^#\/(?:dashboard|configuration(?:page)?|metadata|wizard|mypreferences[^/?#]*|login[^/?#]*|selectserver[^/?#]*|selectuser[^/?#]*|addserver[^/?#]*|signout[^/?#]*)(?:\/|[?]|$)/;
  let runId = 0;
  let priorityFrame = 0;
  let priorityObserver;

  function api() {
    return typeof ApiClient !== 'undefined' ? ApiClient : window.ApiClient;
  }

  function apiUrl(path, query) {
    const client = api();
    if (client && client.getUrl) return client.getUrl(path, query || {});
    return path + (query ? '?' + new URLSearchParams(query).toString() : '');
  }

  function apiFetch(path, options, query) {
    const client = api();
    const url = apiUrl(path, query);
    if (client && client.fetch) return client.fetch(Object.assign({ url: url }, options || {}));
    const request = Object.assign({}, options || {});
    const dataType = request.dataType;
    if (request.type && !request.method) request.method = request.type;
    delete request.type;
    delete request.dataType;
    return fetch(url, request).then(function (response) {
      if (!response.ok) throw new Error('HTTP ' + response.status);
      if (dataType === 'json') return response.json();
      if (dataType === 'text') return response.text();
      return response;
    });
  }

  function readText(value) {
    if (typeof value === 'string') return Promise.resolve(value);
    if (value && typeof value.text === 'function') {
      if (value.ok === false) throw new Error('HTTP ' + value.status);
      return value.text();
    }
    throw new Error('Theme CSS response is not text.');
  }

  function readJson(value) {
    if (value && typeof value.json === 'function') {
      if (value.ok === false) throw new Error('HTTP ' + value.status);
      return value.json();
    }
    if (value && typeof value === 'object') return Promise.resolve(value);
    throw new Error('Theme catalog response is not JSON.');
  }

  function clearTheme() {
    ++runId;
    [STYLE_ID, VARS_ID].forEach(function (id) {
      const node = document.getElementById(id);
      if (!node) return;
      if (node.href && node.href.indexOf('blob:') === 0) URL.revokeObjectURL(node.href);
      node.remove();
    });
  }

  function ensureThemeLast() {
    if (priorityFrame) {
      cancelAnimationFrame(priorityFrame);
      priorityFrame = 0;
    }
    if (SAFE_ROUTE.test(window.location.hash) || document.getElementById(MODAL_ID) || !document.body) return;

    const nodes = [document.getElementById(VARS_ID), document.getElementById(STYLE_ID)].filter(Boolean);
    if (!nodes.length) return;
    const children = document.body.children;
    const offset = children.length - nodes.length;
    const alreadyLast = offset >= 0 && nodes.every(function (node, index) {
      return children[offset + index] === node;
    });
    if (alreadyLast) return;

    if (priorityObserver) priorityObserver.disconnect();
    nodes.forEach(function (node) { document.body.appendChild(node); });
    if (priorityObserver) priorityObserver.observe(document.documentElement, { childList: true, subtree: true });
  }

  function scheduleThemePriority() {
    if (priorityFrame) return;
    priorityFrame = requestAnimationFrame(function () {
      priorityFrame = 0;
      ensureThemeLast();
    });
  }

  function kebab(value) {
    return String(value || '')
      .replace(/^-+/, '')
      .replace(/([a-z])([A-Z])/g, '$1-$2')
      .replace(/_/g, '-')
      .toLowerCase();
  }

  function applyVariables(values) {
    const keys = Object.keys(values || {});
    if (!keys.length) return;
    let css = ':root, body {\n';
    keys.forEach(function (key) {
      css += '  --' + kebab(key) + ': ' + values[key] + ' !important;\n';
    });
    css += '}';
    const style = document.createElement('style');
    style.id = VARS_ID;
    style.textContent = css;
    (document.body || document.head).appendChild(style);
  }

  function applyTheme(themeId, version, variables) {
    clearTheme();
    if (!themeId || themeId === 'jellyfin-default') return;
    const epoch = runId;
    apiFetch('ThemeStore/Theme.css', { type: 'GET', dataType: 'text' }, { id: themeId, v: version || '1' })
      .then(readText)
      .then(function (css) {
        if (epoch !== runId || SAFE_ROUTE.test(window.location.hash) || document.getElementById(MODAL_ID)) return;
        Object.keys(variables || {}).forEach(function (key) {
          css = css.split('{{' + key + '}}').join(variables[key]);
        });
        applyVariables(variables || {});
        const blob = new Blob([css], { type: 'text/css' });
        const link = document.createElement('link');
        link.id = STYLE_ID;
        link.rel = 'stylesheet';
        link.href = URL.createObjectURL(blob);
        (document.body || document.head).appendChild(link);
        scheduleThemePriority();
      })
      .catch(function (error) {
        console.warn('[ThemeStore] Could not apply theme:', error);
      });
  }

  function chooseTheme(data) {
    if (data.AllowUserThemes && data.SelectedThemeId) return data.SelectedThemeId;
    if (data.DefaultMode === 'CustomCss') return 'custom';
    if (data.DefaultMode === 'Catalog') return data.DefaultThemeId || '';
    return '';
  }

  function refreshTheme() {
    if (SAFE_ROUTE.test(window.location.hash) || document.getElementById(MODAL_ID)) {
      clearTheme();
      return;
    }
    if (!api()) {
      setTimeout(refreshTheme, 250);
      return;
    }
    apiFetch('ThemeStore/Catalog', { type: 'GET', dataType: 'json' })
      .then(readJson)
      .then(function (data) {
        const id = chooseTheme(data);
        const theme = (data.Themes || []).find(function (entry) { return entry.Id === id; });
        const variables = data.SelectedThemeId === id ? data.Variables : (data.DefaultVariables || {});
        applyTheme(id, theme ? theme.Version : '1', variables);
      })
      .catch(clearTheme);
  }

  function closeStore() {
    const overlay = document.getElementById(MODAL_ID);
    if (overlay) overlay.remove();
    refreshTheme();
  }

  async function openStore() {
    const old = document.getElementById(MODAL_ID);
    if (old) old.remove();
    clearTheme();

    const overlay = document.createElement('div');
    overlay.id = MODAL_ID;
    overlay.style.cssText = 'position:fixed;inset:0;z-index:9999;background:#101010;overflow:auto;color:#eee;';
    overlay.innerHTML = '<div style="position:sticky;top:0;z-index:50;display:flex;justify-content:flex-end;padding:.5rem;background:#111;border-bottom:1px solid #333"><button type="button" aria-label="Theme Store schließen" style="border:0;background:transparent;color:#fff;font-size:2rem;line-height:1;cursor:pointer;padding:.25rem .7rem">×</button></div><div data-theme-store-content><div style="padding:3rem;text-align:center">Theme Store wird geladen…</div></div>';
    overlay.querySelector('button').addEventListener('click', closeStore);
    document.body.appendChild(overlay);

    const client = api();
    const content = overlay.querySelector('[data-theme-store-content]');
    try {
      const html = await client.fetch({ url: client.getUrl('ThemeStore/Page'), type: 'GET', dataType: 'text' });
      const doc = new DOMParser().parseFromString(html, 'text/html');
      const page = doc.querySelector('[data-role="page"]');
      content.innerHTML = page ? page.outerHTML : html;
      const storeView = content.querySelector('[data-role="page"]') || content;
      const module = await import(client.getUrl('ThemeStore/PageScript') + '?v=' + Date.now());
      if (module.default) module.default(storeView, { close: closeStore });
    } catch (error) {
      content.textContent = 'Theme Store konnte nicht geladen werden.';
      console.warn('[ThemeStore] Could not open store:', error);
    }
  }

  function injectMenuItem() {
    if (document.getElementById(MENU_ID)) return;
    const client = api();
    const sidebar = document.querySelector('.mainDrawer-scrollContainer, .mainDrawer .scrollContainer');
    if (!sidebar || !client) return;

    const entry = document.createElement('a');
    entry.id = MENU_ID;
    entry.href = '#';
    entry.setAttribute('is', 'emby-linkbutton');
    entry.setAttribute('data-itemid', 'theme-store');
    entry.className = 'navMenuOption lnkMediaFolder';
    entry.innerHTML = '<span class="material-icons navMenuOptionIcon palette" aria-hidden="true"></span><span class="navMenuOptionText">Theme Store</span>';
    entry.addEventListener('click', function (event) {
      event.preventDefault();
      event.stopPropagation();
      const backdrop = document.querySelector('.mainDrawer-backdrop');
      if (backdrop) backdrop.click();
      openStore();
    });

    const custom = sidebar.querySelector('.customMenuOptions');
    const libraries = sidebar.querySelector('.libraryMenuOptions');
    const admin = sidebar.querySelector('.adminMenuOptions');
    if (custom) custom.appendChild(entry);
    else if (libraries) sidebar.insertBefore(entry, libraries);
    else if (admin) sidebar.insertBefore(entry, admin);
    else sidebar.appendChild(entry);
  }

  function navigation() {
    injectMenuItem();
    refreshTheme();
    scheduleThemePriority();
  }

  function start() {
    const menuObserver = new MutationObserver(injectMenuItem);
    menuObserver.observe(document.body, { childList: true, subtree: true });
    priorityObserver = new MutationObserver(function (mutations) {
      if (SAFE_ROUTE.test(window.location.hash) || document.getElementById(MODAL_ID)) return;
      const styleWasAdded = mutations.some(function (mutation) {
        return Array.from(mutation.addedNodes).some(function (node) {
          return (node.nodeName === 'STYLE' || node.nodeName === 'LINK') && node.id !== STYLE_ID && node.id !== VARS_ID;
        });
      });
      if (styleWasAdded) scheduleThemePriority();
    });
    priorityObserver.observe(document.documentElement, { childList: true, subtree: true });
    window.addEventListener('hashchange', navigation);
    window.addEventListener('popstate', navigation);
    window.addEventListener('theme-store:changed', refreshTheme);
    document.addEventListener('keydown', function (event) {
      if (event.key === 'Escape' && document.getElementById(MODAL_ID)) closeStore();
    });
    navigation();
  }

  let attempts = 0;
  const timer = setInterval(function () {
    if (api() || attempts++ > 100) {
      clearInterval(timer);
      if (api()) {
        if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
        else start();
      }
    }
  }, 200);
})();
