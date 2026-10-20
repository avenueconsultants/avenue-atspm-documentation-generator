"""Regenerate pinned ATSPM and compare legacy pages plus explicit Part 5 additions.

Usage: python scripts/check_baseline.py --source-root PATH
Build the Release generator first. This command never updates approved snapshots.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess
import tempfile


ROOT = Path(__file__).resolve().parents[1]
BASELINE = ROOT / "tests" / "baselines" / "atspm"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", required=True, type=Path)
    parser.add_argument("--map", type=Path, default=BASELINE / "container-config-map.json",
                        help="Optional consumer map to verify against the frozen ATSPM output.")
    args = parser.parse_args()
    source = args.source_root.resolve()
    metadata = json.loads((BASELINE / "metadata.json").read_text(encoding="utf-8"))
    git = ["git", "-c", f"safe.directory={source.as_posix()}", "-C", str(source)]
    revision = subprocess.check_output(git + ["rev-parse", "HEAD"], text=True).strip()
    if revision != metadata["sourceRevision"]:
        raise SystemExit(f"Expected source revision {metadata['sourceRevision']}; got {revision}")
    if subprocess.check_output(git + ["status", "--porcelain"], text=True).strip():
        raise SystemExit("Baseline source checkout must be clean.")
    executable = ROOT / "src/GenerateDocuments/bin/Release/net8.0/AtspmDocsGenerator.dll"
    if not executable.is_file():
        raise SystemExit("Build first: dotnet build GenerateDocuments.sln --configuration Release")
    # A fresh temporary workspace makes the comparison independent of stale output.
    with tempfile.TemporaryDirectory(prefix="atspm-baseline-") as temporary:
        output = Path(temporary) / "generated"
        subprocess.run([
            "dotnet", str(executable),
            "--workspace-root", temporary,
            "--source-root", str(source),
            "--output-root", str(output),
            "--map", str(args.map.resolve()),
            "--repository-url", metadata["sourceRepository"],
            "--repository-ref", metadata["sourceRevision"],
            "--generated-at", metadata["sourceTimestamp"],
        ], check=True)
        expected = {p.name: p.read_bytes() for p in (BASELINE / "expected").iterdir() if p.is_file()}
        actual = {p.name: p.read_bytes() for p in output.iterdir() if p.is_file()}
        # Keep the original snapshots frozen while checking additive references.
        expected["index.md"] += (b"- [Configuration changes](configuration-changes.md)\n"
                                 b"- [Configuration templates](configuration-templates.md)\n")
        for name in [item for item in expected if item.endswith(".md") and item != "index.md" and item != "log-messages.md"]:
            suffix = b"\n## Download templates\n"
            if suffix not in actual[name]:
                raise SystemExit(f"Missing template downloads in {name}")
            actual[name] = actual[name].split(suffix, 1)[0]
        template_page = actual.pop("configuration-templates.md").decode()
        assert metadata["sourceRevision"] in template_page
        for container in json.loads(args.map.read_text(encoding="utf-8"))["containers"]:
            slug = container["slug"]
            assert f"{slug}.example.json" in template_page
            assert f"{slug}.env.example" in template_page
            assert json.loads(actual.pop(f"{slug}.example.json"))
            assert actual.pop(f"{slug}.env.example").strip()
        manifest = json.loads(expected[".atspm-docs-generator.json"])
        manifest["ManagedFiles"] = sorted(manifest["ManagedFiles"] + [
            "configuration-changes.md", "configuration-snapshot.json", "configuration-templates.md"]
            + [f"{container['slug']}.{extension}" for container in json.loads(args.map.read_text(encoding="utf-8"))["containers"]
               for extension in ("example.json", "env.example")])
        expected[".atspm-docs-generator.json"] = (json.dumps(manifest, indent=2) + "\n").encode()
        snapshot = json.loads(actual.pop("configuration-snapshot.json"))
        report = actual.pop("configuration-changes.md").decode()
        assert snapshot["schemaVersion"] == 1
        assert snapshot["sourceRevision"] == metadata["sourceRevision"]
        assert snapshot["report"]["baselineRevision"] is None
        assert snapshot["report"]["changes"] == []
        assert snapshot["sections"]
        assert "Initial baseline established" in report
        differences = sorted(name for name in expected.keys() | actual.keys()
                             if expected.get(name) != actual.get(name))
        if differences:
            raise SystemExit("Baseline differs: " + ", ".join(differences))
        print("Baseline matches: legacy service/log content preserved; additive template files and snapshot/report verified.")


if __name__ == "__main__":
    main()
