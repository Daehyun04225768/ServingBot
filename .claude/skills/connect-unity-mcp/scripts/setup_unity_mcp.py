#!/usr/bin/env python3
"""
Wires a Unity project up to CoplayDev's "MCP for Unity" bridge:
  1. Adds the com.coplaydev.unity-mcp package to Packages/manifest.json
     (so Unity installs the in-editor MCP bridge and starts its local
     HTTP server the next time the project is open).
  2. Registers an "unity" HTTP MCP server entry in a project-root
     .mcp.json so Claude Code knows where to connect.

Both steps are additive and idempotent: existing manifest dependencies
and existing mcpServers entries are preserved untouched, and re-running
this script on an already-configured project is a safe no-op that just
reports the current state.

Usage:
    python setup_unity_mcp.py [project_path] [--port 8080]

project_path defaults to the current directory. Exit code is 0 whether
or not changes were needed -- the printed report distinguishes
"already configured" from "just configured" from "failed", so read the
report rather than relying on exit codes alone.
"""

import argparse
import json
import sys
from pathlib import Path

UNITY_MCP_PACKAGE_ID = "com.coplaydev.unity-mcp"
UNITY_MCP_PACKAGE_URL = "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main"


def find_unity_project_root(start: Path) -> Path | None:
    """Walk upward from `start` looking for a directory that has both
    Assets/ and Packages/manifest.json -- the two things every Unity
    project has regardless of render pipeline or Unity version."""
    current = start.resolve()
    for candidate in [current, *current.parents]:
        if (candidate / "Assets").is_dir() and (candidate / "Packages" / "manifest.json").is_file():
            return candidate
    return None


def ensure_manifest_dependency(manifest_path: Path) -> str:
    """Returns 'added', 'already-present', or raises on malformed JSON."""
    text = manifest_path.read_text(encoding="utf-8")
    data = json.loads(text)  # raise clearly if the project's manifest is broken JSON
    deps = data.setdefault("dependencies", {})

    if deps.get(UNITY_MCP_PACKAGE_ID):
        return "already-present"

    deps[UNITY_MCP_PACKAGE_ID] = UNITY_MCP_PACKAGE_URL
    # Unity writes manifest.json with 2-space indent and a trailing newline;
    # match that so the diff stays clean and Unity doesn't reformat the
    # whole file on next save.
    manifest_path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    return "added"


def ensure_mcp_json(mcp_json_path: Path, port: int) -> str:
    """Returns 'added', 'already-present', 'updated-port', or raises on malformed JSON."""
    url = f"http://127.0.0.1:{port}/mcp"

    if mcp_json_path.is_file():
        text = mcp_json_path.read_text(encoding="utf-8")
        data = json.loads(text) if text.strip() else {}
    else:
        data = {}

    servers = data.setdefault("mcpServers", {})
    existing = servers.get("unity")

    if existing == {"type": "http", "url": url}:
        return "already-present"

    status = "updated-port" if existing else "added"
    servers["unity"] = {"type": "http", "url": url}
    mcp_json_path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    return status


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("project_path", nargs="?", default=".", help="Path inside (or at) the Unity project root")
    parser.add_argument("--port", type=int, default=8080, help="Port MCP for Unity's embedded HTTP server listens on (default: 8080)")
    args = parser.parse_args()

    start = Path(args.project_path)
    root = find_unity_project_root(start)

    if root is None:
        print(f"NOT_A_UNITY_PROJECT: no Assets/ + Packages/manifest.json found at or above '{start.resolve()}'.")
        print("Pass the correct Unity project root as an argument, or run this from inside one.")
        return 2

    manifest_path = root / "Packages" / "manifest.json"
    mcp_json_path = root / ".mcp.json"

    try:
        manifest_result = ensure_manifest_dependency(manifest_path)
    except json.JSONDecodeError as e:
        print(f"FAILED: {manifest_path} is not valid JSON ({e}). Fix it by hand and re-run.")
        return 1

    try:
        mcp_json_result = ensure_mcp_json(mcp_json_path, args.port)
    except json.JSONDecodeError as e:
        print(f"FAILED: {mcp_json_path} is not valid JSON ({e}). Fix it by hand and re-run.")
        return 1

    print(f"PROJECT_ROOT: {root}")
    print(f"MANIFEST: {manifest_result} ({manifest_path})")
    print(f"MCP_JSON: {mcp_json_result} ({mcp_json_path})")

    if manifest_result == "already-present" and mcp_json_result == "already-present":
        print("STATUS: already fully configured.")
    else:
        print("STATUS: configured.")

    print(
        "NEXT_STEP: open (or re-focus) the Unity Editor on this project and wait for it to finish "
        "resolving packages / compiling. MCP for Unity starts its embedded HTTP bridge server "
        f"(default port {args.port}) automatically once the package is installed -- this cannot "
        "happen headlessly, Unity itself has to be running."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
