# Attribution

Jellyfin Theme Store is based on [Jellyfin-PG/Skin-Manager](https://github.com/Jellyfin-PG/Skin-Manager), originally published by the Jellyfin Plugin Group / Jellyfin SM.

The original source snapshot carried the MIT License and the copyright notice:

> Copyright (c) 2026 Jellyfin SM

That notice and the complete MIT license are preserved in `LICENSE`. This fork adds a dual-format theme catalog, account-bound per-user preferences, preview galleries, stricter authenticated APIs, safer remote fetching, a separate plugin identity, tests, and GitHub repository automation.

The separate [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) plugin by IAmParadox27 is required at runtime. It is not bundled with this repository and remains under its own GPL-3.0 license.

The default catalog is curated from [awesome-jellyfin/awesome-jellyfin](https://github.com/awesome-jellyfin/awesome-jellyfin). Its theme descriptions, upstream links, and preview-image links are used as catalog metadata. Theme CSS and screenshots are not copied into the plugin binary: they are referenced at the URLs published by their respective authors. Each theme remains subject to its own upstream license and terms. `themes/sources.json` records the detected license identifier or notes when the upstream repository does not declare one.
