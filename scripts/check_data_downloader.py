"""Verify the pinned Data Downloader integration without modifying its checkout.

Run after a Release generator build, with MkDocs installed:
python scripts/check_data_downloader.py --source-root PATH
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import shutil
import sys
import tempfile

from prepare_documentation_pr import ROOT, files, meaningful_change, run

REVISION = "51d563383e15f7b90ab2b3abb91d1b2c39aafeac"
REPOSITORY = "https://github.com/opensourcetransportation/atspm-data-downloader"
APP = "atspm-data-downloader"
MAP = ROOT / "integration/data-downloader/documentation.json"


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    args = parser.parse_args()
    source = args.source_root.resolve()
    git = ["git", "-c", f"safe.directory={source.as_posix()}", "-C", str(source)]
    require(run(*git, "rev-parse", "HEAD") == REVISION, "Use the pinned Data Downloader commit.")
    require(not run(*git, "status", "--porcelain", "--", APP), "Application sources must be unchanged.")
    timestamp = run(*git, "show", "-s", "--format=%cI", "HEAD")
    generator = ["dotnet", str(ROOT / "src/GenerateDocuments/bin/Release/net8.0/AtspmDocsGenerator.dll")]
    map_bytes = MAP.read_bytes()

    with tempfile.TemporaryDirectory(prefix="downloader-integration-") as temporary:
        workspace = Path(temporary)
        scratch = workspace / "source"
        for path in (source / APP).rglob("*.cs"):
            if "bin" in path.parts or "obj" in path.parts:
                continue
            destination = scratch / path.relative_to(source)
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, destination)
        output = workspace / "docs"

        def generate(ref=REVISION, date=timestamp):
            run(*generator, "check", "--source-root", str(scratch), "--map", str(MAP))
            run(*generator, "--workspace-root", str(workspace), "--source-root", str(scratch),
                "--output-root", str(output), "--map", str(MAP),
                "--repository-url", REPOSITORY, "--repository-ref", ref, "--generated-at", date)

        def build():
            env = dict(os.environ, DOCS_DIR=str(output), DOCS_CONFIG=str(MAP), DOCS_REPOSITORY_URL=REPOSITORY)
            run(sys.executable, "-m", "mkdocs", "build", "--strict", "--config-file",
                str(ROOT / "mkdocs.yml"), "--site-dir", str(workspace / "site"), env=env)

        generate()
        snapshot = json.loads((output / "support/configuration-snapshot.json").read_text())
        require(len(snapshot["sections"]) == 1, "Expected one configuration section.")
        section = snapshot["sections"][0]
        require(section["name"] == "DownloaderConfiguration", "Incorrect section binding.")
        properties = {item["name"]: item for item in section["properties"]}
        require(set(properties) == {"Start", "End", "LocationIdentifiers", "DataType", "ApiKey", "ApiUrl", "Format", "OutputPath"},
                "Expected all eight downloader settings.")
        require({name for name, item in properties.items() if item["required"]} == {"Start", "End", "LocationIdentifiers"},
                "Annotation requirements differ from the pinned source.")
        require(properties["Format"]["default"] == "DownloadFormat.NdJson", "Incorrect format default.")
        require(set(properties["Format"]["options"]) == {"Csv", "Json", "NdJson"}, "Missing enum choices.")
        require(properties["LocationIdentifiers"]["environmentVariables"] == ["DownloaderConfiguration__LocationIdentifiers__0"],
                "List environment binding must be indexed.")
        require(snapshot["report"]["changes"] == [], "First run must establish a baseline.")
        logs = (output / "support/log-messages.md").read_text(encoding="utf-8")
        ids = [int(value) for value in re.findall(r"^\| `(\d+)`", logs, re.MULTILINE)]
        require(ids == list(range(1000, 1008)) + list(range(2000, 2008)), "Expected all 16 unique events.")
        page = (output / "data-downloader.md").read_text(encoding="utf-8")
        require(f"/blob/{REVISION}/{APP}/Configuration/DownloaderConfiguration.cs#L26" in page, "Incorrect source provenance.")
        require('"ApiKey": "YOUR_API_KEY_HERE"' in page, "Use placeholder credentials in examples.")
        build()
        html = (workspace / "site/index.html").read_text(encoding="utf-8")
        require("ATSPM Data Downloader Documentation" in html, "Consumer branding missing.")

        before = files(output)
        generate()
        require(before == files(output), "Same-revision generation must be byte-identical.")
        generate("b" * 40, "2026-10-02T00:00:00Z")
        require(not meaningful_change(before, files(output), REPOSITORY), "Revision-only changes must not create a PR.")

        configuration = scratch / APP / "Configuration/DownloaderConfiguration.cs"
        content = configuration.read_text(encoding="utf-8").replace(
            "public string? OutputPath { get; set; }",
            "public string? OutputPath { get; set; }\n"
            "    /// <summary>Storage region required by the next version.</summary>\n"
            "    [Required]\n    public string? StorageRegion { get; set; }")
        configuration.write_text(content, encoding="utf-8")
        new_folder = scratch / APP / "NewConfiguration"
        new_folder.mkdir()
        (new_folder / "RetryConfiguration.cs").write_text(
            'namespace atspm_data_downloader.Configuration;\n'
            '/// <summary>Retry settings.</summary>\n'
            '[ConfigurationSection("RetryConfiguration")]\n'
            'public class RetryConfiguration {\n'
            '/// <summary>Maximum retries.</summary>\n'
            'public int Attempts { get; set; } = 3; }\n', encoding="utf-8")
        log = scratch / APP / "Logging/EventLogDownloaderLogMessages.cs"
        content = log.read_text(encoding="utf-8")
        position = content.rfind("}")
        content = content[:position] + (
            '    /// <summary>Retry scheduled.</summary>\n'
            '    [LoggerMessage(EventId = 2008, EventName = "Retry Scheduled", Level = LogLevel.Information, Message = "Retry scheduled") ]\n'
            '    public partial void RetryScheduled();\n') + content[position:]
        log.write_text(content, encoding="utf-8")
        generate("c" * 40, "2026-10-03T00:00:00Z")
        changed = json.loads((output / "support/configuration-snapshot.json").read_text())
        changes = changed["report"]["changes"]
        require(any(item["kind"] == "Required setting added" and item["property"] == "StorageRegion" for item in changes),
                "A new required setting must appear in the change report.")
        require(any(item["kind"] == "Section added" and item["section"] == "RetryConfiguration" for item in changes),
                "A new source folder/section must be discovered automatically.")
        require("`2008`" in (output / "support/log-messages.md").read_text(), "New log event missing.")
        require(MAP.read_bytes() == map_bytes, "Automatic discovery must not need map edits.")
        build()
    print("Data Downloader integration passed: coverage, branding, strict sites, repeatability and automatic discovery.")


if __name__ == "__main__":
    main()
