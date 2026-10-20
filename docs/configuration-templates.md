# Downloadable configuration templates

Part 11 adds two generated files per configured page: `support/<slug>.example.json` and
`support/<slug>.env.example`. The page and the generated **Configuration templates**
reference link to both files. The reference identifies the source repository
and exact revision used for generation. JSON remains directly parseable as
application configuration, so revision metadata lives on that reference page
instead of becoming a fake configuration key. The environment file uses
`Section__Property` names, including indexed and nested suffixes discovered
from source. It is a list of example assignments, not a script to execute.

The downloadable templates include documented settings only. Strings and
credential-like names receive safe placeholders; numeric and Boolean examples
come from source initializers where possible. Nested object leaves and
collection entries use `REPLACE_ME` in the environment example. Neither format
reads real deployment files or secret stores. Users must replace placeholders
and review the relevant configuration reference before using either file.

Files are generated inside the same staged output transaction as Markdown and
the configuration snapshot. They are tracked in the ownership manifest, copied
to the MkDocs site, included in documentation PR review, and deterministic for
the same source revision. Existing generated directories without templates
upgrade through the normal ownership check. The PR comparison ignores a change
to the template reference's revision line alone while retaining changes to
actual template content.

This changes the generated documentation, not application or deployment
configuration. Consumer repositories require no new configuration keys.
