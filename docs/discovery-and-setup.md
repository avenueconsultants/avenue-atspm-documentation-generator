# Part 4: discovery and setup

Implemented on `feature/generic-documentation`. The current action and original
generation syntax remain compatible. No consumer repositories, branches, PRs or
publishing settings were changed.

## Behavior

- `init` writes a minimal consumer map in `documentation.json`.
  It scans registered roots, selects one automatic page, finds structured log
  folders, reports unannotated candidates, and never overwrites an existing map.
- `check` validates the map against source and reports unmapped sections, stale
  references, missing types/exclusions, log errors and missing descriptions. It
  fails coverage if no configuration sections are discovered. It never writes.
- `sync` previews additive assignments. It requires an explicit destination page
  for unmapped sections; `--write` applies them atomically. Existing configuration
  values and order are retained. No-op runs do not rewrite files; changed files are
  formatted as JSON without comments. Stale mappings are reported, not deleted.
- `autoDiscover: true` includes all non-excluded sections from registered folders
  on that page. New properties and sections require no JSON rewrite. Existing
  explicit sections retain order, named bindings avoid redundant base entries,
  and additional section names are sorted. No service ownership is inferred.
- `configurationTypes` registers namespace-qualified source types without requiring
  attributes or an application refactor. Partial classes are supported.
- `excludedProperties` removes selected top-level properties from reference rows,
  examples and environment-variable entries. `excludedSections` removes whole
  discovered sections. Typographical/stale exclusions fail clearly.
- Source traversal prunes generated, dependency and test folders and skips links.
  Explicit source roots must resolve inside the consumer; setup writes stay there too.

Schema version 2 gains these optional fields. The shared site hook still uses the
configured page names/slugs; automatic section selection happens in the C# engine.
See [README instructions](../README.md#setup-discovery-and-synchronization).

## Validation

Tests cover initialization and overwrite refusal, ignored folders, explicit types
with partial declarations, ambiguous simple names avoided by namespace registration,
exclusions, new source appearing on automatic pages, coverage failures, sync preview,
target selection, preservation of examples/branding, idempotence, stale references,
and write containment. An integration test initializes a consumer, checks coverage,
generates its automatic page and builds its site with strict MkDocs validation.

Validation passed: 75 .NET tests, five Python tests (including strict site builds),
repository navigation/vendor checks, and whitespace checks. The nine pinned ATSPM
output files remain byte-identical; snapshots were not refreshed. Hosted CI has
not been dispatched.

## Limits and next part

Discovery is limited to source in registered folders, recognized section attributes,
and explicitly registered types. Candidate class names are suggestions, not proof
of configuration bindings. Nested property exclusion paths, NuGet-only definitions,
runtime binding/validation inference, and ordinary log-call parsing are not added
here. `sync` does not discover folders outside configured roots or decide service
ownership. Zero-section initialization is a scaffold that needs manual registration.

Structured snapshots and configuration change reports are described in
[Part 5](configuration-change-reports.md). The shared PR workflow uses these
setup/check/sync commands as described in [Part 6](shared-pr-automation.md).
