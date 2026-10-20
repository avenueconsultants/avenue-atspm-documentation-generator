"""Run after a Release build: python -m unittest discover -s scripts -p 'test_*.py'."""
from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from site_configuration import ROOT, configure_site, load_map


class SiteConfigurationTests(unittest.TestCase):
    def test_init_check_generate_and_build_automatic_site(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "Options.cs").write_text('''
                [ConfigurationSection("Utility")]
                public class Options { public int Port { get; set; } = 123; }
            ''', encoding="utf-8")
            generator = ["dotnet", str(ROOT / "src/GenerateDocuments/bin/Release/net8.0/AtspmDocsGenerator.dll")]
            self.run_command(generator + ["init", "--source-root", str(root), "--product-name", "New Utility"], os.environ)
            map_path = root / "documentation.json"
            initial_map = map_path.read_bytes()
            self.run_command(generator + ["check", "--source-root", str(root)], os.environ)
            output = root / "docs"
            self.run_command(generator + ["--workspace-root", str(root), "--source-root", str(root),
                "--output-root", str(output), "--map", str(map_path),
                "--repository-url", "https://github.com/example/utility", "--repository-ref", "abc",
                "--generated-at", "2026-10-01T00:00:00Z"], os.environ)
            env = dict(os.environ, DOCS_CONFIG=str(map_path), DOCS_DIR=str(output))
            site = root / "site"
            self.run_command([sys.executable, "-m", "mkdocs", "build", "--strict",
                "--config-file", str(ROOT / "mkdocs.yml"), "--site-dir", str(site)], env)
            self.assertIn("Utility__Port", (site / "configuration/index.html").read_text(encoding="utf-8"))
            self.assertEqual(initial_map, map_path.read_bytes())

    def test_disabled_logging_has_no_reference_navigation(self):
        config = configure_site({}, {
            "productName": "Utility", "containers": [{"name": "Utility", "slug": "utility"}],
            "logMessages": {"enabled": False, "sourcePath": "does-not-exist"},
        })
        self.assertEqual([{"Home": "index.md"}, {"Containers": [{"Utility": "utility.md"}]},
                          {"References": [{"Configuration changes": "support/configuration-changes.md"},
                                          {"Configuration templates": "support/configuration-templates.md"}]}], config["nav"])
        self.assertEqual("Utility Documentation", config["site_name"])
        self.assertEqual("", config["copyright"])

    def test_legacy_map_keeps_navigation_and_exports(self):
        config = configure_site({}, load_map(ROOT / "config/container-config-map.json"))
        self.assertEqual("ATSPM Documentation", config["site_name"])
        self.assertEqual(6, len(config["nav"][1]["Containers"]))
        self.assertEqual("atspm-log-messages", config["extra"]["documentation"]["log_filename"])
        self.assertEqual("Copyright Utah Department of Transportation", config["copyright"])

    def test_comments_and_case_match_loader(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "documentation.json"
            path.write_text('''{
                // consumer map
                "PRODUCTNAME": "Example // utility",
                "Containers": [{"NAME":"Example", "Slug":"example"}],
                "Site": {"SiteUrl":"https://example.com/docs/"} /* comment */
            }''', encoding="utf-8")
            config = configure_site({}, load_map(path))
            self.assertEqual("Example // utility Documentation", config["site_name"])
            self.assertEqual("https://example.com/docs/", config["site_url"])
            self.assertEqual(3, len(config["nav"]))

    def test_custom_sites_build_strictly_with_and_without_logs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "Config").mkdir()
            (root / "Logs").mkdir()
            (root / "Config/Options.cs").write_text('''
                [ConfigurationSection("Utility")]
                public class Options { public int Port { get; set; } = 123; }
            ''', encoding="utf-8")
            (root / "Logs/Events.cs").write_text('''
                public partial class Events {
                  [LoggerMessage(EventId = 1, EventName = "Started", Level = LogLevel.Information)]
                  public partial void Started();
                }
            ''', encoding="utf-8")
            documentation_map = {
                "schemaVersion": 2, "productName": "Example Utility",
                "sourcePaths": ["Config"],
                "containers": [{"name": "Utility", "slug": "utility", "sections": ["Utility"]}],
                "logMessages": {"sourcePaths": ["Logs"], "description": "Utility events."},
                "site": {"siteName": "Operations Handbook", "exportFilePrefix": "example",
                         "logReferenceTitle": 'Utility "Events" </script>',
                         "siteUrl": "https://example.com/utility/"},
            }
            map_path = root / "documentation.json"
            output = root / "docs"
            env = dict(os.environ, DOCS_CONFIG=str(map_path), DOCS_DIR=str(output))
            for logging in [True, False]:
                with self.subTest(logging=logging):
                    if not logging:
                        documentation_map.pop("logMessages")
                    map_path.write_text(json.dumps(documentation_map), encoding="utf-8")
                    self.run_command([
                        "dotnet", str(ROOT / "src/GenerateDocuments/bin/Release/net8.0/AtspmDocsGenerator.dll"),
                        "--workspace-root", str(root), "--source-root", str(root),
                        "--output-root", str(output), "--map", str(map_path),
                        "--repository-url", "https://github.com/example/utility",
                        "--repository-ref", "abc123", "--generated-at", "2026-09-29T00:00:00Z",
                    ], env)
                    site = root / ("site-logs" if logging else "site-no-logs")
                    self.run_command([sys.executable, "-m", "mkdocs", "build", "--strict",
                                      "--config-file", str(ROOT / "mkdocs.yml"),
                                      "--site-dir", str(site)], env)
                    index = (site / "index.html").read_text(encoding="utf-8")
                    self.assertIn("Operations Handbook", index)
                    self.assertNotIn("Config API", index)
                    self.assertNotIn("ATSPM", index)
                    self.assertTrue((site / "utility/index.html").exists())
                    self.assertTrue((site / "support/configuration-changes/index.html").exists())
                    self.assertTrue((site / "support/configuration-snapshot.json").exists())
                    self.assertIn('href="support/configuration-changes/"', index)
                    self.assertEqual(logging, (site / "support/log-messages/index.html").exists())
                    self.assertEqual(logging, 'href="support/log-messages/"' in index)
                    if logging:
                        html = (site / "support/log-messages/index.html").read_text(encoding="utf-8")
                        self.assertIn('const logFilename = "example-log-messages";', html)
                        self.assertIn(r'\u003c/script\u003e', html)
                        self.assertNotIn('Utility "Events" </script>', html)
                        self.assertTrue((site / "assets/vendor/xlsx.full.min.js").is_file())

    def test_changed_configuration_report_builds_and_survives_rerun(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "Options.cs"
            source.write_text('[ConfigurationSection("Utility")] public class Options { public int Port { get; set; } = 123; }', encoding="utf-8")
            generator = ["dotnet", str(ROOT / "src/GenerateDocuments/bin/Release/net8.0/AtspmDocsGenerator.dll")]
            self.run_command(generator + ["init", "--source-root", str(root)], os.environ)
            config = root / "documentation.json"
            output = root / "docs"
            command = generator + ["--workspace-root", str(root), "--source-root", str(root),
                "--output-root", str(output), "--map", str(config),
                "--repository-url", "https://github.com/example/utility",
                "--generated-at", "2026-10-01T00:00:00Z", "--repository-ref"]
            self.run_command(command + ["old"], os.environ)
            source.write_text('[ConfigurationSection("Utility")] public class Options { public int Port { get; set; } = 456; public required string Host { get; set; } }', encoding="utf-8")
            self.run_command(command + ["new"], os.environ)
            original = (output / "support/configuration-changes.md").read_bytes()
            self.run_command(command + ["new"], os.environ)
            self.assertEqual(original, (output / "support/configuration-changes.md").read_bytes())
            env = dict(os.environ, DOCS_CONFIG=str(config), DOCS_DIR=str(output))
            site = root / "site"
            self.run_command([sys.executable, "-m", "mkdocs", "build", "--strict",
                "--config-file", str(ROOT / "mkdocs.yml"), "--site-dir", str(site)], env)
            html = (site / "support/configuration-changes/index.html").read_text(encoding="utf-8")
            for expected in ["Default changed", "Required setting added", "Utility__Host", "Not set", "123", "456",
                             "https://github.com/example/utility/commit/old", "configuration-snapshot.json"]:
                self.assertIn(expected, html)

    def run_command(self, command, env):
        result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
