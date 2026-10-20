"""Generate a validated, documentation-only worktree for the reusable workflow.

No network writes, commits, or PR operations happen here. The workflow delegates
those operations to create-pull-request after this command succeeds.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def run(*args: str, env=None) -> str:
    result = subprocess.run(args, text=True, encoding="utf-8", stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, env=env, check=False, timeout=180)
    print(result.stdout, end="", flush=True)
    if result.returncode:
        raise RuntimeError(f"Command failed ({result.returncode}): {args[0]}\n{result.stdout}")
    return result.stdout.strip()


def relative_path(value: str) -> str:
    # These values also become git pathspecs and multiline action outputs.
    if not re.fullmatch(r"[A-Za-z0-9_. /-]+", value):
        raise ValueError("Paths must use simple repository-relative names without globs.")
    parts = value.split("/")
    if any(not part or part in (".", "..") or part.startswith(".") for part in parts):
        raise ValueError("Paths must be relative, non-hidden, and cannot contain traversal.")
    return str(PurePosixPath(value))


def child(root: Path, value: str) -> Path:
    path = root / relative_path(value)
    for candidate in (path, *path.parents):
        if candidate == root:
            break
        if candidate.is_symlink():
            raise ValueError(f"Symbolic links are not supported: {candidate}")
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError("Path escapes its repository.")
    return path


def files(root: Path) -> dict[str, bytes]:
    if not root.exists():
        return {}
    if not root.is_dir():
        raise ValueError(f"Expected a generated directory: {root}")
    result = {}
    for path in root.rglob("*"):
        if path.is_symlink():
            raise ValueError(f"Generated output must contain only regular files: {path}")
        if path.is_dir():
            if path.relative_to(root).as_posix() != "support":
                raise ValueError(f"Unexpected generated directory: {path}")
            continue
        if not path.is_file():
            raise ValueError(f"Generated output must contain only regular files: {path}")
        result[path.relative_to(root).as_posix()] = path.read_bytes()
    return result


def semantic_content(name: str, content: bytes, repository_url: str) -> bytes:
    name = PurePosixPath(name).name
    if not name.endswith(".md") or name == "configuration-changes.md":
        return content
    text = content.decode("utf-8").replace("\r\n", "\n")
    # Only the generated header and dedicated source links are volatile. Never
    # replace arbitrary SHAs/defaults in configuration tables or descriptions.
    text = re.sub(r"^<small>Generated on [^\n]+</small>$", "<generation-time>",
                  text, count=1, flags=re.MULTILINE)
    if name == "configuration-templates.md":
        text = re.sub(r"^Source revision: \[[^\]]+\]\([^\n]+\)\.$",
                      "Source revision: <revision>.", text, count=1, flags=re.MULTILINE)
    url = re.escape(repository_url.rstrip("/"))
    source = rf"(\]\({url}/blob/)[^/\s]+(/[^)\s]+#L\d+\))"
    lines = text.splitlines(keepends=True)
    for index, line in enumerate(lines):
        if line.startswith("[View source]("):
            lines[index] = re.sub(source, r"\1<revision>\2", line)
        elif name == "log-messages.md" and line.startswith("| "):
            # Source is the final column; preserve the message and its examples.
            lines[index] = re.sub(source + r"(?= \|\s*$)", r"\1<revision>\2", line)
    return "".join(lines).encode("utf-8")


def meaningful_change(before: dict[str, bytes], after: dict[str, bytes], url: str) -> bool:
    return before.keys() != after.keys() or any(
        semantic_content(name, value, url) != semantic_content(name, after[name], url)
        for name, value in before.items())


def branch_name(target_branch: str, output: str) -> str:
    identity = f"{target_branch}\n{output}".encode()
    return "automation/documentation-" + hashlib.sha256(identity).hexdigest()[:16]


def configuration_review(output: Path, revision: str, output_name: str,
                         previous_snapshot: bytes | None) -> str:
    """Put the actionable source-contract changes where PR reviewers will see them."""
    snapshot_bytes = (output / "support/configuration-snapshot.json").read_bytes()
    snapshot = json.loads(snapshot_bytes)
    report = snapshot["report"]
    location = f"{output_name}/support/configuration-changes.md"
    lines = ["## Configuration review", "",
             f"Detailed report: `{location}`. Review deployment values; source analysis cannot confirm what is deployed.", ""]
    if previous_snapshot == snapshot_bytes or snapshot["sourceRevision"] != revision:
        lines.append("No new modeled configuration changes in this run. The saved report still shows the last comparison.")
    elif report["baselineRevision"] is None:
        lines.append("Initial baseline: there is no earlier snapshot to compare. Review the full configuration reference.")
    elif not report["changes"]:
        lines.append("No modeled configuration changes in this run.")
    else:
        lines.append(f"{len(report['changes'])} configuration change(s) need review:")
        lines.append("")
        def one_line(value: str) -> str:
            return re.sub(r"\s+", " ", value).replace("`", "'")

        for change in report["changes"]:
            setting = change["section"] + (f".{change['property']}" if change["property"] else "")
            variables = ", ".join(change["environmentVariables"]) or "none listed"
            services = ", ".join(change["affectedServices"]) or "unmapped"
            before_value = change["before"] if change["before"] is not None else "(none)"
            after_value = change["after"] if change["after"] is not None else "(none)"
            # Keep source-authored text on one PR-body line.
            lines.append(f"- **{one_line(change['kind'])}**: `{one_line(setting)}`; "
                         f"`{one_line(before_value)}` → `{one_line(after_value)}`; "
                         f"environment: `{one_line(variables)}`; services: {one_line(services)}.")
    return "\n".join(lines) + "\n"


def prepare(args) -> bool:
    source, target = args.source.resolve(), args.target.resolve()
    if source == target:
        raise ValueError("Source and PR worktree must be separate checkouts.")
    config = child(source, args.config)
    target_config = child(target, args.config)
    output = child(target, args.output)
    if config.is_dir() or not config.is_file():
        raise ValueError(f"Consumer configuration not found: {config}")
    if target_config == output or output in target_config.parents or target_config in output.parents:
        raise ValueError("Configuration and generated output paths must not overlap.")
    old_map = config.read_bytes()
    before = files(output)
    generator = ["dotnet", str(ROOT / "src/GenerateDocuments/bin/Release/net8.0/AtspmDocsGenerator.dll")]
    sha = run("git", "-C", str(source), "rev-parse", "HEAD")
    timestamp = run("git", "-C", str(source), "show", "-s", "--format=%cI", "HEAD")
    target_sha = run("git", "-C", str(target), "rev-parse", "HEAD")
    if args.sync_container:
        run(*generator, "sync", "--source-root", str(source), "--map", str(config),
            "--container", args.sync_container, "--write")
    new_map = config.read_bytes()
    map_changed = old_map != new_map
    if map_changed and (not target_config.is_file() or target_config.read_bytes() != old_map):
        raise ValueError("Cannot sync a map that differs between source and target branches. Align it first.")
    run(*generator, "check", "--source-root", str(source), "--map", str(config))

    args.artifacts.mkdir(parents=True, exist_ok=False)
    with tempfile.TemporaryDirectory(prefix="documentation-pr-") as temporary:
        staging = Path(temporary)
        generated = staging / "generated"
        generated.mkdir()
        for name, content in before.items():
            (generated / name).parent.mkdir(parents=True, exist_ok=True)
            (generated / name).write_bytes(content)
        # The generator validates ownership and the previous snapshot before
        # replacing anything. Its workspace here is disposable staging only.
        run(*generator, "--workspace-root", str(staging), "--source-root", str(source),
            "--output-root", str(generated), "--map", str(config),
            "--repository-url", args.repository_url, "--repository-ref", sha,
            "--generated-at", timestamp)
        env = dict(os.environ, DOCS_DIR=str(generated), DOCS_CONFIG=str(config),
                   DOCS_REPOSITORY_URL=args.repository_url)
        run(sys.executable, "-m", "mkdocs", "build", "--strict", "--config-file",
            str(ROOT / "mkdocs.yml"), "--site-dir", str(args.artifacts / "site"), env=env)
        after = files(generated)
        changed = meaningful_change(before, after, args.repository_url) or map_changed
        shutil.copytree(generated, args.artifacts / "generated")
        shutil.copyfile(config, args.artifacts / "documentation.json")
        review = configuration_review(generated, sha, args.output,
                                      before.get("support/configuration-snapshot.json", before.get("configuration-snapshot.json")))
        report = (
            f"Generated from {args.repository_url}/commit/{sha}.\n\n"
            f"PR target: `{args.target_branch}` at `{target_sha}`.\n\n"
        ) + review + (
            "\nConfiguration checks and strict MkDocs validation passed. "
            "The workflow also provides a downloadable site preview.\n\n"
            f"Repository map updated: {'yes' if map_changed else 'no'}.\n\n"
            "This PR only proposes documentation and optional additive map updates. "
            "It does not change deployed configuration or publish a site.\n"
        )
        (args.artifacts / "pr-body.md").write_text(report, encoding="utf-8")
        if changed:
            # No target writes occur until discovery, generation and site validation succeed.
            output.mkdir(parents=True, exist_ok=True)
            for name in before.keys() - after.keys():
                (output / name).unlink()
            for name, content in after.items():
                (output / name).parent.mkdir(parents=True, exist_ok=True)
                (output / name).write_bytes(content)
            if map_changed:
                target_config.write_bytes(new_map)
    summary = f"Documentation: {'meaningful changes prepared' if changed else 'no meaningful changes'}."
    print(summary)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as stream:
            stream.write(summary + "\n\n" + report)
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as stream:
            stream.write(f"changed={str(changed).lower()}\n")
            stream.write(f"branch={branch_name(args.target_branch, args.output)}\n")
    return changed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--target", type=Path, required=True)
    parser.add_argument("--target-branch", required=True)
    parser.add_argument("--config", default="documentation.json")
    parser.add_argument("--output", default="docs/autogenerated")
    parser.add_argument("--sync-container", default="")
    parser.add_argument("--repository-url", required=True)
    parser.add_argument("--artifacts", type=Path, required=True)
    args = parser.parse_args()
    args.artifacts = args.artifacts.resolve()
    prepare(args)


if __name__ == "__main__":
    main()
