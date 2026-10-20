"""MkDocs hook: derive navigation and branding from the generator's map."""
from __future__ import annotations

import json
import os
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def get_value(mapping: dict, key: str, default=None):
    # The C# loader accepts property names case-insensitively.
    return next((value for name, value in mapping.items() if name.lower() == key.lower()), default)


def load_map(path: Path) -> dict:
    # Match the C# loader's JSON-comment support without touching quoted URLs/text.
    text = re.sub(r'("(?:\\.|[^"\\])*")|//[^\r\n]*|/\*[\s\S]*?\*/',
                  lambda match: match[0] if match[1] else " ",
                  path.read_text(encoding="utf-8-sig"))
    try:
        return json.loads(text)
    except ValueError as error:
        raise ValueError(f"Invalid documentation configuration '{path}': {error}") from error


def configure_site(config, documentation_map: dict):
    product = get_value(documentation_map, "productName")
    site = get_value(documentation_map, "site", {})
    logs = get_value(documentation_map, "logMessages")
    logs_enabled = logs is not None and get_value(logs, "enabled", True)
    pages = get_value(documentation_map, "containers")
    config["site_name"] = get_value(site, "siteName") or f"{product} Documentation"
    config["site_description"] = get_value(site, "siteDescription") or f"Generated configuration reference for {product}"
    config["site_url"] = get_value(site, "siteUrl") or ""
    config["copyright"] = get_value(site, "copyright") or ""
    config["nav"] = [
        {"Home": "index.md"},
        {"Containers": [{get_value(page, "name"): f'{get_value(page, "slug")}.md'} for page in pages]},
    ]
    references = [{"Log messages": "log-messages.md"}] if logs_enabled else []
    references.append({"Configuration changes": "configuration-changes.md"})
    references.append({"Configuration templates": "configuration-templates.md"})
    config["nav"].append({"References": references})
    prefix = get_value(site, "exportFilePrefix") or re.sub(r"[^a-z0-9]+", "-", product.lower()).strip("-") or "documentation"
    config.setdefault("extra", {})["documentation"] = {
        "log_title": get_value(site, "logReferenceTitle") or f"{product} Log Message Reference",
        "log_filename": f"{prefix}-log-messages",
    }
    return config


def on_config(config):
    path = Path(os.environ.get("DOCS_CONFIG") or ROOT / "config/container-config-map.json")
    return configure_site(config, load_map(path))
