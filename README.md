# Jellyfin Theme Store

A theme store for Jellyfin Web that lets every signed-in user choose a personal CSS theme without changing the appearance for anyone else. Administrators can also define a server-wide default: the standard Jellyfin interface, a catalog theme, or custom CSS.

This project is based on [Jellyfin-PG/Skin-Manager](https://github.com/Jellyfin-PG/Skin-Manager). Copyright, license, and dependency information is available under [Credits and license](#credits-and-license) and in [`NOTICE.md`](NOTICE.md).

## Features

- Theme Store entry in Jellyfin's hamburger menu for every signed-in user
- Personal selections stored server-side and isolated by Jellyfin user ID
- A user's theme follows their account across browsers and devices
- Administrator-defined server default: Jellyfin, catalog theme, or custom CSS
- Multiple preview images per theme with a full-size gallery
- Search by name, author, description, and tags
- Configurable theme variables from JSON catalogs for administrators and users
- A maintained catalog sourced from Awesome Jellyfin with complete theme variants and add-on combinations
- A simple human-readable catalog format plus compatibility with the existing JSON format
- Local CSS caching on the Jellyfin server
- Automatic recovery after delayed login, SPA navigation, mobile resume, or Jellyfin replacing the active style element
- Compatibility protection for Jellyfin's native Intro Skipper button, even when a selected theme overrides broad player or button styles
- Deterministic selection order with safe fallback from missing personal themes to the administrator default
- Authenticated user APIs with separate administrator permissions
- Protection against arbitrary proxy URLs, private network targets, excessive redirects, and oversized downloads
- Support for Jellyfin installations hosted under a base URL

## Requirements and compatibility

- Jellyfin Server 10.11.x
- [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) 2.5.5 or newer
- A client that loads the Jellyfin Web frontend supplied by the server

Supported clients include regular web browsers and many Jellyfin applications that embed Jellyfin Web. Fully native clients such as Android TV do not load the server's web frontend, so the plugin cannot display the store or apply CSS in those clients.

Custom themes are intentionally disabled on login, setup, administrator, and Theme Store pages. This keeps a safe recovery interface available if a theme is broken.

### Intro Skipper compatibility

Jellyfin 10.11 renders the skip prompt in Jellyfin Web from media segments supplied by plugins such as [Intro Skipper](https://github.com/intro-skipper/intro-skipper). Because Theme Store themes are deliberately loaded after Jellyfin's normal styles, a broad theme rule could otherwise hide or block that native button. Theme Store therefore adds a narrowly scoped compatibility layer for Jellyfin's direct `body > .skip-button-container` element. It restores only the visible button's layout, stacking, opacity, and pointer handling; Jellyfin's intentional `hide` and `skip-button-hidden` states remain untouched.

Theme Store does not generate intro segments or force Jellyfin to offer a skip action. If no button is created at all, finish Intro Skipper's analysis task, clear the client cache, and confirm that the client's Jellyfin skip option is set to **Ask to Skip**. Near the end of an item, Jellyfin may use **Up Next** instead of displaying a separate outro skip button. See Intro Skipper's [troubleshooting guide](https://github.com/intro-skipper/intro-skipper/wiki/Troubleshooting) and Jellyfin [skip options](https://github.com/intro-skipper/intro-skipper/wiki/Jellyfin-Skip-Options).

No Intro Skipper source code or optional GPL CSS is bundled with Theme Store. If you use Intro Skipper's **Inject CSS** option, that upstream stylesheet remains separately loaded through Jellyfin's Branding CSS and retains its upstream license.

## Install through Jellyfin

Copy the following **Theme Store repository URL** into Jellyfin:

```text
https://daseric.github.io/Jellyfin-ThemeStore/manifest.json
```

1. Sign in to Jellyfin as an administrator.
2. Open `Dashboard → Plugins → Repositories`.
3. Add a repository named **Jellyfin Theme Store**, paste the URL above, and save it.
4. Open the plugin catalog and install **File Transformation** first and **Theme Store** second.
5. Restart Jellyfin.
6. Open `Dashboard → Plugins → Theme Store Settings` and configure the catalog and server default.

You can also open the [Theme Store repository manifest](https://daseric.github.io/Jellyfin-ThemeStore/manifest.json) directly or read Jellyfin's [official plugin repository documentation](https://jellyfin.org/docs/general/server/plugins/#repositories).

The repository becomes installable after its first GitHub release. For every `v*` tag, the release workflow automatically writes the release download URL, checksum, and image URL to `manifest.json`.

## Included theme catalog

The default catalog is published together with the Jellyfin plugin repository at:

```text
https://daseric.github.io/Jellyfin-ThemeStore/catalog.json
```

It is curated from [Awesome Jellyfin's theme list](https://github.com/awesome-jellyfin/awesome-jellyfin/blob/main/THEMES.md). Theme CSS and screenshots remain hosted by their respective authors; this repository stores the metadata and the ordered combinations needed for complete variants. The initial catalog contains 18 upstream projects and 67 selectable entries, including Catppuccin flavors, Evergarden seasons, Ultrachromic presets, Scyfin colors, Flow colors, ZestyTheme colors, and other documented variants.

Each variant has its own permanent ID, so `Catppuccin - Mocha` and `Catppuccin - Latte` are independent selections. When an upstream option is only an add-on, the catalog declares both the base CSS and the add-on CSS in order. Standalone fragments that do not form a usable theme are not listed by themselves.

The curated definitions live in [`themes/sources.json`](themes/sources.json), while [`themes/catalog.json`](themes/catalog.json) is generated. To refresh descriptions and preview images from Awesome Jellyfin after reviewing upstream installation instructions, run:

```bash
python scripts/update_theme_catalog.py
python scripts/update_theme_catalog.py --check
```

The update is intentionally review-driven instead of scraping arbitrary installation snippets at runtime. This prevents an upstream README edit from silently changing every Jellyfin server immediately. Add or update explicit CSS URLs and variants in `themes/sources.json`, regenerate the catalog, review the diff, and create a new plugin release.

## Custom catalogs

An example catalog is included at [`themes/catalog.txt`](themes/catalog.txt). Each theme uses two lines:

```css
#THEME-NAME, THEME-PREVIEW1.png, THEME-PREVIEW2.jpg
@import url('https://link-to.theme/my-theme/my-theme.css');
```

Separate multiple themes with an empty line:

```css
#Ocean, previews/home.png, previews/details.webp
@import url('./css/ocean.css');

#Midnight, https://images.example.org/midnight-1.jpg
@import url('https://cdn.example.org/midnight/theme.css');
```

A named variant may contain multiple ordered imports. This is useful when an add-on requires a base theme:

```css
#Scyfin - OLED, https://example.org/scyfin-oled.png
@import url('https://cdn.example.org/scyfin-theme.css');
@import url('https://cdn.example.org/theme-oled.css');
```

Catalog rules:

- The text after `#` and before the first comma is the visible theme or variant name.
- Any number of preview images may follow the theme name.
- One or more `@import` lines may follow a header. They are loaded in order as one complete selection.
- Relative image and CSS paths are resolved against the catalog URL.
- Supported image formats depend on the browser; common formats include PNG, JPG/JPEG, WebP, GIF, AVIF, and SVG.
- Only absolute or correctly resolvable HTTP and HTTPS URLs are accepted.
- Lines beginning with `//` are comments.
- Invalid entries are skipped and reported on the administrator page.
- The built-in **Jellyfin Default** option does not require a catalog entry.

### Extended JSON format

Existing Skin Manager catalogs remain supported. Use `previewUrls` to provide multiple screenshots:

```json
[
  {
    "id": "ocean",
    "name": "Ocean",
    "author": "Theme Author",
    "description": "A blue theme for Jellyfin.",
    "version": "1.2.0",
    "cssUrl": "./css/ocean.css",
    "cssUrls": [
      "./css/ocean.css",
      "./css/ocean-compact-cards.css"
    ],
    "previewUrl": "./previews/ocean-home.png",
    "previewUrls": [
      "./previews/ocean-home.png",
      "./previews/ocean-details.png"
    ],
    "tags": ["dark", "blue"]
  }
]
```

If `id` is omitted, the plugin generates a stable ID from the theme name. An explicit and permanent `id` is recommended for catalogs intended for long-term use.

Use `cssUrls` for an ordered base-theme and variant/add-on combination. `cssUrl` remains supported for existing catalogs; when both are present, duplicate URLs are removed and `cssUrl` is treated as the first source.

JSON catalogs may also declare the existing Skin Manager `vars` array. Users and administrators will be prompted to configure those values when selecting the theme.

## Usage

### Administrators

The following options are available under `Dashboard → Plugins → Theme Store Settings`:

- Theme catalog URL
- Global server default
- Custom CSS
- Permission for users to choose personal themes
- Manual clearing of the local CSS cache

When personal themes are enabled, users without a personal selection continue to use the configured server default. When personal themes are disabled, the server default is enforced for everyone.

The lightweight active-theme state is never browser-cached. Jellyfin Web retries automatically while authentication is still starting, restores cached CSS after returning from the background, and rechecks the server state after navigation, reconnect, focus, or account changes. Existing CSS remains active while a replacement is downloaded, avoiding unnecessary flashes of the Jellyfin default interface.

### Users

A **Theme Store** entry appears directly in Jellyfin's hamburger menu for every signed-in user. It opens the preview gallery and personal theme picker without entering the administrator dashboard. The selection is saved on the Jellyfin server under the authenticated user ID. Choosing **Use server default** removes only the current user's personal selection.

## Security and privacy

CSS themes become an active part of the interface. They can hide content or load external images and fonts, which may create connections to third-party servers. Only use catalogs and themes you trust.

The server downloads catalogs and CSS only through HTTP or HTTPS. It blocks obvious loopback, private, link-local, and local destinations; limits redirects, request time, and file size; and accepts only catalog theme IDs through the CSS endpoint. Preview images are loaded directly by the browser with a `no-referrer` policy.

The HTML shell for the user-facing store is publicly retrievable because ordinary browser navigation does not include Jellyfin's token header. It contains no catalog, user, or server data. Every data, CSS, preference, and administrative endpoint requires authentication; administrative operations additionally require elevated permissions.

## Development

```bash
dotnet restore
dotnet test Jellyfin.Plugin.ThemeStore.sln --configuration Release
node --test tests/injection.lifecycle.test.mjs
dotnet publish Jellyfin.Plugin.ThemeStore.csproj --configuration Release --output publish
```

Create a release with a four-part Jellyfin plugin version tag:

```bash
git tag v1.0.0.0
git push origin v1.0.0.0
```

GitHub Actions runs the test suite, builds `ThemeStore_1.0.0.0.zip`, creates the GitHub release, updates the newest version in `manifest.json`, and publishes both `manifest.json` and `catalog.json` through GitHub Pages. The dependency on File Transformation is added automatically.

Before the first Pages deployment, select **GitHub Actions** under `Settings → Pages → Build and deployment → Source`. After a successful release, the same workflow publishes the generated repository manifest as `/manifest.json`.

## Data and uninstalling

Personal theme assignments are stored in `user-themes.json`, and downloaded CSS files are stored in the plugin's data directory. Depending on the Jellyfin installation, this configuration or data directory may remain after uninstalling the plugin. For complete cleanup, stop Jellyfin and remove the Theme Store plugin data directory after uninstalling it.

## Credits and license

The original code was published as [Jellyfin-PG/Skin-Manager](https://github.com/Jellyfin-PG/Skin-Manager) by Jellyfin Plugin Group / Jellyfin SM. The source snapshot supplied for this fork contains the MIT License and the notice `Copyright (c) 2026 Jellyfin SM`; the complete text is preserved unchanged in [`LICENSE`](LICENSE). The original README described the project as GPL-3.0 despite the included license file. This repository follows and preserves the license text actually distributed with the source snapshot.

[File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) is maintained by IAmParadox27. It is a separately installed runtime dependency under GPL-3.0. Its source code and binaries are not bundled with this repository.

See [`NOTICE.md`](NOTICE.md) for additional attribution details.

The included catalog metadata and screenshots are derived from [awesome-jellyfin/awesome-jellyfin](https://github.com/awesome-jellyfin/awesome-jellyfin). Every catalog card identifies the upstream author and license where one is declared. The third-party themes are fetched from their authors' URLs and are not bundled into the plugin binary; each remains subject to its own upstream license and terms.
