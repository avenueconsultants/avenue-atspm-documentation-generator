# Pages preview and publication

Part 12 adds the shared workflow `.github/workflows/documentation-pages.yml` and
an optional [consumer caller](../integration/publish-documentation-pages.yml).
The caller is a small second workflow in the source repository. It watches
commits to generated documentation on the reviewed branch and can also be run
manually. Its `publish: true` example publishes after reviewed files are
committed. The shared workflow itself still defaults to `publish: false`.

The Pages workflow checks out the **exact consumer commit that triggered it**
and the exact commit of the generator workflow GitHub selected. It does not regenerate documentation. Before
building, it verifies that the map and every generated file are committed and
unchanged, the ownership manifest agrees with the map, and the snapshot names
the same source repository and a full source commit. It then builds that
committed documentation with strict MkDocs validation. The preview artifact
contains the rendered site and a review note identifying the documentation,
documented source and generator commits. It can be inspected without enabling
GitHub Pages.

To adopt publishing, copy the caller to
`.github/workflows/publish-documentation-pages.yml` and set `docs-branch` to the
branch that will receive the generated-documentation PR. After the PR is
reviewed and merged into that branch, the caller builds and publishes from
those committed files. The earlier generation preview cannot publish a site.
The example follows generator `main`; replace its one `@main` reference with a
full commit SHA to freeze the generator version. The workflow checks out its
supporting scripts at the same resolved commit automatically.

Before adopting the publishing caller, configure the repository's Pages source
as **GitHub Actions** and protect the `github-pages` environment for the
allowed publishing branch. The example caller already grants `pages: write`,
`id-token: write` and `contents: read`, and sets `publish: true`. Change it to
`false` if the consumer should produce preview artifacts only.
The shared workflow packages a Pages artifact and runs `actions/deploy-pages`
only with that explicit input. A protected environment can require approval
before the deployment job starts. No repository has been configured or
published by this Part 12 change. See GitHub's [custom Pages workflow
guidance](https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site)
and [deployment environment
rules](https://docs.github.com/en/actions/concepts/workflows-and-actions/deployment-environments).

The site is rebuilt from committed Markdown, JSON templates and the
repository-owned map. A source push proposes a docs PR through the separate
generation workflow; it does not alter the public site until reviewed docs
are merged.
