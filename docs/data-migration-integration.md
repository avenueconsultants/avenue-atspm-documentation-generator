# Data Migration generator integration

The generator-owned example at `integration/data-migration/documentation.json` is
checked against commit `cc29cf943d54a4c6de9373442e05f93cbe9f8f10` of
`opensourcetransportation/ATSPM-43-to-5-Data-Migration`. It generates three pages
for the command configuration types bound in `Program.cs`. Descriptions reflect
the corresponding CLI options. `EndIsDateOnly` is derived by command setup and
excluded from the configurable setting list. `propertyDescriptions` can supply
plain-language text where source properties have no XML comments; misspelled
section and property names fail `check`.

`logMessages.includeOrdinaryCalls` opts into `ILogger.LogInformation`,
`LogWarning`, `LogError`, `LogDebug`, `LogTrace` and `LogCritical` call sites.
Literal and interpolated message expressions are indexed with source links.
Computed messages are omitted. Ordinary calls have no declared event ID, so the
reference shows a dash and never invents one. This source currently produces
102 messages. The option defaults to false for existing consumers.

The generated `Required` column reflects property annotations, not CLI option
requirements or runtime validation. For example, the transfer commands require
`--source`, `--start` and `--end`, while the upgrade command can obtain them from
its configuration section. The generated `Default` column reflects property
initializers, not values in `appsettings.json` or CLI defaults. Review command
help and the repository's deployment configuration before running a migration.

The current map covers the three local, typed command sections. It does **not**
yet document all runtime configuration. `Program.cs` registers database contexts
from `Utah.Udot.Atspm.Infrastructure` package version 5.3.1. Its
`appsettings.json` contains `ConnectionStrings` with `ConfigContext`,
`AggregationContext`, `EventLogContext` and `IdentityContext` objects. Each has
`Provider` and `ConnectionString`; the generator has not resolved the package's
types into the configuration model. `ConfigurationMigrationService.cs` reads
`ColumnMappings` and `LocationQueries` directly through `IConfiguration`, so
there is no local POCO to discover for them either. These are explicit coverage
gaps for the remaining Part 10 work, not an indication that the settings are
unused. A future generic mechanism must identify each external or untyped
section's provenance and validate it against source/package changes.

Run the pinned integration check after building the generator and installing
`requirements.txt`:

```sh
python scripts/check_data_migration.py --source-root _source/data-migration
```

The check verifies the source revision, package version, bound sections,
descriptions, log coverage, strict MkDocs build and repeatable output. This
integration remains generator-side review material; it has not changed the
Data Migration repository or activated a consumer workflow.
