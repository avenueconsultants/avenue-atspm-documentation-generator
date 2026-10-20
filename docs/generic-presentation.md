# Part 3: generic pages and branding

Implemented on `feature/generic-documentation`. Consumer repositories and production
publishing remain untouched. Schema version 2 gains optional site settings and log
folder lists; existing ATSPM configurations still work.

## Changes

- A shared MkDocs `on_config` hook derives navigation from the consumer map and
  derives site/export branding from `productName` and optional `site` overrides.
- The action passes the selected map as `DOCS_CONFIG`, and the source repository
  URL as `DOCS_REPOSITORY_URL`. Local builds use the same map via that environment
  variable; the bundled map is the default.
- Fixed ATSPM navigation, copyright and export names were removed from the shared
  theme/configuration. The ATSPM-specific description and copyright moved to its map.
- Export text is serialized with Jinja's `tojson` filter, including HTML-sensitive
  characters, instead of interpolating raw configuration into JavaScript strings.
- `logMessages.sourcePaths` supports several folders; singular `sourcePath` still
  works. File overlaps are deduplicated, build directories excluded, and duplicate
  event IDs checked across all folders.
- Omitted `logMessages` or `enabled: false` disables logs, removes references and
  removes previously managed log output. Explicit `null` remains invalid.
- The configuration schema, example and README document these extensions.

See the [README](../README.md#site-branding-and-optional-logs) for exact fields.

## Verification

- 64 .NET tests pass, including cross-folder duplicate detection, overlapping
  paths, disabled logging/stale page cleanup, and invalid branding settings.
- Four Python tests pass, including strict MkDocs builds of a custom utility both
  with and without logs. Rendered HTML confirms custom navigation/title/filenames,
  escaped script-sensitive export titles, and local browser export assets.
- The nine pinned ATSPM baseline output files remain byte-identical. No snapshots
  were refreshed. Its strict MkDocs build and repository checks pass.
- Hosted CI now runs the generic site checks after the Release build and MkDocs
  dependency installation; hosted workflows have not been dispatched.

The HTML checks do not exercise actual PDF/Excel clicks in a browser. Export library
code and table extraction are unchanged; only the title/filename inputs changed.
The default ATSPM appearance is retained by the existing theme and navigation order.

## Next part

Part 4 adds discovery and setup. This part still requires explicit page/section
mapping and does not attempt to discover service ownership or alter consumer JSON.
