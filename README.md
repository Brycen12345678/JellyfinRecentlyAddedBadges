# Jellyfin Recently Added Badges

A Jellyfin Server 12 plugin that displays a **New** badge on recently added movies and TV series.

## Features

- Choose which Jellyfin libraries display badges.
- Configure the badge background colour.
- Place the badge in any of the four poster corners.
- Choose how many days after Date Added the badge remains visible (1–365 days).
- Fixed badge text: `New`.
- First-run behavior is automatic: existing movies and series inside the configured Date Added window are eligible.
- Web badges use Jellyfin Enhanced's quality badge container when it occupies the same corner, placing **New** first so the quality chips move down instead of overlapping.
- Native-client Primary poster requests are composited in memory. Original media artwork files are never modified.

## Compatibility

- Jellyfin Server 12.x (plugin ABI `12.0.0.0`).
- .NET 10.
- The plugin is built and packaged by GitHub Actions; the Jellyfin server does not need the .NET SDK.

## Install from the Jellyfin plugin catalog

After the first GitHub release has been published:

1. Open **Dashboard → Plugins → Repositories**.
2. Add this repository URL:
   `https://raw.githubusercontent.com/Brycen12345678/JellyfinRecentlyAddedBadges/main/manifest.json`
3. Open the Catalog, find **Jellyfin Recently Added Badges**, and install it.
4. Restart Jellyfin.
5. Open the plugin's configuration page and select the libraries and badge settings.

## Development

The GitHub Actions workflow validates the web script and manifest updater, restores, builds, packages, and uploads an artifact on pushes and pull requests. Releases are published from a branch named `release/<version>` (for example, `release/v0.1.0.0`). The workflow creates the matching Git tag and GitHub release, then updates `manifest.json` with the release ZIP and its MD5 checksum.

The plugin is still early-stage. Native rendering has passed compilation and packaging, but should be tested against the actual Jellyfin clients in use before treating every client as verified.
