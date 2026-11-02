#!/usr/bin/env python3
"""Add or replace one Jellyfin plugin release in manifest.json."""

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path

PLUGIN_GUID = "a9b5c3e1-7d42-4f86-9a31-6c2e8b7d4f10"
PLUGIN_NAME = "Jellyfin Recently Added Badges"
OWNER = "Brycen12345678"
TARGET_ABI = "12.0.0.0"


def version_key(value: str):
    return tuple(int(part) if part.isdigit() else part for part in value.split("."))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", required=True)
    parser.add_argument("--checksum", required=True)
    parser.add_argument("--timestamp", required=True)
    args = parser.parse_args()

    path = Path("manifest.json")
    manifest = json.loads(path.read_text(encoding="utf-8")) if path.exists() else []
    plugin = next((entry for entry in manifest if entry.get("guid") == PLUGIN_GUID), None)

    if plugin is None:
        plugin = {
            "guid": PLUGIN_GUID,
            "name": PLUGIN_NAME,
            "description": "Displays a New badge on recently added movies and TV series in Jellyfin.",
            "overview": "Adds configurable New badges to recently added movies and TV shows, including native-client poster images.",
            "owner": OWNER,
            "category": "General",
            "versions": [],
        }
        manifest.append(plugin)

    versions = plugin.setdefault("versions", [])
    versions[:] = [entry for entry in versions if entry.get("version") != args.version]
    versions.append({
        "version": args.version,
        "changelog": "Initial public release of Jellyfin Recently Added Badges.",
        "targetAbi": TARGET_ABI,
        "sourceUrl": (
            f"https://github.com/{OWNER}/JellyfinRecentlyAddedBadges/releases/download/"
            f"v{args.version}/JellyfinRecentlyAddedBadges_{args.version}.zip"
        ),
        "checksum": args.checksum.lower(),
        "timestamp": args.timestamp,
    })
    versions.sort(key=lambda entry: version_key(entry["version"]), reverse=True)
    manifest.sort(key=lambda entry: entry.get("name", "").casefold())
    path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
