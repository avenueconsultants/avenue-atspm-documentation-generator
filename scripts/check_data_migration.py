"""Check the pinned Data Migration source against the generator-owned example map."""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import re
import sys
import tempfile

from prepare_documentation_pr import ROOT, files, run

REVISION = "cc29cf943d54a4c6de9373442e05f93cbe9f8f10"
REPOSITORY = "https://github.com/opensourcetransportation/ATSPM-43-to-5-Data-Migration"
MAP = ROOT / "integration/data-migration/documentation.json"


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    source = parser.parse_args().source_root.resolve()
    git = ["git", "-c", f"safe.directory={source.as_posix()}", "-C", str(source)]
    require(run(*git, "rev-parse", "HEAD") == REVISION, "Use the pinned Data Migration commit.")
    require(not run(*git, "status", "--porcelain", "--", "Commands", "Services", "Program.cs", "DataMigrator.csproj"),
            "Migration application sources must be unchanged.")
    project = (source / "DataMigrator.csproj").read_text(encoding="utf-8")
    require('Include="Utah.Udot.Atspm.Infrastructure" Version="5.3.1"' in project,
            "External configuration dependency changed; review provenance.")
    program = (source / "Program.cs").read_text(encoding="utf-8")
    for section in ("TransferConfigCommandConfiguration", "MigrationCommandConfiguration", "UpgradeTo5CommandConfiguration"):
        require(section in program, f"Binding for {section} changed.")
    generator = ["dotnet", str(ROOT / "src/GenerateDocuments/bin/Release/net8.0/AtspmDocsGenerator.dll")]
    checked = run(*generator, "check", "--source-root", str(source), "--map", str(MAP))
    require("3 configuration sections; 0 unmapped" in checked, "Migration configuration coverage changed.")
    timestamp = run(*git, "show", "-s", "--format=%cI", "HEAD")

    with tempfile.TemporaryDirectory(prefix="data-migration-integration-") as temporary:
        workspace = Path(temporary)
        output = workspace / "docs"

        def generate():
            run(*generator, "--workspace-root", str(workspace), "--source-root", str(source),
                "--output-root", str(output), "--map", str(MAP),
                "--repository-url", REPOSITORY, "--repository-ref", REVISION, "--generated-at", timestamp)

        generate()
        expected = {"transfer-configuration.md", "transfer-events-speed.md", "upgrade-to-5.md"}
        require(all((output / name).is_file() for name in expected), "Missing migration page.")
        combined = "\n".join((output / name).read_text(encoding="utf-8") for name in expected)
        require("EndIsDateOnly" not in combined, "Derived end-date flag must not appear as a setting.")
        require("Compressed-window insert batch size" in combined, "CLI-backed descriptions are missing.")
        require("REPLACE_WITH_SECRET" in combined, "Source example must retain a safe placeholder.")
        logs = (output / "log-messages.md").read_text(encoding="utf-8")
        require("**102** log messages" in logs, "Ordinary log call coverage changed.")
        require(not re.search(r"^\| `?\d+`? \|", logs, re.MULTILINE),
                "Ordinary calls must not receive invented event IDs.")
        env = dict(os.environ, DOCS_DIR=str(output), DOCS_CONFIG=str(MAP), DOCS_REPOSITORY_URL=REPOSITORY)
        run(sys.executable, "-m", "mkdocs", "build", "--strict", "--config-file",
            str(ROOT / "mkdocs.yml"), "--site-dir", str(workspace / "site"), env=env)
        for slug in ("transfer-configuration", "transfer-events-speed", "upgrade-to-5"):
            for extension in ("example.json", "env.example"):
                require((workspace / "site" / f"{slug}.{extension}").is_file(),
                        f"Published site is missing {slug}.{extension} download.")
        before = files(output)
        generate()
        require(before == files(output), "Same-revision generation must be byte-identical.")

    print("Data Migration integration passed: sections, descriptions, ordinary logs, strict site and repeatability.")


if __name__ == "__main__":
    main()
