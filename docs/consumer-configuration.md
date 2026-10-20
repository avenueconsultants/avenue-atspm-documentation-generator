# Part 2: repository-owned configuration

Implemented on `feature/generic-documentation`, retaining schema version 2 and the
existing CLI `--map` option. This part does not alter consumer repositories or
introduce new publishing/PR automation.

## Contract

The action accepts `config-file: documentation.json`, resolved
relative to `source-root`. Absolute paths also work. The action now defaults to
the root file; an explicitly empty input uses the bundled ATSPM map. Missing
map files fail before the build; paths with
line breaks are rejected because they cannot be represented as single-line step
outputs. Spaces in file paths are supported.

Source folders inside the map remain relative to the source checkout, independent
of the configuration file's directory. See [README setup](../README.md#repository-owned-configuration)
and the [example](../config/documentation.example.json).

The existing JSON Schema remains the editor contract. Its optional examples and
duplicate-ID allowlist now reflect the loader's existing empty defaults. Runtime
validation rejects malformed JSON, unknown members, invalid/null structures,
unsupported versions, duplicate names and conflicting filenames, with file context.
Reserved reference slugs and case-insensitive page filename collisions are rejected
instead of letting generated files overwrite each other.

The configuration file is not modified. `$schema` does not trigger network access.
The Part 1 frozen map and output are unchanged.

## Validation

- .NET test suite: 53 passing tests, including consumer-file generation, optional
  defaults, null structures, reserved filenames and malformed input diagnostics.
- `bash scripts/test-config-path.sh`: bundled default, relative and absolute paths,
  checkout paths with spaces, missing files, directories and newline rejection pass.
- `python scripts/check_baseline.py --source-root _source/baseline-atspm`: nine files
  match byte for byte.
- Repository navigation/vendor checks pass.
- CI now exercises both the legacy action and an explicit consumer-owned map and
  compares their complete output directories. Hosted CI has not been dispatched.

## Deliberate boundary

This section records the Part 2 boundary. [Part 3](generic-presentation.md) now
supports optional/multiple log folders and dynamic site navigation/branding.

The schema remains version 2: explicit container/section mappings and a required
log source. No discovery or hypothetical version-3 fields have been introduced.
MkDocs navigation is still fixed to ATSPM, so custom page lists must temporarily
use `validate-site: "false"`; configuration validation always runs. Part 3 removes
that presentation limitation. The current ATSPM page list still builds strictly.
