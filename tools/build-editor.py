"""Builds the standalone chart editor from editor/index.html.

editor/index.html is the page body as the hosted (Artifact) version publishes it - the host adds
the document skeleton. The standalone copy, which the mod ships and opens from disk, needs that
skeleton itself: this script adds it.

usage: python tools/build-editor.py [output]   (default: BossBattles/Editor/BossBattlesEditor.html)
"""
import pathlib
import sys

root = pathlib.Path(__file__).resolve().parent.parent
source = (root / "editor" / "index.html").read_text(encoding="utf-8")
output = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else root / "BossBattles" / "Editor" / "BossBattlesEditor.html"

page = (
    "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n"
    "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1, viewport-fit=cover\">\n"
    "<style>html,body{margin:0}[hidden]{display:none!important}img{max-width:100%}</style>\n"
    "</head>\n<body>\n" + source + "\n</body>\n</html>\n"
)

output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(page, encoding="utf-8")
print(f"wrote {output} ({len(page) // 1024} KB)")
