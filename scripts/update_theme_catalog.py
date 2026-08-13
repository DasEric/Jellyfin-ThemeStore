#!/usr/bin/env python3
"""Build themes/catalog.json from Awesome Jellyfin plus curated CSS variants."""

from __future__ import annotations

import argparse
import html
import json
import re
import sys
import urllib.request
from pathlib import Path
from urllib.parse import urlparse


ROOT = Path(__file__).resolve().parents[1]
SOURCES_PATH = ROOT / "themes" / "sources.json"
CATALOG_PATH = ROOT / "themes" / "catalog.json"


def download_text(url: str) -> str:
    request = urllib.request.Request(url, headers={"User-Agent": "Jellyfin-ThemeStore catalog builder"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read().decode("utf-8")


def plain_markdown(value: str) -> str:
    value = re.sub(r"!\[[^]]*\]\([^)]*\)", "", value)
    value = re.sub(r"\[([^]]+)\]\([^)]*\)", r"\1", value)
    value = re.sub(r"[`*_>]", "", value)
    return " ".join(html.unescape(value).split()).strip()


def direct_github_asset_url(value: str) -> str:
    match = re.match(r"https://github\.com/([^/]+)/([^/]+)/(?:blob|raw)/([^/]+)/(.+?)(?:\?raw=true)?$", value)
    if not match:
        return value
    owner, repository, branch, path = match.groups()
    return f"https://raw.githubusercontent.com/{owner}/{repository}/{branch}/{path}"


def parse_awesome(markdown: str) -> dict[str, dict]:
    matches = list(re.finditer(r"^## \[([^]]+)\]\([^)]*\) by (.+)$", markdown, re.MULTILINE))
    themes: dict[str, dict] = {}
    for index, match in enumerate(matches):
        block_end = matches[index + 1].start() if index + 1 < len(matches) else len(markdown)
        block = markdown[match.end():block_end]
        name = plain_markdown(match.group(1))
        author = plain_markdown(match.group(2))
        source_match = re.search(r"Get this Theme\s*`?\]\((https://github\.com/[^)]+)\)", block, re.IGNORECASE)
        images = [
            direct_github_asset_url(html.unescape(value.strip()))
            for value in re.findall(r"<img\s+src=[\"']([^\"']+)", block, re.IGNORECASE)
        ]
        description_lines = []
        for line in block.splitlines():
            stripped = line.strip()
            if not stripped or stripped.startswith("<"):
                continue
            if stripped == "---":
                continue
            if "Get this Theme" in stripped:
                stripped = re.sub(
                    r"\s*\[`[^]]*Get this Theme[^]]*`\]\([^)]+\)\s*$",
                    "",
                    stripped,
                    flags=re.IGNORECASE,
                ).strip()
                if not stripped:
                    continue
            description_lines.append(plain_markdown(stripped))
        themes[name.casefold()] = {
            "name": name,
            "author": author,
            "description": " ".join(part for part in description_lines if part),
            "sourceUrl": source_match.group(1).rstrip("/") if source_match else "",
            "previewUrls": images,
        }
    return themes


def validate_url(value: str, label: str) -> None:
    parsed = urlparse(value)
    if parsed.scheme != "https" or not parsed.netloc:
        raise ValueError(f"{label} must be an absolute HTTPS URL: {value!r}")


def build_catalog(sources: dict, awesome: dict[str, dict]) -> list[dict]:
    result = []
    ids: set[str] = set()
    for source in sources["themes"]:
        source_name = source["sourceName"]
        metadata = awesome.get(source_name.casefold())
        if metadata is None:
            raise ValueError(f"Theme {source_name!r} no longer exists in Awesome Jellyfin")
        if not metadata["sourceUrl"]:
            raise ValueError(f"Theme {source_name!r} has no upstream repository link")

        base_css = source.get("baseCssUrl")
        source_tags = [source_name, *source.get("tags", [])]
        for variant in source["variants"]:
            theme_id = variant["id"]
            if theme_id.casefold() in ids:
                raise ValueError(f"Duplicate theme id: {theme_id}")
            ids.add(theme_id.casefold())

            css_urls = list(variant.get("cssUrls", []))
            if not css_urls and base_css:
                css_urls.append(base_css)
            if variant.get("addonCssUrl"):
                css_urls.append(variant["addonCssUrl"])
            if not css_urls:
                raise ValueError(f"Theme variant {theme_id!r} has no CSS source")
            for css_url in css_urls:
                validate_url(css_url, f"CSS URL for {theme_id}")

            previews = metadata["previewUrls"]
            if "previewIndexes" in variant:
                previews = [previews[position] for position in variant["previewIndexes"]]
            for preview in previews:
                validate_url(preview, f"Preview URL for {theme_id}")

            description = metadata["description"] or f"A Jellyfin theme by {metadata['author']}."
            if variant["name"].casefold() != source_name.casefold():
                description = f"{description} Variant: {variant['name']}."

            result.append({
                "id": theme_id,
                "name": variant["name"],
                "author": metadata["author"],
                "description": description,
                "version": "awesome-main",
                "sourceUrl": metadata["sourceUrl"],
                "cssUrl": css_urls[0],
                "cssUrls": css_urls,
                "previewUrls": previews,
                "tags": list(dict.fromkeys([*source_tags, *variant.get("tags", [])])),
                "license": source.get("license", "See upstream repository"),
            })
    return result


def render_catalog(catalog: list[dict]) -> str:
    return json.dumps(catalog, ensure_ascii=False, indent=2) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="fail if catalog.json is not current")
    parser.add_argument("--awesome-file", type=Path, help="use a local THEMES.md instead of downloading it")
    args = parser.parse_args()

    sources = json.loads(SOURCES_PATH.read_text(encoding="utf-8"))
    markdown = args.awesome_file.read_text(encoding="utf-8") if args.awesome_file else download_text(sources["awesomeThemesUrl"])
    catalog = build_catalog(sources, parse_awesome(markdown))
    output = render_catalog(catalog)

    if args.check:
        if not CATALOG_PATH.exists() or CATALOG_PATH.read_text(encoding="utf-8") != output:
            print("themes/catalog.json is out of date; run scripts/update_theme_catalog.py", file=sys.stderr)
            return 1
        print(f"Catalog is current: {len(catalog)} entries")
        return 0

    CATALOG_PATH.write_text(output, encoding="utf-8", newline="\n")
    print(f"Wrote {len(catalog)} entries to {CATALOG_PATH.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
