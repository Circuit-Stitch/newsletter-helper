#!/usr/bin/env python3
"""
Render Scripts/src/Newsletter Workflow.md into Newsletter Workflow.pdf at the
project root.

The markdown is the source of truth - edit it and re-run this. The PDF is a
build artefact and is meant to be overwritten.

Typeset in the newsletter's own palette, sampled from the real issues and
recorded in newsletter-design-spec.md, so the handout looks like it belongs to
the publication it describes.

Conversion is deliberately stdlib-only, so this runs with the system python3
like the other scripts. It covers the subset of markdown the document actually
uses: headings, paragraphs, tables with column alignment, blockquotes, ordered
and unordered lists with nested block content, fenced code, rules, and inline
bold/italic/code. It is NOT a general markdown implementation.

PDF generation shells out to headless Chrome, which is the only capable
renderer present on this machine (no pandoc, wkhtmltopdf, weasyprint or
LibreOffice).
"""
import html
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "Scripts" / "src" / "Newsletter Workflow.md"
DST = ROOT / "Newsletter Workflow.pdf"

CHROME = Path("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome")


# ---------------------------------------------------------------- inline pass

def inline(text):
    """Inline markdown -> HTML. Code spans are lifted out first so that their
    contents (`<div>`, `Published/*.docx`) survive escaping and emphasis."""
    spans = []

    def stash(m):
        spans.append(html.escape(m.group(1)))
        return "\x00%d\x00" % (len(spans) - 1)

    text = re.sub(r"`([^`]+)`", stash, text)
    text = html.escape(text)
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', text)
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(r"(?<!\*)\*([^*]+)\*(?!\*)", r"<em>\1</em>", text)
    return re.sub(r"\x00(\d+)\x00", lambda m: "<code>%s</code>" % spans[int(m.group(1))], text)


# ----------------------------------------------------------------- block pass

BULLET = re.compile(r"^([-*])\s+(.*)$")
NUMBER = re.compile(r"^(\d+)\.\s+(.*)$")
HEADING = re.compile(r"^(#{1,6})\s+(.*)$")


def is_table(lines, i):
    return (i + 1 < len(lines) and lines[i].lstrip().startswith("|")
            and re.match(r"^\s*\|[\s:|-]+\|\s*$", lines[i + 1]))


def cells(row):
    return [c.strip() for c in row.strip().strip("|").split("|")]


def blocks(lines):
    """Parse a list of lines into HTML. Recurses for list items and quotes."""
    out, i = [], 0
    while i < len(lines):
        line = lines[i]

        if not line.strip():
            i += 1
            continue

        # fenced code
        if line.lstrip().startswith("```"):
            i += 1
            buf = []
            while i < len(lines) and not lines[i].lstrip().startswith("```"):
                buf.append(lines[i])
                i += 1
            i += 1
            out.append("<pre><code>%s</code></pre>" % html.escape("\n".join(buf)))
            continue

        # horizontal rule (a table's separator row always starts with |)
        if re.match(r"^(-{3,}|\*{3,}|_{3,})$", line.strip()):
            out.append("<hr>")
            i += 1
            continue

        m = HEADING.match(line)
        if m:
            level = len(m.group(1))
            out.append("<h%d>%s</h%d>" % (level, inline(m.group(2)), level))
            i += 1
            continue

        # table
        if is_table(lines, i):
            head = cells(lines[i])
            align = []
            for spec in cells(lines[i + 1]):
                if spec.endswith(":") and spec.startswith(":"):
                    align.append("center")
                elif spec.endswith(":"):
                    align.append("right")
                else:
                    align.append("left")
            i += 2
            body = []
            while i < len(lines) and lines[i].lstrip().startswith("|"):
                body.append(cells(lines[i]))
                i += 1

            def row(cs, tag):
                return "".join(
                    '<%s style="text-align:%s">%s</%s>'
                    % (tag, align[j] if j < len(align) else "left", inline(c), tag)
                    for j, c in enumerate(cs))

            out.append("<table><thead><tr>%s</tr></thead><tbody>%s</tbody></table>"
                       % (row(head, "th"),
                          "".join("<tr>%s</tr>" % row(r, "td") for r in body)))
            continue

        # blockquote
        if line.lstrip().startswith(">"):
            buf = []
            while i < len(lines) and lines[i].lstrip().startswith(">"):
                buf.append(re.sub(r"^\s*>\s?", "", lines[i]))
                i += 1
            inner = blocks(buf)
            # Three quote styles, matching the original handout: a headed box
            # for the standing rule, a warm box for asides that open with a
            # bold lead-in, and a plain green rule for everything else.
            if "<h3>" in inner:
                cls = " class=\"rule\""
            elif inner.lstrip().startswith("<p><strong>"):
                cls = " class=\"note\""
            else:
                cls = ""
            out.append("<blockquote%s>%s</blockquote>" % (cls, inner))
            continue

        # lists
        m = BULLET.match(line) or NUMBER.match(line)
        if m:
            ordered = bool(NUMBER.match(line))
            items = []
            while i < len(lines):
                m = NUMBER.match(lines[i]) if ordered else BULLET.match(lines[i])
                if not m:
                    break
                indent = len(lines[i]) - len(lines[i].lstrip())
                buf = [m.group(2)]
                i += 1
                # continuation: blank lines, or anything indented past the marker
                while i < len(lines):
                    nxt = lines[i]
                    if not nxt.strip():
                        # only keep the blank if more indented content follows
                        j = i
                        while j < len(lines) and not lines[j].strip():
                            j += 1
                        if (j < len(lines)
                                and len(lines[j]) - len(lines[j].lstrip()) > indent):
                            buf.append("")
                            i += 1
                            continue
                        break
                    if len(nxt) - len(nxt.lstrip()) > indent:
                        buf.append(nxt.strip() if nxt.strip().startswith("|")
                                   else re.sub(r"^\s{2,3}", "", nxt))
                        i += 1
                        continue
                    break
                inner = blocks(buf)
                # tight item: a lone paragraph loses its wrapper
                only_p = re.fullmatch(r"<p>(.*)</p>", inner, re.S)
                inner = only_p.group(1) if only_p else inner
                # Numbered steps wrap their content so the step badge can be a
                # flex sibling. An absolutely-positioned badge gets painted at
                # the foot of a page even when the item itself breaks to the
                # next one, leaving a sliver behind.
                items.append("<li><div class=\"t\">%s</div></li>" % inner if ordered
                             else "<li>%s</li>" % inner)
            tag = "ol" if ordered else "ul"
            out.append("<%s>%s</%s>" % (tag, "".join(items), tag))
            continue

        # paragraph
        buf = []
        while i < len(lines) and lines[i].strip():
            s = lines[i]
            if (HEADING.match(s) or s.lstrip().startswith((">", "```"))
                    or is_table(lines, i)
                    or re.match(r"^(-{3,}|\*{3,}|_{3,})$", s.strip())
                    or BULLET.match(s) or NUMBER.match(s)):
                break
            buf.append(s.strip())
            i += 1
        if buf:
            out.append("<p>%s</p>" % inline(" ".join(buf)))

    return "\n".join(out)


# --------------------------------------------------------------------- styles

CSS = """
:root{
  --rule:#9BBB59; --deep:#76923C; --pale:#EBF1DE;
  --warm:#EEECE1; --orange:#FAC090; --ink:#363636; --mute:#6d6d66;
}
@page{ size:Letter; margin:0.85in 0.8in; }
*{ box-sizing:border-box; }
body{
  font:11pt/1.62 "Helvetica Neue",Helvetica,Arial,sans-serif;
  color:var(--ink); margin:0; -webkit-print-color-adjust:exact; print-color-adjust:exact;
}
p{ margin:0 0 .78em; }
a{ color:var(--deep); }

.eyebrow{
  font-size:8.5pt; font-weight:700; letter-spacing:.16em; text-transform:uppercase;
  color:var(--deep); margin:0 0 .5em;
}
h1{
  font:700 30pt/1.1 Georgia,"Times New Roman",serif;
  margin:0 0 .35em; letter-spacing:-.01em;
}
h1.section{
  font-size:22pt; margin:0 0 .6em; padding-bottom:.3em;
  border-bottom:2px solid var(--rule); break-before:page;
}
.lede{ font-size:12.5pt; color:var(--mute); max-width:34em; margin:0 0 1.1em; }
.masthead{ border-bottom:2.5px solid var(--rule); padding-bottom:1.1em; margin-bottom:1.6em; }

h2{
  font:700 16pt/1.25 Georgia,"Times New Roman",serif;
  margin:1.7em 0 .55em; break-after:avoid;
}
h3{ font:700 13pt/1.3 Georgia,"Times New Roman",serif; margin:0 0 .5em; break-after:avoid; }
h2+p,h3+p{ margin-top:0; }

hr{ border:0; border-top:1px solid #ddd; margin:1.9em 0; }

/* quotes -------------------------------------------------------------- */
blockquote{
  margin:1.1em 0; padding:.1em 0 .1em 1.1em;
  border-left:3px solid var(--rule); color:#5c6b48; break-inside:avoid;
}
blockquote p:last-child{ margin-bottom:0; }
blockquote.rule{
  background:var(--pale); border-left:0; border-radius:3px;
  padding:1.3em 1.5em; color:var(--ink);
}
blockquote.rule h3{ font-size:15pt; line-height:1.35; margin-bottom:.55em; }
blockquote.rule p{ color:#5f6b52; font-size:10.5pt; }
blockquote.note{
  background:#fdf4ea; border-left:3px solid var(--orange);
  padding:.9em 1.2em; color:var(--ink); font-size:10.5pt;
}
blockquote.note strong{ color:#a9631a; }

/* lists --------------------------------------------------------------- */
ul{ margin:.8em 0; padding-left:1.15em; }
ul li{ margin-bottom:.55em; padding-left:.2em; }
ul li::marker{ color:var(--rule); }

ol{ counter-reset:step; list-style:none; margin:1em 0; padding:0; }
ol li{
  counter-increment:step; display:flex; gap:.85em;
  margin-bottom:.85em; break-inside:avoid; page-break-inside:avoid;
}
ol li::before{
  content:counter(step); flex:0 0 1.75em; height:1.75em; border-radius:50%;
  background:var(--pale); border:1px solid var(--rule); color:var(--deep);
  font-size:9.5pt; font-weight:700; line-height:1.75em; text-align:center;
}
ol li .t{ flex:1; min-width:0; }
ol li .t strong:first-child{ display:block; font-size:11.5pt; margin-bottom:.1em; }

/* tables -------------------------------------------------------------- */
table{
  width:100%; border-collapse:collapse; margin:1.1em 0;
  font-size:9.5pt; break-inside:avoid;
}
th{
  font-size:8pt; font-weight:700; letter-spacing:.1em; text-transform:uppercase;
  color:var(--deep); text-align:left; padding:.55em .7em;
  border-bottom:1.5px solid var(--rule); white-space:nowrap;
}
td{ padding:.5em .7em; border-bottom:1px solid #e8e8e2; vertical-align:top; }
tbody tr:last-child td{ border-bottom:0; }

/* code ---------------------------------------------------------------- */
code{
  font:9.2pt/1.4 "SF Mono",Menlo,Consolas,monospace;
  background:var(--warm); padding:.1em .35em; border-radius:2px; color:#4a4a3f;
}
pre{
  background:#fafaf6; border:1px solid #e4e4d8; border-radius:3px;
  padding:1em 1.2em; margin:1.1em 0; overflow:visible; break-inside:avoid;
}
pre code{ background:none; padding:0; font-size:8.6pt; line-height:1.55; color:#55554a; }

.footer{ margin-top:2.4em; padding-top:1em; border-top:1px solid #ddd;
         font-size:9pt; color:var(--mute); }
"""


def build_html(md):
    body = blocks(md.split("\n"))

    # Lift the opening H1 + bold line + lede into a masthead, the way the
    # original handout was set: association name above, title, then the standfirst.
    m = re.match(r"\s*<h1>(.*?)</h1>\s*<p><strong>(.*?)</strong></p>\s*<p>(.*?)</p>",
                 body, re.S)
    if m:
        head = ('<header class="masthead"><p class="eyebrow">%s</p><h1>%s</h1>'
                '<p class="lede">%s</p></header>' % (m.group(2), m.group(1), m.group(3)))
        rest = body[m.end():]
    else:
        head, rest = "", body

    # Any LATER H1 starts a new page as a section divider. Applied to the tail
    # only - the masthead's own title must not pick up the page break.
    rest = rest.replace("<h1>", '<h1 class="section">')
    body = head + rest

    # The masthead draws its own rule, so a markdown --- straight after it would
    # print a second hairline a few points below the first.
    body = re.sub(r"(</header>)\s*<hr>", r"\1", body)
    # The closing italic line is a colophon, not body copy. It draws its own
    # rule, so drop the markdown one immediately above it rather than printing
    # two hairlines a few points apart.
    body = re.sub(r"<p><em>([^<]*)</em></p>\s*$", r'<p class="footer">\1</p>', body)
    body = re.sub(r"<hr>\s*(?=<p class=\"footer\">)", "", body)

    return ("<!doctype html><html><head><meta charset=\"utf-8\">"
            "<title>Newsletter Workflow</title><style>%s</style></head>"
            "<body>%s</body></html>" % (CSS, body))


def main():
    if not SRC.exists():
        sys.exit("missing %s" % SRC)
    if not CHROME.exists():
        sys.exit("Google Chrome not found at %s - it is what renders the PDF" % CHROME)

    doc = build_html(SRC.read_text(encoding="utf8"))

    with tempfile.TemporaryDirectory() as tmp:
        page = Path(tmp) / "workflow.html"
        page.write_text(doc, encoding="utf8")
        r = subprocess.run(
            [str(CHROME), "--headless", "--disable-gpu", "--no-pdf-header-footer",
             "--print-to-pdf=%s" % DST, page.as_uri()],
            capture_output=True, text=True)
        if not DST.exists():
            sys.exit("chrome failed to render:\n" + (r.stderr or r.stdout))

    print("wrote %s (%.0f KB)" % (DST.relative_to(ROOT), DST.stat().st_size / 1024))


if __name__ == "__main__":
    main()
