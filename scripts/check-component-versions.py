"""Check the single source of runtime component versions without building CAD code."""

from __future__ import annotations

import argparse
from pathlib import Path
import re


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    root = args.root.resolve()
    definitions = (root / "src/NanoCAD.Contracts/ComponentVersions.cs").read_text(encoding="utf-8-sig")
    versions: dict[str, str] = {}
    errors: list[str] = []
    for name, version_pattern in (("Server", r"\d+\.\d+\.\d+"), ("Bridge", r"\d+\.\d+\.\d+"), ("Protocol", r"\d+\.\d+")):
        matches = re.findall(rf'public const string {name}\s*=\s*"({version_pattern})"\s*;', definitions)
        if len(matches) != 1:
            errors.append(f"ComponentVersions.{name} must contain exactly one numeric version definition.")
        else:
            versions[name] = matches[0]
    aliases = (
        ("src/NanoCAD.Contracts/Protocol.cs", "Version", "ComponentVersions.Protocol"),
        ("src/NanoCAD.Contracts/Protocol.cs", "ProductVersion", "ComponentVersions.Bridge"),
        ("src/NanoCAD.McpServer/ServerGuidance.cs", "Version", "NanoCAD.Contracts.ComponentVersions.Server"),
    )
    for relative, name, expected in aliases:
        source = (root / relative).read_text(encoding="utf-8-sig")
        values = re.findall(rf"public const string {name}\s*=\s*([^;]+);", source)
        if values != [expected]:
            errors.append(f"{relative}: {name} must reference {expected}.")
    for error in errors:
        print("ERROR " + error)
    if errors:
        return 1
    print("Component versions: " + ", ".join(f"{name} {version}" for name, version in versions.items()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
