"""Paste parts/<name>.html into index.html at each `<!-- @include <name> -->` marker.

Keeps the markers (wrapped as begin/end comments) so it can be re-run after a part changes.
Usage: python tools/assemble.py
"""
import pathlib
import re

root = pathlib.Path(__file__).resolve().parent.parent
index = root / "index.html"
html = index.read_text(encoding="utf-8")

pattern = re.compile(
    r"<!-- @include (\w+) -->(?:.*?<!-- @end \1 -->)?", re.S
)

def paste(match: re.Match) -> str:
    name = match.group(1)
    part = root / "parts" / f"{name}.html"
    if not part.exists():
        print(f"missing parts/{name}.html, left marker")
        return f"<!-- @include {name} -->"
    print(f"included parts/{name}.html")
    return f"<!-- @include {name} -->\n{part.read_text(encoding='utf-8').strip()}\n<!-- @end {name} -->"

index.write_text(pattern.sub(paste, html), encoding="utf-8")
