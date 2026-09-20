#!/usr/bin/env python3
"""Render STATUS.md as a static board.

⭐ A VIEW OF THE FILE, NOT A SECOND STORE. The complaint that killed the README was that nobody
updated it; a tracker with its own copy of the truth has the same disease twice over. This reads
STATUS.md and emits HTML, so the board cannot drift from the repo — updating the row in the commit
that changes the code is still the only way anything moves, and now it is also the only way the
board moves.

Usage:  python3 tools/status_board.py [STATUS.md] [out.html]
"""
import html
import re
import sys
from datetime import date

STATE = {
    "✅": ("verified", "var(--grass)"),
    "🔶": ("not proved", "var(--amber)"),
    "🚧": ("in progress", "var(--sky)"),
    "⬜": ("not started", "var(--dim)"),
}

CSS = """
:root{
  --ground:#f2f3ef; --panel:#fff; --rail:#e2e4dd; --ink:#1d2320; --dim:#5f6a63;
  --orange:#d4700f; --grass:#4f8a2b; --amber:#c08a12; --sky:#2f6f97;
  --mono:'IBM Plex Mono',ui-monospace,'DejaVu Sans Mono',Menlo,monospace;
  --disp:'Bricolage Grotesque','Source Sans 3','Helvetica Neue',Helvetica,Arial,sans-serif;
  --body:'Source Sans 3','Helvetica Neue',Helvetica,Arial,sans-serif;
}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){
  --ground:#121614; --panel:#1a201d; --rail:#2a322e; --ink:#e7ebe6; --dim:#93a098;
  --orange:#f0902f; --grass:#7dbf48; --amber:#e0ad33; --sky:#5fa3cc;
}}
:root[data-theme="dark"]{
  --ground:#121614; --panel:#1a201d; --rail:#2a322e; --ink:#e7ebe6; --dim:#93a098;
  --orange:#f0902f; --grass:#7dbf48; --amber:#e0ad33; --sky:#5fa3cc;
}
*{box-sizing:border-box}
html{color-scheme:light dark}
body{margin:0;background:var(--ground);color:var(--ink);font-family:var(--body);font-size:15px;line-height:1.5}
main{max-width:1000px;margin:0 auto;padding-block:28px 64px;padding-left:18px;padding-right:18px}
header{border-bottom:2px solid var(--ink);padding-bottom:14px}
h1{font-family:var(--disp);font-weight:800;font-size:clamp(28px,6vw,44px);margin:0 0 4px;letter-spacing:-.02em;text-wrap:balance}
h1 span{color:var(--orange)}
.sub{color:var(--dim);max-width:64ch;margin:0}
.tally{display:flex;flex-wrap:wrap;gap:6px;margin:16px 0 0;font-family:var(--mono);font-size:12px}
.pip{display:inline-flex;align-items:center;gap:6px;padding:3px 9px;background:var(--panel);border:1px solid var(--rail)}
.dot{width:8px;height:8px;border-radius:50%}
h2{font-family:var(--disp);font-weight:600;font-size:13px;letter-spacing:.14em;text-transform:uppercase;color:var(--dim);
   margin:36px 0 10px;padding-bottom:6px;border-bottom:1px solid var(--rail)}
h2 em{font-style:normal;color:var(--orange);font-family:var(--mono);letter-spacing:0;text-transform:none}
.rows{display:flex;flex-direction:column;gap:2px}
.row{display:grid;grid-template-columns:7px 1fr auto;gap:0 14px;background:var(--panel);border:1px solid var(--rail);align-items:start}
.bar{grid-row:1/-1;align-self:stretch}
.mid{padding:11px 0 12px;min-width:0}
.name{font-weight:600;margin:0;overflow-wrap:anywhere}
.note{color:var(--dim);font-size:13.5px;margin:3px 0 0;max-width:74ch;overflow-wrap:anywhere}
.note code{font-family:var(--mono);font-size:12.5px;background:var(--rail);padding:1px 4px;border-radius:2px}
.right{display:flex;align-items:center;gap:8px;padding:10px 12px 10px 0;flex-wrap:wrap;justify-content:flex-end}
.chip{font-family:var(--mono);font-size:11px;font-weight:600;letter-spacing:.06em;text-transform:uppercase;
      padding:4px 9px;border:1px solid currentColor;white-space:nowrap}
.who{font-family:var(--mono);font-size:11.5px;color:var(--dim)}
footer{margin-top:44px;padding-top:14px;border-top:1px solid var(--rail);color:var(--dim);font-size:12.5px;font-family:var(--mono);
       display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap}
@media (max-width:620px){
  .row{grid-template-columns:5px 1fr}
  .right{grid-column:2;justify-content:flex-start;padding:0 12px 11px 0}
}
"""


def inline(text):
    """Markdown-ish inline: `code`, **bold**, and nothing else. Escaped first."""
    out = html.escape(text)
    out = re.sub(r"`([^`]+)`", r"<code>\1</code>", out)
    out = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", out)
    return out


def split_row(line):
    return [c.strip() for c in line.strip().strip("|").split("|")]


def parse(md):
    """Sections of (title, rows) where a row is (name, state-glyph or None, owner, note)."""
    sections, title, rows, header = [], None, [], None
    for line in md.splitlines():
        if line.startswith("## "):
            if title:
                sections.append((title, rows))
            title, rows, header = line[3:].strip(), [], None
            continue
        if not line.startswith("|") or title is None:
            continue
        cells = split_row(line)
        if set("".join(cells)) <= set("-: "):
            continue
        if header is None:
            header = [c.lower() for c in cells]
            continue
        # Column roles differ per table; find them by header name, fall back to position.
        def col(*names, default=None):
            for n in names:
                if n in header:
                    return cells[header.index(n)]
            return default
        state = col("state") if "state" in header else None
        glyph = next((g for g in STATE if state and g in state), None)
        note = col("evidence / note", "why it is stuck", "why", default="")
        if note == "" and "state" in header and glyph is None:
            note = state or ""
        rows.append((cells[0], glyph, col("owner", "who found", default=""), note))
    if title:
        sections.append((title, rows))
    return sections


def render(md, updated):
    sections = parse(md)
    head = md.split("## ", 1)[0]
    lead = next((l for l in head.splitlines() if l.startswith("**")), "").strip("*")

    counts = {}
    for title, rows in sections:
        if title.lower().startswith("the game"):
            for _, g, _, _ in rows:
                if g:
                    counts[g] = counts.get(g, 0) + 1

    parts = [
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">",
        "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">",
        "<title>TPW Port Board</title>",
        "<link rel=\"stylesheet\" href=\"https://fonts.googleapis.com/css2?"
        "family=Bricolage+Grotesque:opsz,wght@12..96,600;12..96,800&"
        "family=IBM+Plex+Mono:wght@400;600&"
        "family=Source+Sans+3:wght@400;600&display=swap\">",
        f"<style>{CSS}</style></head><body><main>",
        "<header><h1>Theme Park World <span>port board</span></h1>",
        f"<p class=\"sub\">{inline(lead)}</p></header>",
    ]

    if counts:
        parts.append("<p class=\"tally\">")
        for g, (label, color) in STATE.items():
            if counts.get(g):
                parts.append(
                    f"<span class=\"pip\"><i class=\"dot\" style=\"background:{color}\"></i>"
                    f"<b>{counts[g]}</b> {label}</span>"
                )
        parts.append("</p>")

    for title, rows in sections:
        if not rows:
            continue
        parts.append(f"<h2>{html.escape(title)} <em>{len(rows)}</em></h2><div class=\"rows\">")
        for name, glyph, who, note in rows:
            label, color = STATE.get(glyph, ("", "var(--rail)"))
            parts.append(
                f"<div class=\"row\"><i class=\"bar\" style=\"background:{color}\"></i><div class=\"mid\">"
                f"<p class=\"name\">{inline(name)}</p>"
                + (f"<p class=\"note\">{inline(note)}</p>" if note else "")
                + "</div><div class=\"right\">"
                + (f"<span class=\"who\">{html.escape(who)}</span>" if who else "")
                + (f"<span class=\"chip\" style=\"color:{color}\">{label}</span>" if label else "")
                + "</div></div>"
            )
        parts.append("</div>")

    parts.append(
        "<footer><span>generated from STATUS.md — the repo is the source, this is only the view</span>"
        f"<span>{updated}</span></footer></main></body></html>"
    )
    return "".join(parts)


if __name__ == "__main__":
    src = sys.argv[1] if len(sys.argv) > 1 else "STATUS.md"
    dst = sys.argv[2] if len(sys.argv) > 2 else "-"
    out = render(open(src, encoding="utf-8").read(), date.today().isoformat())
    if dst == "-":
        sys.stdout.write(out)
    else:
        open(dst, "w", encoding="utf-8").write(out)
        print(f"wrote {dst} ({len(out)} bytes)")
