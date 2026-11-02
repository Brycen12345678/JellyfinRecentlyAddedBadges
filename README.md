# Jellyfin Recently Added Badges

A Jellyfin Server 12 plugin that displays a **New** badge on recently added movies and TV series.

## Planned features

- Per-library enable/disable controls.
- Configurable badge background colour, corner position, and age window.
- Fixed `New` badge text and styling designed to complement Jellyfin Enhanced.
- Native-client poster compositing that leaves original media artwork untouched.
- Web poster badges integrated with the poster overlay layout.
- First-run detection of existing items within the configured Date Added window.

## Current development status

- [x] Initial .NET 10 / Jellyfin 12 plugin project.
- [x] Plugin entry point and persistent configuration model.
- [x] Dashboard configuration page with live Jellyfin library list.
- [x] Settings for enabled libraries, badge colour, position, and duration.
- [ ] Recently-added item detection and cache invalidation.
- [ ] Web poster badge integration and stacking with Jellyfin Enhanced.
- [ ] Native-client poster image compositing.
- [ ] Automated release ZIP and Jellyfin plugin repository manifest.

## Compatibility

- Jellyfin Server 12.x (plugin ABI 12.0.0.0).
- .NET 10.

## Build

The GitHub Actions workflow builds the plugin on pushes to `main` and pull requests. The server itself does not need the .NET SDK.

The plugin is under development; no installable release is available yet.
