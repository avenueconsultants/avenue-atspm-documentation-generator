"""Validate committed generated docs before building a Pages preview or deployment."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import subprocess

from prepare_documentation_pr import child, relative_path, files
from site_configuration import get_value, load_map


def validate(repository: Path, config_name: str, output_name: str,
             repository_url: str, docs_revision: str, generator_ref: str,
             require_committed: bool = True) -> str:
    repository = repository.resolve()
    config_name = relative_path(config_name)
    output_name = relative_path(output_name)
    config = child(repository, config_name)
    output = child(repository, output_name)
    if not config.is_file() or not output.is_dir() or output == config or output in config.parents or config in output.parents:
        raise ValueError("Configuration and generated documentation must be separate committed paths.")
    if not re.fullmatch(r"[0-9a-fA-F]{40}", docs_revision) or not re.fullmatch(r"[0-9a-fA-F]{40}", generator_ref):
        raise ValueError("Documentation and generator revisions must be full commit SHAs.")
    if require_committed:
        head = subprocess.check_output(["git", "-C", str(repository), "rev-parse", "HEAD"], text=True).strip()
        if head.lower() != docs_revision.lower():
            raise ValueError("Checked-out documentation commit does not match the requested revision.")
        tracked = set(subprocess.check_output(
            ["git", "-C", str(repository), "ls-files", "--cached", "--", config_name, output_name],
            text=True).splitlines())
        if config_name not in tracked:
            raise ValueError("Documentation map is not committed.")
        changed = subprocess.check_output(
            ["git", "-C", str(repository), "status", "--porcelain", "--untracked-files=all", "--", config_name, output_name],
            text=True).strip()
        if changed:
            raise ValueError("Documentation or map differs from the checked-out commit.")
    else:
        tracked = None

    mapping = load_map(config)
    containers = get_value(mapping, "containers")
    if not isinstance(containers, list) or not containers:
        raise ValueError("Documentation map has no containers.")
    logs = get_value(mapping, "logMessages")
    log_enabled = logs is not None and get_value(logs, "enabled", True)
    # Reviewed sites from before the layout change remain publishable.
    prefix = "support/" if (output / "support/.atspm-docs-generator.json").is_file() else ""
    expected = {"index.md", prefix + "configuration-changes.md", prefix + "configuration-snapshot.json",
                prefix + "configuration-templates.md", prefix + ".atspm-docs-generator.json"}
    if log_enabled:
        expected.add(prefix + "log-messages.md")
    for container in containers:
        slug = get_value(container, "slug")
        if not isinstance(slug, str) or not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", slug):
            raise ValueError(f"Invalid container slug: {slug!r}")
        expected.update({f"{slug}.md", f"{prefix}{slug}.example.json", f"{prefix}{slug}.env.example"})
    actual = set(files(output))
    if actual != expected:
        raise ValueError(f"Committed generated files differ from the map: missing={sorted(expected-actual)}, extra={sorted(actual-expected)}")
    if tracked is not None:
        uncommitted = {f"{output_name}/{name}" for name in expected} - tracked
        if uncommitted:
            raise ValueError(f"Generated files are not committed: {sorted(uncommitted)}")
    manifest = json.loads((output / (prefix + ".atspm-docs-generator.json")).read_text(encoding="utf-8"))
    if manifest.get("Generator") != "atspm-documentation-generator" or set(manifest.get("ManagedFiles", [])) != expected - {prefix + ".atspm-docs-generator.json"}:
        raise ValueError("Generator ownership manifest does not match the committed files.")
    snapshot = json.loads((output / (prefix + "configuration-snapshot.json")).read_text(encoding="utf-8"))
    source_revision = snapshot.get("sourceRevision")
    if snapshot.get("repositoryUrl", "").rstrip("/") != repository_url.rstrip("/") or not isinstance(source_revision, str) or not re.fullmatch(r"[0-9a-fA-F]{40}", source_revision):
        raise ValueError("Snapshot source repository or revision does not match the publishing repository.")
    for container in containers:
        slug = get_value(container, "slug")
        json.loads((output / f"{prefix}{slug}.example.json").read_text(encoding="utf-8"))
    return (f"Documentation commit: {docs_revision}\n"
            f"Documented source commit: {source_revision}\n"
            f"Generator commit: {generator_ref}\n"
            f"Repository: {repository_url}\n"
            f"Map: {config_name}\nGenerated files: {len(expected)}\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True, type=Path)
    parser.add_argument("--config", default="documentation.json")
    parser.add_argument("--output", default="docs/autogenerated")
    parser.add_argument("--repository-url", required=True)
    parser.add_argument("--docs-revision", required=True)
    parser.add_argument("--generator-ref", required=True)
    parser.add_argument("--review-file", required=True, type=Path)
    args = parser.parse_args()
    review = validate(args.repository, args.config, args.output, args.repository_url,
                      args.docs_revision, args.generator_ref)
    args.review_file.write_text(review, encoding="utf-8")
    print(review, end="")


if __name__ == "__main__":
    main()
