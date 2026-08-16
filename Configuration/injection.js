(function () {
  'use strict';

  if (window.__jellyfinThemeStoreLoaded) return;
  window.__jellyfinThemeStoreLoaded = true;

  const MENU_ID = 'theme-store-sidebar';
  const MODAL_ID = 'theme-store-modal';
  const STYLE_ID = 'theme-store-user-theme';
  const VARS_ID = 'theme-store-user-vars';
  const COMPATIBILITY_ID = 'theme-store-compatibility';
  const COMPATIBILITY_CSS = [
    'body > .skip-button-container {',
    '  position: fixed !important;',
    '  left: 0 !important;',
    '  right: 0 !important;',
    '  pointer-events: none !important;',
    '  z-index: 10000 !important;',
    '}',
    'body > .skip-button-container:has(> .skip-button:not(.hide):not(.skip-button-hidden)) {',
    '  display: block !important;',
    '  visibility: visible !important;',
    '  opacity: 1 !important;',
    '}',
    'body > .skip-button-container > .skip-button:not(.hide):not(.skip-button-hidden) {',
    '  display: flex !important;',
    '  visibility: visible !important;',
    '  opacity: 1 !important;',
    '  pointer-events: auto !important;',
    '  z-index: 10000 !important;',
    '}'
  ].join('\n');
  const SAFE_ROUTE = /^#\/(?:dashboard[^/?#]*|configuration(?:page)?[^/?#]*|metadata[^/?#]*|wizard[^/?#]*|mypreferences[^/?#]*|login[^/?#]*|selectserver[^/?#]*|selectuser[^/?#]*|addserver[^/?#]*|signout[^/?#]*)(?:\/|[?]|$)/;
  const RETRY_DELAYS = [250, 500, 1000, 2000, 5000, 10000, 15000];
  const CSS_CACHE_LIMIT = 4;
  const CSS_CACHE_MAX_CHARS = 8 * 1024 * 1024;

  let started = false;
  let refreshTimer = 0;
  let refreshDueAt = 0;
  let refreshRequest = null;
  let refreshQueued = false;
  let refreshFailures = 0;
  let lastRefreshSuccess = 0;
  let applyRun = 0;
  let applyRequestSignature = '';
  let applyTimer = 0;
  let applyFailures = 0;
  let priorityFrame = 0;
  let priorityObserver;
  let desired = emptyDesired();
  let appliedSignature = '';
  const cssCache = new Map();
  let cssCacheChars = 0;

  function emptyDesired() {
    return { id: '', version: '1', variables: {}, token: '', signature: '', cacheKey: '' };
  }

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
    throw new Error('Theme state response is not JSON.');
  }

  function isSuspended() {
    return SAFE_ROUTE.test(window.location.hash) || !!document.getElementById(MODAL_ID);
  }

  function removeElement(id) {
    const element = document.getElementById(id);
    if (element) element.remove();
  }

  function removeAppliedTheme() {
    ++applyRun;
    if (applyTimer) {
      clearTimeout(applyTimer);
      applyTimer = 0;
    }
    removeElement(STYLE_ID);
    removeElement(VARS_ID);
    removeElement(COMPATIBILITY_ID);
    appliedSignature = '';
  }

  function suspendTheme() {
    removeAppliedTheme();
  }

  function kebab(value) {
    return String(value || '')
      .replace(/^-+/, '')
      .replace(/([a-z])([A-Z])/g, '$1-$2')
      .replace(/_/g, '-')
      .toLowerCase();
  }

  function substituteVariables(css, values) {
    Object.keys(values || {}).forEach(function (key) {
      css = css.split('{{' + key + '}}').join(values[key]);
    });
    return css;
  }

  function variablesCss(values) {
    const keys = Object.keys(values || {});
    if (!keys.length) return '';
    let css = ':root, body {\n';
    keys.forEach(function (key) {
      css += '  --' + kebab(key) + ': ' + values[key] + ' !important;\n';
    });
    return css + '}';
  }

  function styleTarget() {
    return document.body || document.head || document.documentElement;
  }

  function createCompatibilityStyle(id) {
    const style = document.createElement('style');
    style.id = id;
    style.setAttribute('data-theme-store-signature', desired.signature);
    style.textContent = COMPATIBILITY_CSS;
    return style;
  }

  function installCss(rawCss) {
    if (isSuspended() || !desired.id || desired.id === 'jellyfin-default') return;
    const target = styleTarget();
    if (!target) return;

    const themeStyle = document.createElement('style');
    themeStyle.id = STYLE_ID + '-pending';
    themeStyle.setAttribute('data-theme-store-signature', desired.signature);
    themeStyle.textContent = substituteVariables(rawCss || '', desired.variables);

    const variableText = variablesCss(desired.variables);
    const variableStyle = variableText ? document.createElement('style') : null;
    if (variableStyle) {
      variableStyle.id = VARS_ID + '-pending';
      variableStyle.setAttribute('data-theme-store-signature', desired.signature);
      variableStyle.textContent = variableText;
    }
    const compatibilityStyle = createCompatibilityStyle(COMPATIBILITY_ID + '-pending');

    if (priorityObserver) priorityObserver.disconnect();
    target.appendChild(themeStyle);
    if (variableStyle) target.appendChild(variableStyle);
    target.appendChild(compatibilityStyle);
    removeElement(STYLE_ID);
    removeElement(VARS_ID);
    removeElement(COMPATIBILITY_ID);
    themeStyle.id = STYLE_ID;
    if (variableStyle) variableStyle.id = VARS_ID;
    compatibilityStyle.id = COMPATIBILITY_ID;
    appliedSignature = desired.signature;
    if (priorityObserver) priorityObserver.observe(document.documentElement, { childList: true, subtree: true });
    scheduleThemePriority();
  }

  function rememberCss(key, css) {
    if (cssCache.has(key)) {
      cssCacheChars -= cssCache.get(key).length;
      cssCache.delete(key);
    }
    cssCache.set(key, css);
    cssCacheChars += css.length;
    while (cssCache.size > 1 && (cssCache.size > CSS_CACHE_LIMIT || cssCacheChars > CSS_CACHE_MAX_CHARS)) {
      const oldestKey = cssCache.keys().next().value;
      cssCacheChars -= cssCache.get(oldestKey).length;
      cssCache.delete(oldestKey);
    }
  }

  function scheduleApplyRetry() {
    if (applyTimer || isSuspended() || !desired.id) return;
    const delay = RETRY_DELAYS[Math.min(applyFailures, RETRY_DELAYS.length - 1)];
    applyTimer = setTimeout(function () {
      applyTimer = 0;
      applyDesiredTheme(true);
    }, delay);
  }

  function applyDesiredTheme(force) {
    if (isSuspended()) {
      suspendTheme();
      return;
    }
    if (!desired.id || desired.id === 'jellyfin-default') {
      removeAppliedTheme();
      appliedSignature = desired.signature;
      return;
    }

    const existing = document.getElementById(STYLE_ID);
    if (!force && appliedSignature === desired.signature && existing && existing.getAttribute('data-theme-store-signature') === desired.signature) {
      scheduleThemePriority();
      return;
    }
    if (cssCache.has(desired.cacheKey)) {
      installCss(cssCache.get(desired.cacheKey));
      return;
    }
    if (applyRequestSignature === desired.signature) return;
    if (!api()) {
      applyFailures++;
      scheduleApplyRetry();
      return;
    }

    const epoch = ++applyRun;
    const requestedSignature = desired.signature;
    applyRequestSignature = requestedSignature;
    apiFetch('ThemeStore/Theme.css', { type: 'GET', dataType: 'text', cache: 'no-store' }, {
      id: desired.id,
      v: desired.version,
      s: desired.token || desired.signature
    })
      .then(readText)
      .then(function (css) {
        if (epoch !== applyRun || requestedSignature !== desired.signature) return;
        rememberCss(desired.cacheKey, css);
        applyFailures = 0;
        installCss(css);
      })
      .catch(function (error) {
        if (epoch !== applyRun || requestedSignature !== desired.signature) return;
        applyFailures++;
        console.warn('[ThemeStore] Could not apply theme; retrying:', error);
        scheduleApplyRetry();
      })
      .finally(function () {
        if (applyRequestSignature === requestedSignature) applyRequestSignature = '';
        if (requestedSignature === desired.signature && !isSuspended() && !document.getElementById(STYLE_ID)) scheduleApplyRetry();
      });
  }

  function stableVariables(values) {
    return Object.keys(values || {}).sort().map(function (key) { return key + '=' + values[key]; }).join('&');
  }

  function setDesiredTheme(state) {
    const next = {
      id: String(state.ThemeId || ''),
      version: String(state.Version || '1'),
      variables: state.Variables || {},
      token: String(state.StateToken || '')
    };
    next.signature = next.token || [next.id, next.version, stableVariables(next.variables)].join('|');
    next.cacheKey = [next.id, next.version, next.token || 'no-token'].join('|');
    const changed = next.signature !== desired.signature || next.id !== desired.id;
    desired = next;
    if (changed) {
      ++applyRun;
      applyFailures = 0;
      if (applyTimer) {
        clearTimeout(applyTimer);
        applyTimer = 0;
      }
    }
    applyDesiredTheme(false);
  }

  function retryDelay(failures) {
    return RETRY_DELAYS[Math.min(Math.max(failures - 1, 0), RETRY_DELAYS.length - 1)];
  }

  function scheduleRefresh(delay) {
    delay = Math.max(0, delay || 0);
    const dueAt = Date.now() + delay;
    if (refreshTimer && dueAt >= refreshDueAt) return;
    if (refreshTimer) clearTimeout(refreshTimer);
    refreshDueAt = dueAt;
    refreshTimer = setTimeout(function () {
      refreshTimer = 0;
      refreshDueAt = 0;
      refreshThemeState();
    }, delay);
  }

  function refreshThemeState() {
    if (SAFE_ROUTE.test(window.location.hash)) {
      suspendTheme();
      return Promise.resolve();
    }
    if (!api()) {
      refreshFailures++;
      scheduleRefresh(retryDelay(refreshFailures));
      return Promise.resolve();
    }
    if (refreshRequest) {
      refreshQueued = true;
      return refreshRequest;
    }

    refreshRequest = apiFetch('ThemeStore/State', {
      type: 'GET',
      dataType: 'json',
      cache: 'no-store',
      headers: { 'Cache-Control': 'no-cache' }
    }, { _: Date.now() })
      .then(readJson)
      .then(function (state) {
        refreshFailures = 0;
        lastRefreshSuccess = Date.now();
        setDesiredTheme(state || {});
      })
      .catch(function (error) {
        refreshFailures++;
        console.warn('[ThemeStore] Could not refresh theme state; keeping current theme and retrying:', error);
        scheduleRefresh(retryDelay(refreshFailures));
      })
      .finally(function () {
        refreshRequest = null;
        if (refreshQueued) {
          refreshQueued = false;
          scheduleRefresh(0);
        }
      });
    return refreshRequest;
  }

  function ensureThemeLast() {
    if (priorityFrame) {
      cancelAnimationFrame(priorityFrame);
      priorityFrame = 0;
    }
    if (isSuspended()) {
      suspendTheme();
      return;
    }
    if (!desired.id || desired.id === 'jellyfin-default') return;

    const themeStyle = document.getElementById(STYLE_ID);
    if (!themeStyle || themeStyle.getAttribute('data-theme-store-signature') !== desired.signature) {
      if (cssCache.has(desired.cacheKey)) installCss(cssCache.get(desired.cacheKey));
      else applyDesiredTheme(false);
      return;
    }

    const target = styleTarget();
    if (!target) return;
    let compatibilityStyle = document.getElementById(COMPATIBILITY_ID);
    if (!compatibilityStyle || compatibilityStyle.getAttribute('data-theme-store-signature') !== desired.signature) {
      if (priorityObserver) priorityObserver.disconnect();
      if (compatibilityStyle) compatibilityStyle.remove();
      compatibilityStyle = createCompatibilityStyle(COMPATIBILITY_ID);
      target.appendChild(compatibilityStyle);
      if (priorityObserver) priorityObserver.observe(document.documentElement, { childList: true, subtree: true });
    }
    const nodes = [themeStyle, document.getElementById(VARS_ID), compatibilityStyle].filter(Boolean);
    const children = target.children;
    const offset = children.length - nodes.length;
    const alreadyLast = offset >= 0 && nodes.every(function (node, index) {
      return node.parentNode === target && children[offset + index] === node;
    });
    if (alreadyLast) return;

    if (priorityObserver) priorityObserver.disconnect();
    nodes.forEach(function (node) { target.appendChild(node); });
    if (priorityObserver) priorityObserver.observe(document.documentElement, { childList: true, subtree: true });
  }

  function scheduleThemePriority() {
    if (priorityFrame) return;
    priorityFrame = requestAnimationFrame(function () {
      priorityFrame = 0;
      ensureThemeLast();
    });
  }

  function closeStore() {
    const overlay = document.getElementById(MODAL_ID);
    if (overlay) overlay.remove();
    applyDesiredTheme(false);
    scheduleRefresh(0);
  }

  async function openStore() {
    const old = document.getElementById(MODAL_ID);
    if (old) old.remove();
    suspendTheme();

    const overlay = document.createElement('div');
    overlay.id = MODAL_ID;
    overlay.style.cssText = 'position:fixed;inset:0;z-index:9999;background:#101010;overflow:auto;color:#eee;';
    overlay.innerHTML = '<div style="position:sticky;top:0;z-index:50;display:flex;justify-content:flex-end;padding:.5rem;background:#111;border-bottom:1px solid #333"><button type="button" aria-label="Theme Store schließen" style="border:0;background:transparent;color:#fff;font-size:2rem;line-height:1;cursor:pointer;padding:.25rem .7rem">×</button></div><div data-theme-store-content><div style="padding:3rem;text-align:center">Theme Store wird geladen…</div></div>';
    overlay.querySelector('button').addEventListener('click', closeStore);
    document.body.appendChild(overlay);

    const client = api();
    const content = overlay.querySelector('[data-theme-store-content]');
    if (!client || !client.fetch || !client.getUrl) {
      content.textContent = 'Theme Store konnte noch nicht geladen werden. Bitte kurz warten und erneut öffnen.';
      scheduleRefresh(250);
      return;
    }
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
    if (isSuspended()) {
      suspendTheme();
      return;
    }
    applyDesiredTheme(false);
    scheduleThemePriority();
    scheduleRefresh(50);
  }

  function wake() {
    if (document.visibilityState === 'hidden') return;
    injectMenuItem();
    if (isSuspended()) {
      suspendTheme();
      return;
    }
    applyDesiredTheme(false);
    scheduleThemePriority();
    scheduleRefresh(0);
  }

  function start() {
    if (started) return;
    started = true;
    const menuObserver = new MutationObserver(injectMenuItem);
    menuObserver.observe(document.documentElement, { childList: true, subtree: true });
    priorityObserver = new MutationObserver(function (mutations) {
      if (isSuspended()) return;
      const relevant = mutations.some(function (mutation) {
        const changedNodes = Array.from(mutation.addedNodes).concat(Array.from(mutation.removedNodes));
        return changedNodes.some(function (node) {
          return node.id === STYLE_ID || node.id === VARS_ID || node.id === COMPATIBILITY_ID || node.nodeName === 'STYLE' || node.nodeName === 'LINK' || node.nodeName === 'BODY';
        });
      });
      if (relevant) scheduleThemePriority();
    });
    priorityObserver.observe(document.documentElement, { childList: true, subtree: true });

    window.addEventListener('hashchange', navigation);
    window.addEventListener('popstate', navigation);
    window.addEventListener('pageshow', wake);
    window.addEventListener('focus', wake);
    window.addEventListener('online', wake);
    window.addEventListener('storage', wake);
    window.addEventListener('theme-store:changed', function () { scheduleRefresh(0); });
    document.addEventListener('visibilitychange', wake);
    document.addEventListener('resume', wake);
    document.addEventListener('viewshow', wake, true);
    document.addEventListener('pagebeforeshow', wake, true);
    document.addEventListener('keydown', function (event) {
      if (event.key === 'Escape' && document.getElementById(MODAL_ID)) closeStore();
    });

    setInterval(function () {
      injectMenuItem();
      if (document.visibilityState === 'hidden' || isSuspended()) return;
      scheduleThemePriority();
      if (!lastRefreshSuccess || Date.now() - lastRefreshSuccess > 30000) scheduleRefresh(0);
    }, 5000);
    navigation();
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
  else start();
})();
