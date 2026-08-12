# Jellyfin Theme Store

A theme store for Jellyfin Web that lets every signed-in user choose a personal CSS theme without changing the appearance for anyone else. Administrators can also define a server-wide default: the standard Jellyfin interface, a catalog theme, or custom CSS.

This project is based on [Jellyfin-PG/Skin-Manager](https://github.com/Jellyfin-PG/Skin-Manager). Copyright, license, and dependency information is available under [Credits and license](#credits-and-license) and in [`NOTICE.md`](NOTICE.md).

## Features

- Theme Store entry in every user's normal Jellyfin settings
- Personal selections stored server-side and isolated by Jellyfin user ID
- A user's theme follows their account across browsers and devices
- Administrator-defined server default: Jellyfin, catalog theme, or custom CSS
- Multiple preview images per theme with a full-size gallery
- Search by name, author, description, and tags
- Configurable theme variables from JSON catalogs for administrators and users
- A simple human-readable catalog format plus compatibility with the existing JSON format
- Local CSS caching on the Jellyfin server
- Authenticated user APIs with separate administrator permissions
- Protection against arbitrary proxy URLs, private network targets, excessive redirects, and oversized downloads
- Support for Jellyfin installations hosted under a base URL

## Requirements and compatibility

- Jellyfin Server 10.11.x
- [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) 2.5.5 or newer
- A client that loads the Jellyfin Web frontend supplied by the server

Supported clients include regular web browsers and many Jellyfin applications that embed Jellyfin Web. Fully native clients such as Android TV do not load the server's web frontend, so the plugin cannot display the store or apply CSS in those clients.

Custom themes are intentionally disabled on login, setup, administrator, and Theme Store pages. This keeps a safe recovery interface available if a theme is broken.

## Installation from a Jellyfin plugin repository

After creating the first GitHub release, the repository URL will be:

```text
https://raw.githubusercontent.com/YOUR-GITHUB-NAME/Jellyfin-ThemeStore/main/manifest.json
```

1. Open `Dashboard → Plugins → Repositories` in Jellyfin.
2. Add the URL of this repository's `manifest.json`.
3. In the plugin catalog, install **File Transformation** first and **Theme Store** second.
4. Restart Jellyfin.
5. Open `Dashboard → Plugins → Theme Store` and configure the catalog and server default.

Before publishing this local project, replace `YOUR-GITHUB-NAME` and the remaining `OWNER` placeholders with the actual GitHub user or organization. For every `v*` tag, the release workflow automatically writes the release download URL, checksum, and image URL to `manifest.json`.

## Managing the theme catalog

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

Catalog rules:

- The text after `#` and before the first comma is the visible theme name.
- Any number of preview images may follow the theme name.
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

JSON catalogs may also declare the existing Skin Manager `vars` array. Users and administrators will be prompted to configure those values when selecting the theme.

## Usage

### Administrators

The following options are available under `Dashboard → Plugins → Theme Store`:

- Theme catalog URL
- Global server default
- Custom CSS
- Permission for users to choose personal themes
- Manual clearing of the local CSS cache

When personal themes are enabled, users without a personal selection continue to use the configured server default. When personal themes are disabled, the server default is enforced for everyone.

### Users

A **Theme Store** entry appears in the normal user settings. The selection is saved on the Jellyfin server under the authenticated user ID. Choosing **Use server default** removes only the current user's personal selection.

## Security and privacy

CSS themes become an active part of the interface. They can hide content or load external images and fonts, which may create connections to third-party servers. Only use catalogs and themes you trust.

The server downloads catalogs and CSS only through HTTP or HTTPS. It blocks obvious loopback, private, link-local, and local destinations; limits redirects, request time, and file size; and accepts only catalog theme IDs through the CSS endpoint. Preview images are loaded directly by the browser with a `no-referrer` policy.

The HTML shell for the user-facing store is publicly retrievable because ordinary browser navigation does not include Jellyfin's token header. It contains no catalog, user, or server data. Every data, CSS, preference, and administrative endpoint requires authentication; administrative operations additionally require elevated permissions.

## Development

```bash
dotnet restore
dotnet test Jellyfin.Plugin.ThemeStore.sln --configuration Release
dotnet publish Jellyfin.Plugin.ThemeStore.csproj --configuration Release --output publish
```

Create a release with a four-part Jellyfin plugin version tag:

```bash
git tag v1.0.0.0
git push origin v1.0.0.0
```

GitHub Actions runs the test suite, builds `ThemeStore_1.0.0.0.zip`, creates the GitHub release, and updates the newest version in `manifest.json`. The dependency on File Transformation is added automatically.

## Data and uninstalling

Personal theme assignments are stored in `user-themes.json`, and downloaded CSS files are stored in the plugin's data directory. Depending on the Jellyfin installation, this configuration or data directory may remain after uninstalling the plugin. For complete cleanup, stop Jellyfin and remove the Theme Store plugin data directory after uninstalling it.

## Credits and license

The original code was published as [Jellyfin-PG/Skin-Manager](https://github.com/Jellyfin-PG/Skin-Manager) by Jellyfin Plugin Group / Jellyfin SM. The source snapshot supplied for this fork contains the MIT License and the notice `Copyright (c) 2026 Jellyfin SM`; the complete text is preserved unchanged in [`LICENSE`](LICENSE). The original README described the project as GPL-3.0 despite the included license file. This repository follows and preserves the license text actually distributed with the source snapshot.

[File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) is maintained by IAmParadox27. It is a separately installed runtime dependency under GPL-3.0. Its source code and binaries are not bundled with this repository.

See [`NOTICE.md`](NOTICE.md) for additional attribution details.
