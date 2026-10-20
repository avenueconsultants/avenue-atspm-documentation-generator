# Part 5: configuration change reports

Implemented on `feature/generic-documentation`. The report is used by the
[shared PR automation](shared-pr-automation.md). Production publishing remains
separate.

## Output and comparison semantics

Generation always produces `configuration-snapshot.json` and
`configuration-changes.md` alongside the existing pages. The home page and MkDocs
navigation link to the report; the report links to the downloadable snapshot.

Snapshot schema version 1 contains captured source provenance, effective section
bindings, affected services, source locations, supported property metadata and the
latest structured comparison. It stores source initializer expressions, not runtime
configuration. Named bindings get their actual environment-variable prefixes.
Unmapped discoveries are retained with no service assignment and flagged in the report.

The first run creates an initial baseline without inventing historical additions.
Subsequent changes compare to the snapshot in the existing output directory. The
report includes previous/current revisions and repository links. Each new change
replaces the last comparison. With no modeled change, the old snapshot and report
are retained, including source provenance, even for a later application commit.
This avoids clearing a useful report on the next run or creating a new one for
source-line moves, reordered properties, documentation edits or unrelated changes.

Tracked differences: sections/settings added or removed, types, default expressions,
required status, enum member names, environment-variable names, and service mappings.
New required settings show whether a source initializer exists. Renames appear as
removals/additions. Exclusions/mappings can affect the documented contract without
changing application code; the report explicitly explains this distinction.

The JSON report entries have `kind`, `section`, optional `property`, `before`, `after`,
`affectedServices`, and `environmentVariables`. The latter two include the union of
old/new values. The snapshot's `report.baselineRevision` and
`report.baselineRepositoryUrl` are null for an initial baseline; `report.changes` is
then empty. Consumers should check `schemaVersion` before reading this format.

## Reliability and compatibility

- Missing/corrupt/unsupported previous snapshots fail before output replacement.
  A missing report alone can be regenerated from its valid snapshot.
- Legacy directories without snapshots migrate as an initial baseline. Their
  existing ownership policy still applies; handwritten files are not adopted.
- Snapshot/report files participate in the same staged output transaction as pages.
- Snapshot and manifest JSON use LF line endings on Windows and Linux.
- `configuration-changes` is a reserved page slug to avoid filename collisions.
- Original service/log page formatting is unchanged. The frozen ATSPM fixtures are
  retained; the baseline checker verifies precisely the new index link, managed-file
  additions and initial snapshot/report rather than refreshing all expected output.

Keep the snapshot in the consuming repository. Automation should check out the
reviewed baseline before generating. Local output is the baseline for local runs;
there is no implicit Git-history fetch. Git provides history beyond the latest report.

## Validation

Tests exercise all supported differences, first-run behavior, source provenance,
named bindings, unmapped sections, stable ordering, repeated runs, unrelated commits,
reverts, legacy migration, invalid snapshots and failure without replacing output.
Rendered site tests verify the change table, affected environment names, source
revision links and downloadable JSON in strict MkDocs builds.

## Scope

The report does not validate actual deployment settings, execute default expressions,
infer runtime/CLI requirements, detect semantic renames, or evaluate nested-property
defaults/types and enum numeric assignments. No PR is posted or merged in this part.
See [README usage](../README.md#configuration-change-reports).
