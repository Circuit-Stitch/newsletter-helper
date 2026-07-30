#!/usr/bin/env python3
"""
Build Drafts/MCAA-Newsletter-DRAFT.docx from the already-image-compressed
August issue. This is the second of three passes; fix_aspect_ratios.py turns
the draft into the finished Template/MCAA-Newsletter-MASTER.docx.

Strips accumulated editing cruft that has been riding along in this document
for years, WITHOUT touching layout, text, fonts, or images:

  - 9,027 revision-ID (rsid) records in settings.xml
  - every w:rsid* attribute sprayed through document.xml
  - 1,259 stale HTML <w:div> records in webSettings.xml (left over from
    content pasted out of email/web over the years)
  - the document-protection / "last edited by" metadata

Embedded fonts are deliberately KEPT, so the script-font signature still
renders on any machine.
"""
import zipfile, re, shutil, sys, hashlib
from pathlib import Path

# Paths hang off the repo root so the script runs from any working directory.
ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "Published" / "2026 AUG newsletter_final.docx"
# A file is named for where it is, not for how far through the pipeline it got:
# DRAFT in Drafts/, MASTER in Template/. There is only ever one of each.
DST = ROOT / "Drafts" / "MCAA-Newsletter-DRAFT.docx"


def strip(data_xml: bytes) -> bytes:
    t = data_xml.decode("utf8", "ignore")
    # editing-session attributes on every paragraph/run/table row
    t = re.sub(r'\s+w:rsid(?:R|RDefault|P|RPr|Tr|Del|Sect)="[0-9A-Fa-f]+"', "", t)
    return t.encode("utf8")


def main():
    if not SRC.exists():
        sys.exit(f"missing {SRC}")

    zin = zipfile.ZipFile(SRC)

    # SRC is the archive copy itself, which shrink_archive.py rewrites in place,
    # so its existence no longer proves the image pass has run (the old cleaned/
    # staging dir used to prove exactly that). Duplicate media parts mean it has
    # not - the master would still build, just many times larger than it needs.
    media = [n for n in zin.namelist() if n.startswith("word/media/")]
    dupes = len(media) - len({hashlib.sha1(zin.read(n)).hexdigest() for n in media})
    if dupes:
        print(f"WARNING: {SRC.name} still carries {dupes} duplicate image parts.\n"
              f"         Run shrink_archive.py first for a far smaller master.\n")

    DST.parent.mkdir(exist_ok=True)
    zout = zipfile.ZipFile(DST, "w", zipfile.ZIP_DEFLATED, compresslevel=9)

    report = {}
    for item in zin.infolist():
        n = item.filename
        data = zin.read(n)
        before = len(data)

        if n == "word/document.xml":
            data = strip(data)

        elif n == "word/settings.xml":
            t = data.decode("utf8", "ignore")
            t = re.sub(r"<w:rsids>.*?</w:rsids>", "", t, flags=re.S)
            data = t.encode("utf8")

        elif n == "word/webSettings.xml":
            t = data.decode("utf8", "ignore")
            t = re.sub(r"<w:divs>.*?</w:divs>", "", t, flags=re.S)
            data = t.encode("utf8")

        elif n == "docProps/core.xml":
            t = data.decode("utf8", "ignore")
            t = re.sub(r"<cp:lastModifiedBy>.*?</cp:lastModifiedBy>", "", t, flags=re.S)
            t = re.sub(r"<cp:revision>.*?</cp:revision>", "<cp:revision>1</cp:revision>", t, flags=re.S)
            data = t.encode("utf8")

        if len(data) != before:
            report[n] = (before, len(data))
        zout.writestr(item, data)

    zout.close()
    zin.close()

    print(f"{'part':30}{'before':>12}{'after':>12}{'saved':>9}")
    for k, (b, a) in sorted(report.items(), key=lambda x: -(x[1][0] - x[1][1])):
        print(f"{k[:29]:30}{b/1024:11.0f}K{a/1024:11.0f}K{100*(1-a/b):8.0f}%")
    print(f"\n{'FILE':30}{SRC.stat().st_size/1048576:11.1f}M"
          f"{DST.stat().st_size/1048576:11.1f}M"
          f"{100*(1-DST.stat().st_size/SRC.stat().st_size):8.0f}%")
    print(f"\nwrote {DST}")


if __name__ == "__main__":
    main()
