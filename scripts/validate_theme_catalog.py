#!/usr/bin/env python3
"""Validate the generated catalog structure and optionally all remote resources."""

from __future__ import annotations

import argparse
import concurrent.futures
import json
import sys
import urllib.error
import urllib.request
from pathlib import Path
from urllib.parse import urlparse


ROOT = Path(__file__).resolve().parents[1]
CATALOG_PATH = ROOT / "themes" / "catalog.json"


def validate_https(url: str, label: str, errors: list[str]) -> None:
    parsed = urlparse(url)
    if parsed.scheme != "https" or not parsed.netloc:
        errors.append(f"{label}: expected an absolute HTTPS URL, got {url!r}")


def check_remote(item: tuple[str, str]) -> str | None:
    kind, url = item
    request = urllib.request.Request(url, method="HEAD", headers={"User-Agent": "Jellyfin-ThemeStore catalog validator"})
    try:
        with urllib.request.urlopen(request, timeout=25) as response:
            content_type = response.headers.get_content_type()
    except urllib.error.HTTPError as error:
        if error.code not in (403, 405):
            return f"{kind} URL returned HTTP {error.code}: {url}"
        request = urllib.request.Request(
            url,
            headers={"User-Agent": "Jellyfin-ThemeStore catalog validator", "Range": "bytes=0-0"},
        )
        try:
            with urllib.request.urlopen(request, timeout=25) as response:
                content_type = response.headers.get_content_type()
        except Exception as fallback_error:  # noqa: BLE001 - validation reports the remote failure
            return f"{kind} URL failed: {url} ({fallback_error})"
    except Exception as error:  # noqa: BLE001 - validation reports the remote failure
        return f"{kind} URL failed: {url} ({error})"

    if kind == "preview" and not content_type.startswith("image/"):
        return f"Preview URL returned {content_type}, not an image: {url}"
    if kind == "css" and content_type not in ("text/css", "text/plain", "application/octet-stream"):
        return f"CSS URL returned unexpected content type {content_type}: {url}"
    return None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--network", action="store_true", help="also check every CSS and preview URL")
    args = parser.parse_args()

    catalog = json.loads(CATALOG_PATH.read_text(encoding="utf-8"))
    errors: list[str] = []
    ids: set[str] = set()
    remotes: set[tuple[str, str]] = set()
    for position, theme in enumerate(catalog, 1):
        theme_id = str(theme.get("id", "")).strip()
        if not theme_id:
            errors.append(f"Entry {position} has no id")
        elif theme_id.casefold() in ids:
            errors.append(f"Duplicate id: {theme_id}")
        ids.add(theme_id.casefold())

        if not str(theme.get("name", "")).strip():
            errors.append(f"Entry {theme_id or position} has no name")
        if not str(theme.get("license", "")).strip():
            errors.append(f"Entry {theme_id or position} has no license information")

        validate_https(theme.get("sourceUrl", ""), f"Source URL for {theme_id}", errors)
        css_urls = theme.get("cssUrls", [])
        if not css_urls:
            errors.append(f"Entry {theme_id or position} has no cssUrls")
        if css_urls and theme.get("cssUrl") != css_urls[0]:
            errors.append(f"Entry {theme_id or position} cssUrl is not the first cssUrls item")
        for url in css_urls:
            validate_https(url, f"CSS URL for {theme_id}", errors)
            remotes.add(("css", url))

        previews = theme.get("previewUrls", [])
        if not previews:
            errors.append(f"Entry {theme_id or position} has no previews")
        for url in previews:
            validate_https(url, f"Preview URL for {theme_id}", errors)
            remotes.add(("preview", url))

    if args.network and not errors:
        with concurrent.futures.ThreadPoolExecutor(max_workers=12) as executor:
            for failure in executor.map(check_remote, sorted(remotes)):
                if failure:
                    errors.append(failure)

    if errors:
        print("\n".join(f"ERROR: {error}" for error in errors), file=sys.stderr)
        return 1

    suffix = f", {len(remotes)} remote resources reachable" if args.network else ""
    print(f"Validated {len(catalog)} catalog entries{suffix}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
