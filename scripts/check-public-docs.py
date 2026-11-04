"""Check English-only public documentation and source without CAD or network access.

Requires Python 3.11+. Run from any directory; --root selects a staged checkout.
Source comments and text fixtures are included. Private developer archives,
local artifacts and agent instructions are outside the publication scope.
Link checks cover files included in this checkout, not remote URLs.
"""

from __future__ import annotations

import argparse
import html
import json
import re
import sys
import tomllib
import unicodedata
from pathlib import Path
from urllib.parse import unquote, urlsplit


FENCE = re.compile(r"^ {0,3}(`{3,}|~{3,})(.*)$")
HEADING = re.compile(r"^ {0,3}(#{1,6})\s+(.+?)\s*#*\s*$")
LIST = re.compile(r"^(\s*)(?:[-+*]|\d+[.)])\s+")
LINK = re.compile(r"!?\[[^\]\n]*\]\(\s*(?:<([^>\n]+)>|([^\s)]+))(?:\s+[\"'][^\n]*?[\"'])?\s*\)")
REFERENCE = re.compile(r"^ {0,3}\[([^\]]+)\]:\s*(?:<([^>]+)>|(\S+))")
REFERENCE_LINK = re.compile(r"!?\[([^\]\n]+)\]\[([^\]\n]*)\]")
TABLE_RULE = re.compile(r"^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)+\|?\s*$")
INLINE_CODE = re.compile(r"(`+).*?\1")
EXPLICIT_ID = re.compile(r"<(?:a|span)\b[^>]*\b(?:id|name)=[\"']([^\"']+)[\"']", re.I)
PLACEHOLDER = re.compile(r"\b(?:TODO|TBD|FIXME|YOUR_USERNAME|YOUR_REPOSITORY|OWNER/REPO)\b|\[COPYRIGHT HOLDER\]|<(?:release|owner|repo|username)>|\[(?:your repository|repository URL)\]", re.I)
NON_ENGLISH_TEXT = re.compile(r"[\u0400-\u052f]")
LOCALIZATION_REFERENCE = re.compile(
    r"\bdocs[/\\]r[u](?:[/\\]|\b)|\bREADME[.]r[u][.]md\b|\bRus(?:sian)\b|\bbi(?:lingual)\b",
    re.I,
)
LOCALIZATION_PATH = re.compile(r"^(?:docs/r[u](?:/|$)|README[.]r[u][.]md$)", re.I)
PUBLIC_ROOT_TEXT = {
    "LICENSE", ".gitignore", ".gitattributes", ".editorconfig", "Directory.Build.props",
    "global.json", "NuGet.Config", "NanoCAD.Mcp.sln",
}
PUBLIC_BUILD_SCRIPTS = {
    "bootstrap.ps1", "build.ps1", "build-native-parameters412.ps1",
    "build-native-model-history421.ps1", "build-visual-worker-pilot.ps1",
    "check-public-docs.py", "check-component-versions.py", "check-format.ps1",
}
PUBLIC_TEXT_SUFFIXES = {
    ".cs", ".csproj", ".cpp", ".h", ".ps1", ".cmd", ".bat", ".py", ".md",
    ".json", ".resx", ".xml", ".props", ".targets", ".manifest", ".config",
    ".vcxproj", ".txt", ".toml", ".yml", ".yaml", ".csv", ".tsv",
}
EXPORTED_SOURCE_SUFFIXES = {
    ".cs", ".csproj", ".cpp", ".h", ".ps1", ".md", ".json", ".resx",
    ".xml", ".props", ".targets", ".manifest", ".config", ".vcxproj",
}
SKIP_PARTS = {"bin", "obj", "__pycache__", "generated", ".git"}


def slug(text: str) -> str:
    """GFM-style heading ID, preserving Unicode letters."""
    text = re.sub(r"!?\[([^\]]+)\]\([^)]*\)", r"\1", text)
    text = re.sub(r"<[^>]*>", "", html.unescape(text))
    text = text.replace("`", "").lower()
    return "".join(
        "-" if char.isspace() else char
        for char in unicodedata.normalize("NFC", text)
        if char.isspace() or char in "-_" or unicodedata.category(char)[0] in "LNM"
    )


class Checker:
    def __init__(self, root: Path, scope: str = "export") -> None:
        self.root = root.resolve()
        self.scope = scope
        self.errors: list[dict[str, object]] = []
        self.texts: dict[Path, str] = {}
        self.anchors: dict[Path, set[str]] = {}
        self.links = 0
        self.configs = 0

    def display(self, path: Path) -> str:
        try:
            return path.relative_to(self.root).as_posix()
        except ValueError:
            return str(path)

    def fail(self, path: Path, line: int, check: str, message: str) -> None:
        self.errors.append({"path": self.display(path), "line": line, "check": check, "message": message})

    def read(self, path: Path) -> str:
        if path not in self.texts:
            self.texts[path] = path.read_text(encoding="utf-8-sig")
        return self.texts[path]

    def public_files(self) -> list[Path]:
        files = {p for p in self.root.glob("*.md") if p.name != "AGENTS.md"}
        files.update((self.root / "docs/en").glob("*.md"))
        index = self.root / "docs/index.md"
        if index.is_file():
            files.add(index)
        github = self.root / ".github"
        if github.is_dir():
            files.update(p for p in github.rglob("*") if p.is_file() and p.suffix.lower() in {".md", ".yml", ".yaml"})
        return sorted(files)

    def public_source_files(self, docs: list[Path]) -> list[Path]:
        """Mirror the public-export scope while supporting source and runtime roots."""
        files = set(docs)
        files.update(self.root / name for name in PUBLIC_ROOT_TEXT if (self.root / name).is_file())
        checkout = self.scope == "checkout"
        for directory in ("src", "tests", "deployment", "licenses", ".github", "config/examples"):
            for path in (self.root / directory).rglob("*"):
                suffixes = EXPORTED_SOURCE_SUFFIXES if directory in {"src", "tests"} and not checkout else PUBLIC_TEXT_SUFFIXES
                if path.is_file() and path.name != "AGENTS.md" and path.suffix.lower() in suffixes and not any(
                    part.casefold() in SKIP_PARTS for part in path.relative_to(self.root).parts
                ):
                    files.add(path)
        scripts = self.root / "scripts"
        files.update((self.root / "config").glob("*.example"))
        if checkout:
            files.update(path for path in scripts.rglob("*") if path.is_file()
                         and path.suffix.lower() in PUBLIC_TEXT_SUFFIXES
                         and not any(part.casefold() in SKIP_PARTS for part in path.relative_to(self.root).parts))
        else:
            files.update(scripts / name for name in PUBLIC_BUILD_SCRIPTS if (scripts / name).is_file())
        return sorted(files)

    def check_english_only(self, files: list[Path]) -> None:
        # Reject an obsolete localization tree even when it has no linked pages.
        for path in self.root.glob("docs/*"):
            if LOCALIZATION_PATH.match(path.relative_to(self.root).as_posix()):
                self.fail(path, 1, "language", "Remove the obsolete localization directory from public files")
        for path in files:
            if LOCALIZATION_PATH.match(path.relative_to(self.root).as_posix()):
                self.fail(path, 1, "language", "Localized document paths are not allowed")
            try:
                for number, line in enumerate(self.read(path).splitlines(), 1):
                    if NON_ENGLISH_TEXT.search(line):
                        self.fail(path, number, "language", "Non-English script found in public text, source, comment or fixture")
                    if LOCALIZATION_REFERENCE.search(line):
                        self.fail(path, number, "language", "Obsolete localization reference found in public text")
            except (OSError, UnicodeError) as error:
                self.fail(path, 1, "read", str(error))

    def visible_lines(self, path: Path, validate: bool = False) -> list[str]:
        lines = self.read(path).splitlines()
        output = lines.copy()
        opened: tuple[str, int, str] | None = None
        body: list[str] = []
        for number, line in enumerate(lines, 1):
            match = FENCE.match(line)
            if opened:
                output[number - 1] = ""
                if match and match[1][0] == opened[0][0] and len(match[1]) >= len(opened[0]) and not match[2].strip():
                    if validate:
                        self.parse_config(path, opened[1], opened[2], "\n".join(body))
                    opened = None
                    body = []
                else:
                    body.append(line)
            elif match:
                opened = (match[1], number, match[2].strip().split()[0].lower() if match[2].strip() else "")
                output[number - 1] = ""
        if opened and validate:
            self.fail(path, opened[1], "fence", "Unclosed fenced code block")
        return output

    def parse_config(self, path: Path, line: int, language: str, body: str) -> None:
        if language not in {"json", "toml"}:
            return
        self.configs += 1
        try:
            if language == "json":
                json.loads(body)
            else:
                tomllib.loads(body)
        except (ValueError, tomllib.TOMLDecodeError) as error:
            self.fail(path, line, "configuration", f"Invalid {language.upper()} example: {error}")

    def heading_ids(self, path: Path) -> set[str]:
        if path in self.anchors:
            return self.anchors[path]
        ids: set[str] = set()
        for line in self.visible_lines(path):
            match = HEADING.match(line)
            if match:
                base = slug(match[2])
                candidate = base
                suffix = 0
                while candidate in ids:
                    suffix += 1
                    candidate = f"{base}-{suffix}"
                ids.add(candidate)
            ids.update(EXPLICIT_ID.findall(line))
        self.anchors[path] = ids
        return ids

    def forbidden_target(self, target: Path) -> bool:
        relative = target.relative_to(self.root).as_posix()
        if relative.startswith(("artifacts/", ".research/", ".tools/", ".codex/", "docs/history/")):
            return True
        if relative == "AGENTS.md":
            return True
        # Current public docs live only in docs/en and docs/index.md.
        return relative.startswith("docs/") and target.suffix.lower() == ".md" and not (
            relative.startswith("docs/en/") or relative == "docs/index.md"
        )

    def check_link(self, source: Path, line: int, destination: str) -> None:
        self.links += 1
        destination = html.unescape(destination)
        parsed = urlsplit(destination)
        if parsed.scheme in {"http", "https", "mailto", "tel"}:
            return
        if parsed.scheme or destination.startswith("//") or re.match(r"^[A-Za-z]:", destination):
            self.fail(source, line, "link", "Use a repository-relative path or an ordinary web URL")
            return
        path_text = unquote(parsed.path)
        target = (self.root / path_text.lstrip("/")) if path_text.startswith("/") else source.parent / path_text
        target = target.resolve() if path_text else source.resolve()
        try:
            target.relative_to(self.root)
        except ValueError:
            self.fail(source, line, "link", f"Link leaves the repository: {destination}")
            return
        if self.forbidden_target(target):
            self.fail(source, line, "public-scope", f"Link exposes an internal/history path: {destination}")
            return
        if not target.exists():
            self.fail(source, line, "link", f"Missing local target: {destination}")
            return
        fragment = unquote(parsed.fragment)
        if fragment and target.is_file():
            if target.suffix.lower() == ".md":
                if fragment not in self.heading_ids(target):
                    self.fail(source, line, "fragment", f"Missing heading/anchor: {destination}")
            elif re.fullmatch(r"L\d+(?:-L\d+)?", fragment):
                requested = [int(value) for value in re.findall(r"\d+", fragment)]
                if not all(1 <= value <= len(self.read(target).splitlines()) for value in requested):
                    self.fail(source, line, "fragment", f"Source line anchor out of range: {destination}")

    def check_links(self, path: Path, lines: list[str]) -> None:
        references: dict[str, str] = {}
        for number, line in enumerate(lines, 1):
            definition = REFERENCE.match(line)
            if definition:
                references[definition[1].casefold()] = definition[2] or definition[3]
                self.check_link(path, number, definition[2] or definition[3])
                continue
            plain = INLINE_CODE.sub("", line)
            for match in LINK.finditer(plain):
                self.check_link(path, number, match[1] or match[2])
        for number, line in enumerate(lines, 1):
            for match in REFERENCE_LINK.finditer(INLINE_CODE.sub("", line)):
                label = (match[2] or match[1]).casefold()
                if label not in references:
                    self.fail(path, number, "link", f"Undefined reference link: {label}")

    def check_structure(self, path: Path, lines: list[str]) -> None:
        previous_level = 0
        h1 = 0
        for index, line in enumerate(lines):
            if re.match(r"^\[.*\]\(.*\).*\u00b7\s*$", line):
                self.fail(path, index + 1, "navigation", "Remove the trailing navigation separator")
            heading = HEADING.match(line)
            if heading:
                level = len(heading[1])
                h1 += level == 1
                if previous_level and level > previous_level + 1:
                    self.fail(path, index + 1, "heading", f"Heading skips a level (H{previous_level} to H{level})")
                if previous_level == 0 and level != 1 and ".github" not in path.parts:
                    self.fail(path, index + 1, "heading", "First heading must be H1")
                previous_level = level
                if index and lines[index - 1].strip():
                    self.fail(path, index + 1, "spacing", "Add a blank line before the heading")
                if index + 1 < len(lines) and lines[index + 1].strip():
                    self.fail(path, index + 1, "spacing", "Add a blank line after the heading")
            item = LIST.match(line)
            if item and not item[1] and index and lines[index - 1].strip() and not LIST.match(lines[index - 1]):
                self.fail(path, index + 1, "spacing", "Add a blank line before the list")
            if TABLE_RULE.match(line) and index:
                header = index - 1
                if header and lines[header - 1].strip():
                    self.fail(path, header + 1, "spacing", "Add a blank line before the table")
                columns = len(re.split(r"(?<!\\)\|", lines[header].strip().strip("|")))
                end = index + 1
                while end < len(lines) and "|" in lines[end] and lines[end].strip():
                    actual = len(re.split(r"(?<!\\)\|", lines[end].strip().strip("|")))
                    if actual != columns:
                        self.fail(path, end + 1, "table", f"Expected {columns} table columns, found {actual}")
                    end += 1
                if end < len(lines) and lines[end].strip():
                    self.fail(path, end + 1, "spacing", "Add a blank line after the table")
        if ".github" not in path.parts and h1 != 1:
            self.fail(path, 1, "heading", f"Expected one H1, found {h1}")

    def run(self) -> dict[str, object]:
        files = self.public_files()
        if not files:
            self.fail(self.root, 1, "scope", "No public documentation found")
        for path in files:
            try:
                text = self.read(path)
                lines = self.visible_lines(path, validate=True)
                if "\ufffd" in text:
                    self.fail(path, 1, "encoding", "Unicode replacement character found")
                for number, line in enumerate(text.splitlines(), 1):
                    if PLACEHOLDER.search(line):
                        self.fail(path, number, "placeholder", "Unresolved placeholder or TODO marker")
                self.check_links(path, lines)
                if path.suffix.lower() == ".md":
                    self.check_structure(path, lines)
            except (OSError, UnicodeError) as error:
                self.fail(path, 1, "read", str(error))
        source_files = self.public_source_files(files)
        self.check_english_only(source_files)
        if not (self.root / "README.md").is_file():
            self.fail(self.root / "README.md", 1, "scope", "Missing public entry point")
        return {"success": not self.errors, "root": str(self.root), "files_checked": len(files),
                "source_text_files_checked": len(source_files), "language_policy": "English-only",
                "links_checked": self.links, "configuration_examples_parsed": self.configs, "errors": self.errors}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1], help="Repository or staged-publication root")
    parser.add_argument("--json", type=Path, help="Write the complete JSON report to this path")
    parser.add_argument("--scope", choices=("export", "checkout"), default="export",
                        help="Export checks the public allowlist in a private workspace; checkout checks all shipped text in a public checkout")
    args = parser.parse_args()
    report = Checker(args.root, args.scope).run()
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for error in report["errors"]:
        print(f"{error['path']}:{error['line']}: {error['check']}: {error['message']}")
    label = "PASS" if report["success"] else "FAIL"
    print(f"{label}: {report['files_checked']} public documents; {report['source_text_files_checked']} source text files; {report['links_checked']} links; "
          f"{report['configuration_examples_parsed']} JSON/TOML examples; {len(report['errors'])} errors")
    return 0 if report["success"] else 1


if __name__ == "__main__":
    sys.exit(main())
