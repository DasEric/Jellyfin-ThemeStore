export default function (view) {
  if (view.dataset.themeStoreInitialized === '1') return;
  view.dataset.themeStoreInitialized = '1';

  const api = typeof ApiClient !== 'undefined' ? ApiClient : window.ApiClient;
  const q = (name) => view.querySelector('[data-ts="' + name + '"]');
  const state = {
    data: {},
    themes: [],
    selected: '',
    variables: {},
    query: '',
    gallery: [],
    galleryIndex: 0,
    allowed: false
  };

  function url(path) {
    return api.getUrl('ThemeStore/' + path);
  }

  async function call(path, options) {
    const opts = options || {};
    const request = {
      url: url(path),
      type: opts.method || 'GET'
    };
    if (opts.json !== false) request.dataType = 'json';
    if (opts.body !== undefined) {
      request.headers = { 'Content-Type': 'application/json' };
      request.data = JSON.stringify(opts.body);
    }
    try {
      return await api.fetch(request);
    } catch (error) {
      let message = error && (error.message || error.statusText) || 'Anfrage fehlgeschlagen.';
      if (error && error.responseJSON) message = error.responseJSON.Message || error.responseJSON.message || message;
      throw new Error(message);
    }
  }

  function node(tag, className, text) {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;
    return element;
  }

  function defaultLabel(data) {
    if (data.DefaultMode === 'Catalog') return data.DefaultThemeName || 'Katalog-Theme';
    if (data.DefaultMode === 'CustomCss') return 'Eigenes Server-CSS';
    return 'Jellyfin Standard';
  }

  function showError(message) {
    q('warnings').appendChild(node('div', 'ts-notice ts-error', message));
  }

  async function load() {
    try {
      const data = await call('Catalog');
      state.data = data;
      state.themes = data.Themes || [];
      state.selected = data.SelectedThemeId || '';
      state.variables = data.Variables || {};
      state.allowed = !!data.AllowUserThemes;
      q('disabled').hidden = state.allowed;
      q('default-text').textContent = 'Serverstandard: ' + defaultLabel(data);
      (data.Warnings || []).forEach(showError);
      updateActive();
      render();
    } catch (error) {
      q('grid').innerHTML = '';
      q('grid').appendChild(node('div', 'ts-empty', 'Theme Store konnte nicht geladen werden: ' + error.message));
    }
  }

  function updateActive() {
    const current = state.themes.find((theme) => theme.Id === state.selected);
    q('active-name').textContent = current ? current.Name : defaultLabel(state.data);
    q('active').hidden = false;
    q('reset').hidden = !state.selected || !state.allowed;
  }

  function render() {
    const grid = q('grid');
    grid.innerHTML = '';
    const query = state.query.toLowerCase();
    const themes = state.themes.filter(function (theme) {
      return !query || [theme.Name, theme.Author, theme.Description, (theme.Tags || []).join(' ')]
        .join(' ')
        .toLowerCase()
        .includes(query);
    });
    if (!themes.length) {
      grid.appendChild(node('div', 'ts-empty', 'Keine passenden Themes gefunden.'));
      return;
    }

    themes.forEach(function (theme) {
      const card = node('article', 'ts-card' + (theme.Id === state.selected ? ' ts-card-selected' : ''));
      const preview = node('div', 'ts-preview');
      const pictures = theme.PreviewUrls || [];
      if (pictures.length) {
        const image = node('img');
        image.src = pictures[0];
        image.alt = 'Vorschau von ' + theme.Name;
        image.loading = 'lazy';
        image.referrerPolicy = 'no-referrer';
        image.addEventListener('error', function () { image.remove(); }, { once: true });
        preview.appendChild(image);
        if (pictures.length > 1) preview.appendChild(node('span', 'ts-count', pictures.length + ' Bilder'));
        preview.addEventListener('click', function () { openGallery(theme); });
      } else {
        preview.appendChild(node('div', 'ts-empty', 'Keine Vorschau'));
      }

      const body = node('div', 'ts-cardbody');
      body.appendChild(node('h2', '', theme.Name));
      body.appendChild(node('div', 'ts-meta', [theme.Author, theme.Version].filter(Boolean).join(' · ')));
      if (theme.Description) body.appendChild(node('p', '', theme.Description));
      const selected = theme.Id === state.selected;
      const configurable = (theme.Vars || []).length > 0;
      const button = node('button', 'ts-btn ts-btn-primary', selected && !configurable ? 'Ausgewählt' : (configurable ? 'Auswählen & anpassen' : 'Auswählen'));
      button.type = 'button';
      button.disabled = !state.allowed || (selected && !configurable);
      button.addEventListener('click', function () { choose(theme, button); });
      body.appendChild(button);
      card.append(preview, body);
      grid.appendChild(card);
    });
  }

  function choose(theme, button) {
    const values = {};
    const current = theme.Id === state.selected ? state.variables : {};
    for (const variable of theme.Vars || []) {
      const initial = current[variable.Key] !== undefined ? current[variable.Key] : (variable.Default || '');
      const answer = window.prompt((variable.Name || variable.Key) + (variable.Description ? '\n' + variable.Description : ''), initial);
      if (answer === null) return;
      values[variable.Key] = answer;
    }
    save(theme.Id, values, button);
  }

  async function save(id, variables, button) {
    button.disabled = true;
    button.textContent = 'Speichern …';
    try {
      await call('Preference', { method: 'PUT', body: { ThemeId: id, Variables: variables || {} }, json: false });
      state.selected = id;
      state.variables = variables || {};
      q('warnings').innerHTML = '';
      updateActive();
      render();
      window.dispatchEvent(new Event('theme-store:changed'));
    } catch (error) {
      button.disabled = false;
      button.textContent = 'Auswählen';
      showError(error.message);
    }
  }

  async function reset() {
    try {
      await call('Preference', { method: 'DELETE', json: false });
      state.selected = '';
      state.variables = {};
      q('warnings').innerHTML = '';
      updateActive();
      render();
      window.dispatchEvent(new Event('theme-store:changed'));
    } catch (error) {
      showError(error.message);
    }
  }

  function openGallery(theme) {
    state.gallery = theme.PreviewUrls || [];
    state.galleryIndex = 0;
    q('caption').textContent = theme.Name;
    q('gallery').hidden = false;
    showImage();
  }

  function closeGallery() {
    q('gallery').hidden = true;
    q('large-preview').removeAttribute('src');
  }

  function showImage() {
    if (!state.gallery.length) return;
    q('large-preview').src = state.gallery[state.galleryIndex];
    q('previous').hidden = state.gallery.length < 2;
    q('next').hidden = state.gallery.length < 2;
  }

  function move(delta) {
    state.galleryIndex = (state.galleryIndex + delta + state.gallery.length) % state.gallery.length;
    showImage();
  }

  q('search').addEventListener('input', function () { state.query = q('search').value.trim(); render(); });
  q('reset').addEventListener('click', reset);
  q('close-gallery').addEventListener('click', closeGallery);
  q('previous').addEventListener('click', function () { move(-1); });
  q('next').addEventListener('click', function () { move(1); });
  q('gallery').addEventListener('click', function (event) { if (event.target === q('gallery')) closeGallery(); });
  view.addEventListener('keydown', function (event) {
    if (q('gallery').hidden) return;
    if (event.key === 'Escape') closeGallery();
    if (event.key === 'ArrowLeft') move(-1);
    if (event.key === 'ArrowRight') move(1);
  });

  load();
}
