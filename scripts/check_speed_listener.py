"""Verify the pinned Speed Listener source and consumer mapping.

Build the Release generator and install requirements.txt first.
Usage: python scripts/check_speed_listener.py --source-root PATH
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

REVISION = "7f94a674769af2ef715342c53ad18447a01ab3d6"
REPOSITORY = "https://github.com/opensourcetransportation/atspm-speed-listener"
APP = "SpeedListener/SpeedListener"
MAP = ROOT / "integration/speed-listener/documentation.json"


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    source = parser.parse_args().source_root.resolve()
    git = ["git", "-c", f"safe.directory={source.as_posix()}", "-C", str(source)]
    require(run(*git, "rev-parse", "HEAD") == REVISION, "Use the pinned Speed Listener commit.")
    require(not run(*git, "status", "--porcelain", "--", APP), "Application sources must be unchanged.")
    timestamp = run(*git, "show", "-s", "--format=%cI", "HEAD")
    generator = ["dotnet", str(ROOT / "src/GenerateDocuments/bin/Release/net8.0/AtspmDocsGenerator.dll")]
    project = (source / APP / "SpeedListener.csproj").read_text(encoding="utf-8")
    bootstrap = (source / APP / "HostBootstrapper.cs").read_text(encoding="utf-8")
    require('Include="Utah.Udot.Atspm.Infrastructure" Version="5.3.1"' in project,
            "External configuration dependency changed; review provenance.")
    require("AddAtspmDbContext(hostContext)" in bootstrap and "AddAtspmEFConfigRepositories()" in bootstrap,
            "External service registrations changed; review dependency coverage.")
    require('GetSection(nameof(SpeedEmitterConfiguration))' in bootstrap,
            "Emitter section binding changed.")
    listener = (source / APP / "Configuration/SpeedListenerConfiguration.cs").read_text(encoding="utf-8")
    require('[ConfigurationSection(nameof(EventListenerConfiguration), null)]' in listener,
            "Listener's actual section name changed.")
    require("throw new NotImplementedException()" in
            (source / APP / "BackgroundServices/SpeedListenerBackgroundService.cs").read_text(encoding="utf-8"),
            "Listener implementation changed; update its runtime-status documentation.")

    with tempfile.TemporaryDirectory(prefix="speed-listener-integration-") as temporary:
        workspace = Path(temporary)
        scratch = workspace / "source"
        for file in (source / APP).rglob("*.cs"):
            if "bin" in file.parts or "obj" in file.parts:
                continue
            target = scratch / file.relative_to(source)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(file, target)
        output = workspace / "docs"

        def check(expect_success=True):
            result = run(*generator, "check", "--source-root", str(scratch), "--map", str(MAP))
            require(expect_success, "Unexpected successful coverage check.")
            return result

        def generate(ref=REVISION, date=timestamp):
            run(*generator, "--workspace-root", str(workspace), "--source-root", str(scratch),
                "--output-root", str(output), "--map", str(MAP),
                "--repository-url", REPOSITORY, "--repository-ref", ref, "--generated-at", date)

        def build():
            env = dict(os.environ, DOCS_DIR=str(output), DOCS_CONFIG=str(MAP), DOCS_REPOSITORY_URL=REPOSITORY)
            run(sys.executable, "-m", "mkdocs", "build", "--strict", "--config-file",
                str(ROOT / "mkdocs.yml"), "--site-dir", str(workspace / "site"), env=env)

        check()
        generate()
        snapshot = json.loads((output / "support/configuration-snapshot.json").read_text(encoding="utf-8"))
        sections = {item["name"]: item for item in snapshot["sections"]}
        require(set(sections) == {"EventListenerConfiguration", "SpeedEmitterConfiguration"},
                "Expected both actual section bindings.")
        require(sections["EventListenerConfiguration"]["services"][0]["slug"] == "event-listener",
                "Listener mapped to the wrong page.")
        require(sections["SpeedEmitterConfiguration"]["services"][0]["slug"] == "speed-emitter",
                "Emitter mapped to the wrong page.")
        emitter = {item["name"]: item for item in sections["SpeedEmitterConfiguration"]["properties"]}
        listener_props = {item["name"]: item for item in sections["EventListenerConfiguration"]["properties"]}
        require(set(emitter) == {"ListenerHost", "ListenerPort", "ProtocolType", "IntervalMilliseconds"},
                "Expected all four emitter settings.")
        require(set(listener_props) == {"ApiBaseUrl", "ApiEndPoint", "BatchSize", "UdpPort", "threads"},
                "Expected all five listener declarations.")
        require([emitter[name]["default"] for name in ("ListenerHost", "ListenerPort", "ProtocolType", "IntervalMilliseconds")]
                == ['"127.0.0.1"', "1088", "ProtocolType.Udp", "100"], "Emitter defaults changed.")
        require([listener_props[name]["default"] for name in ("BatchSize", "UdpPort", "threads")]
                == ["50_000", "10088", "50"], "Listener numeric defaults changed.")
        page = (output / "event-listener.md").read_text(encoding="utf-8")
        require('"BatchSize": 50000' in page, "Numeric digit separators must produce the correct JSON example.")
        require(f"/blob/{REVISION}/{APP}/Configuration/SpeedListenerConfiguration.cs#L25" in page,
                "Listener source link or named binding changed.")
        ids = [int(value) for value in re.findall(r"^\| `(\d+)`",
            (output / "support/log-messages.md").read_text(encoding="utf-8"), re.MULTILINE)]
        require(ids == list(range(2001, 2008)), "Expected seven unique emitter events.")
        require(snapshot["report"]["changes"] == [], "First run must establish a baseline.")
        build()
        require("ATSPM Speed Listener Documentation" in (workspace / "site/index.html").read_text(encoding="utf-8"),
                "Consumer branding missing.")

        before = files(output)
        generate()
        require(before == files(output), "Same-revision generation must be byte-identical.")
        generate("b" * 40, "2026-10-05T00:00:00Z")
        require(not meaningful_change(before, files(output), REPOSITORY),
                "Revision-only changes must not create a PR.")

        # A new section in the configured root must not be guessed into either role.
        new_section = scratch / APP / "Configuration/NewOptions.cs"
        new_section.write_text('[ConfigurationSection("NewOptions")]\n'
                               'public class NewOptions { public int Port { get; set; } = 777; }\n', encoding="utf-8")
        try:
            check(expect_success=False)
        except RuntimeError as error:
            require("Unmapped section: NewOptions" in str(error), "Check failed for an unexpected reason.")
        else:
            raise AssertionError("New sections require an explicit listener/emitter assignment.")

    print("Speed Listener integration passed: bindings, defaults, events, strict site, repeatability and coverage guard.")


if __name__ == "__main__":
    main()
