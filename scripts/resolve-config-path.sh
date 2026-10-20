#!/usr/bin/env bash
# The action runs on a Linux runner. Only an omitted input selects the legacy map.
set -euo pipefail

source_root="$1"
action_root="$2"
config_file="${3:-}"

if [[ -z "$config_file" ]]; then
  candidate="$action_root/config/container-config-map.json"
elif [[ "$config_file" = /* ]]; then
  candidate="$config_file"
else
  candidate="$source_root/$config_file"
fi

# Single-line paths are required for GitHub step outputs.
if [[ "$candidate" == *$'\n'* || "$candidate" == *$'\r'* ]]; then
  echo "config-file must resolve to a single-line path." >&2
  exit 2
fi
if [[ ! -f "$candidate" ]]; then
  echo "Configuration file does not exist or is not a file: $candidate" >&2
  echo "Set config-file relative to source-root, for example documentation.json." >&2
  exit 2
fi

realpath -e "$candidate"
