"""Validate maintained documentation editions and repository-local Markdown links."""

from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlsplit


LINK = re.compile(r"\[[^\]]*\]\(([^\s)]+)(?:\s+[^)]*)?\)")
FENCED = re.compile(r"^\s*(```|~~~).*?^\s*\1\s*$", re.MULTILINE | re.DOTALL)


def check_documentation(root):
    root = root.resolve()
    editions = {language: root / "docs" / language for language in ("en", "zh-CN")}
    files = {language: {path.relative_to(folder) for path in folder.rglob("*.md")} for language, folder in editions.items()}
    errors = []
    for language, names in files.items():
        other = "zh-CN" if language == "en" else "en"
        for missing in sorted(names - files[other]):
            errors.append(f"Missing {other} edition: {missing.as_posix()}")
        for name in sorted(names):
            path = editions[language] / name
            raw = path.read_bytes()
            if raw.startswith(b"\xef\xbb\xbf") or b"\r" in raw or not raw.endswith(b"\n"):
                errors.append(f"Expected UTF-8 without BOM, LF and final newline: {path.relative_to(root)}")
            try:
                text = raw.decode("utf-8")
            except UnicodeDecodeError:
                errors.append(f"Invalid UTF-8: {path.relative_to(root)}")
                continue
            counterpart = editions[other] / name
            linked = False
            prose = FENCED.sub("", text)
            for match in LINK.finditer(prose):
                href = match.group(1).strip("<>")
                parsed = urlsplit(href)
                if parsed.scheme or parsed.netloc or href.startswith(("#", "~/")):
                    continue
                relative = unquote(parsed.path)
                if not relative:
                    continue
                target = (path.parent / relative).resolve()
                if target == counterpart:
                    linked = True
                if not target.is_relative_to(root):
                    errors.append(f"Link leaves repository: {path.relative_to(root)} -> {href}")
                elif not target.exists():
                    errors.append(f"Broken local link: {path.relative_to(root)} -> {href}")
            if not linked:
                errors.append(f"Missing reciprocal edition link: {path.relative_to(root)}")
    if errors:
        raise ValueError("\n".join(errors))
    print(f"Verified {len(files['en'])} bilingual documentation pairs and their local links.")


def main():
    try:
        check_documentation(Path(__file__).resolve().parents[1])
    except ValueError as error:
        print(error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
