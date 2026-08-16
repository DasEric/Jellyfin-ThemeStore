import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const injectionSource = await readFile(new URL('../Configuration/injection.js', import.meta.url), 'utf8');

class FakeEventTarget {
  constructor() {
    this.listeners = new Map();
  }

  addEventListener(type, listener) {
    if (!this.listeners.has(type)) this.listeners.set(type, []);
    this.listeners.get(type).push(listener);
  }

  dispatchEvent(event) {
    for (const listener of this.listeners.get(event.type) || []) listener.call(this, event);
    return true;
  }
}

class FakeElement extends FakeEventTarget {
  constructor(tagName, ownerDocument) {
    super();
    this.nodeName = tagName.toUpperCase();
    this.ownerDocument = ownerDocument;
    this.children = [];
    this.parentNode = null;
    this.attributes = new Map();
    this.id = '';
    this.textContent = '';
    this.style = { cssText: '' };
  }

  appendChild(child) {
    if (child.parentNode) child.parentNode.removeChild(child);
    this.children.push(child);
    child.parentNode = this;
    return child;
  }

  removeChild(child) {
    const index = this.children.indexOf(child);
    if (index >= 0) this.children.splice(index, 1);
    child.parentNode = null;
    return child;
  }

  remove() {
    if (this.parentNode) this.parentNode.removeChild(this);
  }

  setAttribute(name, value) {
    this.attributes.set(name, String(value));
    if (name === 'id') this.id = String(value);
  }

  getAttribute(name) {
    return this.attributes.get(name) ?? null;
  }

  querySelector() {
    return null;
  }
}

class FakeDocument extends FakeEventTarget {
  constructor() {
    super();
    this.readyState = 'complete';
    this.visibilityState = 'visible';
    this.documentElement = new FakeElement('html', this);
    this.head = new FakeElement('head', this);
    this.body = new FakeElement('body', this);
    this.documentElement.appendChild(this.head);
    this.documentElement.appendChild(this.body);
  }

  createElement(tagName) {
    return new FakeElement(tagName, this);
  }

  getElementById(id) {
    const visit = (node) => {
      if (node.id === id) return node;
      for (const child of node.children) {
        const found = visit(child);
        if (found) return found;
      }
      return null;
    };
    return visit(this.documentElement);
  }

  querySelector() {
    return null;
  }
}

function wait(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function createRuntime({ state, css = 'body { color: red; }', failStateCalls = 0, clientDelay = 0, unauthenticatedCalls = 0 }) {
  const document = new FakeDocument();
  const window = new FakeEventTarget();
  window.window = window;
  window.document = document;
  window.location = { hash: '#/home' };
  let stateCalls = 0;
  let cssCalls = 0;
  let authCalls = 0;
  const observers = [];
  const client = {
    getUrl(path) {
      return path;
    },
    getCurrentUserId() {
      authCalls++;
      if (authCalls <= unauthenticatedCalls) return null;
      return 'test-user-id';
    },
    fetch(options) {
      if (options.url.startsWith('ThemeStore/State')) {
        stateCalls++;
        if (stateCalls <= failStateCalls) return Promise.reject(new Error('HTTP 401'));
        try {
          return Promise.resolve(typeof state === 'function' ? state() : state);
        } catch (error) {
          return Promise.reject(error);
        }
      }
      if (options.url.startsWith('ThemeStore/Theme.css')) {
        cssCalls++;
        return Promise.resolve(typeof css === 'function' ? css() : css);
      }
      return Promise.reject(new Error('Unexpected URL ' + options.url));
    }
  };
  class FakeMutationObserver {
    constructor(callback) {
      this.callback = callback;
      observers.push(this);
    }
    observe() {}
    disconnect() {}
  }
  const sandbox = {
    window,
    document,
    console: { ...console, warn() {} },
    MutationObserver: FakeMutationObserver,
    DOMParser: class {},
    URLSearchParams,
    setTimeout,
    clearTimeout,
    setInterval() { return 1; },
    clearInterval() {},
    requestAnimationFrame(callback) { return setTimeout(callback, 0); },
    cancelAnimationFrame: clearTimeout,
    fetch() { return Promise.reject(new Error('Native fetch must not be used')); },
    Event
  };
  vm.createContext(sandbox);
  vm.runInContext(injectionSource, sandbox, { filename: 'injection.js' });
  if (clientDelay === 0) {
    sandbox.ApiClient = client;
    window.ApiClient = client;
  } else {
    setTimeout(() => {
      sandbox.ApiClient = client;
      window.ApiClient = client;
    }, clientDelay);
  }
  return {
    document,
    window,
    client,
    observers,
    get stateCalls() { return stateCalls; },
    get cssCalls() { return cssCalls; }
  };
}

const selectedState = {
  ThemeId: 'personal',
  Version: '1',
  Variables: {},
  StateToken: 'personal-token'
};

test('applies the theme when ApiClient becomes available after bootstrap', async () => {
  const runtime = createRuntime({ state: selectedState, clientDelay: 30 });

  await wait(500);

  assert.equal(runtime.document.getElementById('theme-store-user-theme')?.textContent, 'body { color: red; }');
  assert.ok(runtime.stateCalls >= 1);
});

test('keeps retrying after an unauthenticated startup response', async () => {
  const runtime = createRuntime({ state: selectedState, failStateCalls: 1 });

  await wait(800);

  assert.ok(runtime.stateCalls >= 2);
  assert.ok(runtime.document.getElementById('theme-store-user-theme'));
});

test('restores cached CSS after mobile resume removes the style element', async () => {
  const runtime = createRuntime({ state: selectedState });
  await wait(400);
  const originalCssCalls = runtime.cssCalls;
  runtime.document.getElementById('theme-store-user-theme').remove();

  runtime.document.dispatchEvent(new Event('visibilitychange'));
  await wait(100);

  assert.ok(runtime.document.getElementById('theme-store-user-theme'));
  assert.equal(runtime.cssCalls, originalCssCalls);
});

test('restores cached CSS when Jellyfin removes the active style node', async () => {
  const runtime = createRuntime({ state: selectedState });
  await wait(400);
  const style = runtime.document.getElementById('theme-store-user-theme');
  const originalCssCalls = runtime.cssCalls;
  style.remove();

  for (const observer of runtime.observers) observer.callback([{ addedNodes: [], removedNodes: [style] }]);
  await wait(100);

  assert.ok(runtime.document.getElementById('theme-store-user-theme'));
  assert.equal(runtime.cssCalls, originalCssCalls);
});

test('keeps the native Jellyfin skip button visible without defeating its hidden states', async () => {
  const runtime = createRuntime({ state: selectedState });
  await wait(400);

  const theme = runtime.document.getElementById('theme-store-user-theme');
  const compatibility = runtime.document.getElementById('theme-store-compatibility');
  assert.ok(compatibility);
  assert.ok(compatibility.textContent.includes('body > .skip-button-container'));
  assert.ok(compatibility.textContent.includes('.skip-button:not(.hide):not(.skip-button-hidden)'));
  assert.ok(compatibility.textContent.includes('display: flex !important'));
  assert.ok(compatibility.textContent.includes('pointer-events: auto !important'));
  assert.ok(runtime.document.body.children.indexOf(compatibility) > runtime.document.body.children.indexOf(theme));
});

test('restores the Intro Skipper compatibility layer after Jellyfin replaces styles', async () => {
  const runtime = createRuntime({ state: selectedState });
  await wait(400);
  const compatibility = runtime.document.getElementById('theme-store-compatibility');
  compatibility.remove();

  for (const observer of runtime.observers) observer.callback([{ addedNodes: [], removedNodes: [compatibility] }]);
  await wait(100);

  const restored = runtime.document.getElementById('theme-store-compatibility');
  assert.ok(restored);
  assert.equal(restored.getAttribute('data-theme-store-signature'), selectedState.StateToken);
  assert.equal(runtime.document.body.children.at(-1), restored);
});

test('keeps theme and skip-button protection after dynamically loaded Jellyfin styles', async () => {
  const runtime = createRuntime({ state: selectedState });
  await wait(400);
  const lateStyle = runtime.document.createElement('style');
  lateStyle.textContent = '.some-jellyfin-style { display: none !important; }';
  runtime.document.body.appendChild(lateStyle);

  for (const observer of runtime.observers) observer.callback([{ addedNodes: [lateStyle], removedNodes: [] }]);
  await wait(100);

  const theme = runtime.document.getElementById('theme-store-user-theme');
  const compatibility = runtime.document.getElementById('theme-store-compatibility');
  assert.ok(runtime.document.body.children.indexOf(theme) > runtime.document.body.children.indexOf(lateStyle));
  assert.equal(runtime.document.body.children.at(-1), compatibility);
});

test('refreshes an existing theme atomically when the server state changes', async () => {
  let current = selectedState;
  let currentCss = 'body { color: red; }';
  const runtime = createRuntime({ state: () => current, css: () => currentCss });
  await wait(400);
  current = { ThemeId: 'server', Version: '2', Variables: {}, StateToken: 'server-token' };
  currentCss = 'body { color: blue; }';

  runtime.window.dispatchEvent(new Event('focus'));
  await wait(400);

  assert.equal(runtime.document.getElementById('theme-store-user-theme')?.textContent, 'body { color: blue; }');
  assert.equal(runtime.document.getElementById('theme-store-user-theme')?.getAttribute('data-theme-store-signature'), 'server-token');
});

test('keeps the active theme when a later state refresh fails', async () => {
  let failNext = false;
  const runtime = createRuntime({
    state: () => {
      if (failNext) {
        failNext = false;
        throw new Error('HTTP 503');
      }
      return selectedState;
    }
  });
  await wait(400);
  const active = runtime.document.getElementById('theme-store-user-theme');
  failNext = true;

  runtime.window.dispatchEvent(new Event('focus'));
  await wait(100);

  assert.equal(runtime.document.getElementById('theme-store-user-theme'), active);
});

test('removes custom CSS when the resolved state returns to Jellyfin default', async () => {
  let current = selectedState;
  const runtime = createRuntime({ state: () => current });
  await wait(400);
  current = { ThemeId: '', Version: '1', Variables: {}, StateToken: 'jellyfin-token' };

  runtime.window.dispatchEvent(new Event('focus'));
  await wait(100);

  assert.equal(runtime.document.getElementById('theme-store-user-theme'), null);
  assert.equal(runtime.document.getElementById('theme-store-compatibility'), null);
});

test('restores the cached theme immediately after leaving a safe route', async () => {
  const runtime = createRuntime({ state: selectedState });
  await wait(400);
  const originalCssCalls = runtime.cssCalls;
  runtime.window.location.hash = '#/dashboard';
  runtime.window.dispatchEvent(new Event('hashchange'));
  assert.equal(runtime.document.getElementById('theme-store-user-theme'), null);

  runtime.window.location.hash = '#/home';
  runtime.window.dispatchEvent(new Event('hashchange'));
  await wait(100);

  assert.ok(runtime.document.getElementById('theme-store-user-theme'));
  assert.ok(runtime.cssCalls <= originalCssCalls + 1);
});

test('treats Jellyfin html-style admin and setup routes as safe recovery pages', async () => {
  const runtime = createRuntime({ state: selectedState });
  await wait(400);

  for (const route of ['#/dashboard.html', '#/dashboardgeneral.html', '#/wizardstart.html']) {
    runtime.window.location.hash = '#/home';
    runtime.window.dispatchEvent(new Event('hashchange'));
    await wait(100);
    assert.ok(runtime.document.getElementById('theme-store-user-theme'));

    runtime.window.location.hash = route;
    runtime.window.dispatchEvent(new Event('hashchange'));

    assert.equal(runtime.document.getElementById('theme-store-user-theme'), null);
  }
});
