from __future__ import annotations

import hashlib
import json
from pathlib import Path

import yaml


ROOT = Path(__file__).resolve().parents[1]


class MkDocsLoader(yaml.SafeLoader):
    pass


def construct_env(loader: MkDocsLoader, node: yaml.Node) -> object:
    if isinstance(node, yaml.SequenceNode):
        values = loader.construct_sequence(node)
        return values[-1] if values else None
    return loader.construct_scalar(node)


MkDocsLoader.add_constructor("!ENV", construct_env)


def validate_navigation() -> None:
    documentation_map = json.loads(
        (ROOT / "config" / "container-config-map.json").read_text(encoding="utf-8")
    )
    mkdocs = yaml.load(
        (ROOT / "mkdocs.yml").read_text(encoding="utf-8"),
        Loader=MkDocsLoader,
    )
    container_group = next(
        item["Containers"] for item in mkdocs["nav"] if "Containers" in item
    )
    actual = [(next(iter(item)), next(iter(item.values()))) for item in container_group]
    expected = [
        (container["name"], f'{container["slug"]}.md')
        for container in documentation_map["containers"]
    ]
    if actual != expected:
        raise SystemExit(
            f"mkdocs container navigation does not match the documentation map.\n"
            f"Expected: {expected}\nActual: {actual}"
        )


def validate_vendor_assets() -> None:
    checksums_path = ROOT / "overrides" / "assets" / "vendor" / "checksums.json"
    if not checksums_path.exists():
        return
    checksums = json.loads(checksums_path.read_text(encoding="utf-8"))
    for relative_path, expected_digest in checksums.items():
        path = checksums_path.parent / relative_path
        if not path.is_file():
            raise SystemExit(f"Missing vendored browser asset: {path}")
        actual_digest = hashlib.sha256(path.read_bytes()).hexdigest()
        if actual_digest != expected_digest:
            raise SystemExit(f"Checksum mismatch for vendored browser asset: {path}")


if __name__ == "__main__":
    validate_navigation()
    validate_vendor_assets()
