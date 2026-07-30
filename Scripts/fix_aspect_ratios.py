#!/usr/bin/env python3
"""
Repair stretched/squashed photos, promoting Drafts/MCAA-Newsletter-DRAFT.docx
to the finished Template/MCAA-Newsletter-MASTER.docx. Last of the three passes.

Each picture sits in a box whose aspect ratio does not match the photo's own, so
Word stretches it. Two different repairs, chosen by severity:

  <= 10% off   centred a:srcRect crop. The box keeps its exact footprint, and a
               trim of a few percent off two edges is invisible. Non-destructive:
               the full image stays embedded and Word's crop tool can drag it
               back out.

  >  10% off   shrink the over-long box dimension instead. A crop this deep
               removes real content - on one artwork it was cutting off the
               artist's signature. Resizing loses nothing, and because the box
               only ever gets SMALLER, it cannot collide with anything in the
               220-text-box floating layout.

Both copies of every picture are fixed, since Word 2010 writes a second copy of
each drawing into an mc:Fallback branch.
"""
import zipfile, re, struct, sys
from pathlib import Path

# Paths hang off the repo root so the script runs from any working directory.
# The draft is kept as its own file rather than edited in place, so this pass
# stays re-runnable without rebuilding it from the archive each time.
ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "Drafts" / "MCAA-Newsletter-DRAFT.docx"
DST = ROOT / "Template" / "MCAA-Newsletter-MASTER.docx"

TOL = 0.005          # leave anything within 0.5% alone
CROP_LIMIT = 0.10    # above this, resize the box rather than crop the photo
EMU = 914400

# Decorative chrome must NOT be corrected. The green header pills (763x38,
# 1092x77) are deliberately stretched to page width, and cropping them would
# slice off their rounded end caps. Real photos here are all at least 263 px on
# the short side and never beyond 3:1, so these thresholds separate them cleanly.
MIN_SHORT_EDGE = 200
MAX_NATIVE_RATIO = 3.0


def dim(b):
    if b[:8] == b"\x89PNG\r\n\x1a\n":
        return struct.unpack(">II", b[16:24])
    if b[:2] == b"\xff\xd8":
        i = 2
        while i < len(b) - 9:
            if b[i] != 0xFF:
                i += 1; continue
            m = b[i + 1]
            if m in (0xC0,0xC1,0xC2,0xC3,0xC5,0xC6,0xC7,0xC9,0xCA,0xCB,0xCD,0xCE,0xCF):
                h, w = struct.unpack(">HH", b[i + 5:i + 9]); return w, h
            if m in (0xD8, 0xD9) or 0xD0 <= m <= 0xD7:
                i += 2; continue
            i += 2 + struct.unpack(">H", b[i + 2:i + 4])[0]
    return None


def main():
    if not SRC.exists():
        sys.exit(f"missing {SRC} - run make_word_master.py first")
    z = zipfile.ZipFile(SRC)
    doc = z.read("word/document.xml").decode("utf8")
    rels = z.read("word/_rels/document.xml.rels").decode("utf8")
    rmap = dict(re.findall(r'Id="(rId\d+)"[^>]*Target="([^"]+)"', rels))
    sizes = {}
    for n in z.namelist():
        if n.startswith("word/media/"):
            d = dim(z.read(n))
            if d:
                sizes["media/" + n.split("/")[-1]] = d

    log, skipped = [], []

    def fix_inline(m):
        blk = m.group(0)
        mb = re.search(r'<a:blip[^>]*r:embed="(rId\d+)"', blk)
        me = re.search(r'<a:ext cx="(\d+)" cy="(\d+)"', blk)
        if not (mb and me):
            return blk
        tgt = rmap.get(mb.group(1), "")
        if tgt not in sizes:
            return blk
        w, h = sizes[tgt]
        name = tgt.replace("media/", "")

        if min(w, h) < MIN_SHORT_EDGE or not (1 / MAX_NATIVE_RATIO < w / h < MAX_NATIVE_RATIO):
            skipped.append((name, w, h))
            return blk

        cl = cr = ct = cb = 0.0
        sr = re.search(r"<a:srcRect([^/>]*)/>", blk)
        if sr:
            for k, v in re.findall(r'([lrtb])="(-?\d+)"', sr.group(1)):
                val = int(v) / 100000
                if k == "l": cl = val
                elif k == "r": cr = val
                elif k == "t": ct = val
                elif k == "b": cb = val

        ew, eh = w * (1 - cl - cr), h * (1 - ct - cb)
        cx, cy = int(me.group(1)), int(me.group(2))
        if ew <= 0 or eh <= 0 or cx == 0 or cy == 0:
            return blk

        target, current = cx / cy, ew / eh
        dist = target / current - 1
        if abs(dist) <= TOL:
            return blk

        # how much of the photo a crop would cost
        if current > target:
            crop_cost = (ew - eh * target) / w
        else:
            crop_cost = (eh - ew / target) / h

        if crop_cost <= CROP_LIMIT:
            # --- mild: centred crop, footprint untouched ---
            if current > target:
                cl += crop_cost / 2; cr += crop_cost / 2; axis = "sides"
            else:
                ct += crop_cost / 2; cb += crop_cost / 2; axis = "top/bottom"
            new = ('<a:srcRect l="%d" t="%d" r="%d" b="%d"/>'
                   % (round(cl * 100000), round(ct * 100000),
                      round(cr * 100000), round(cb * 100000)))
            if sr:
                blk = blk.replace(sr.group(0), new, 1)
            else:
                blk = re.sub(r"(<a:stretch\b)", new + r"\1", blk, count=1)
            log.append((name, dist * 100, "crop", crop_cost * 100, axis,
                        cx / EMU, cy / EMU, cx / EMU, cy / EMU))
        else:
            # --- severe: shrink the over-long dimension, never grow ---
            if dist > 0:
                ncx, ncy = round(cy * current), cy      # box too wide -> narrow it
                axis = "width"
            else:
                ncx, ncy = cx, round(cx / current)      # box too tall -> shorten it
                axis = "height"
            assert ncx <= cx and ncy <= cy, "resize must only shrink"
            # wp:extent and a:ext are NOT always equal - Word leaves a small
            # effect-extent delta on most pictures here. Scale each by the same
            # factor rather than assuming they match, or wp:extent silently
            # keeps the old size and the two disagree.
            sx, sy = ncx / cx, ncy / cy
            we = re.search(r'<wp:extent cx="(\d+)" cy="(\d+)"', blk)
            if we:
                wx, wy = int(we.group(1)), int(we.group(2))
                blk = blk.replace(we.group(0),
                                  '<wp:extent cx="%d" cy="%d"' % (round(wx * sx), round(wy * sy)), 1)
            blk = blk.replace('<a:ext cx="%d" cy="%d"' % (cx, cy),
                              '<a:ext cx="%d" cy="%d"' % (ncx, ncy))
            log.append((name, dist * 100, "resize", 0.0, axis,
                        cx / EMU, cy / EMU, ncx / EMU, ncy / EMU))
        return blk

    out = re.sub(r"<wp:inline\b.*?</wp:inline>", fix_inline, doc, flags=re.S)

    zo = zipfile.ZipFile(DST, "w", zipfile.ZIP_DEFLATED, compresslevel=9)
    for item in z.infolist():
        data = out.encode("utf8") if item.filename == "word/document.xml" else z.read(item.filename)
        zo.writestr(item, data)
    zo.close(); z.close()

    seen = {}
    for e in log:
        seen.setdefault(e[0], e)
    print(f"{'image':15}{'was off':>9}  {'repair':7}{'detail':>10}  {'box before':>13}{'box after':>13}")
    for name, dist, kind, cost, axis, bw, bh, aw, ah in sorted(seen.values(), key=lambda x: -abs(x[1])):
        detail = f"{cost:.1f}% {axis}" if kind == "crop" else f"{axis}"
        chg = f"{aw:.2f}x{ah:.2f}in" if kind == "resize" else "unchanged"
        print(f"{name[:14]:15}{dist:+8.1f}%  {kind:7}{detail:>10}  {f'{bw:.2f}x{bh:.2f}in':>13}{chg:>13}")
    print(f"\n{len(log)} placements corrected ({len(seen)} unique photos)")
    print(f"   cropped : {len({e[0] for e in log if e[2]=='crop'})}")
    print(f"   resized : {len({e[0] for e in log if e[2]=='resize'})}")
    if skipped:
        u = {s[0]: s for s in skipped}
        print(f"\nleft alone as decorative chrome:")
        for n_, w_, h_ in u.values():
            print(f"   {n_:15} {w_}x{h_}")
    print(f"\nwrote {DST}")


if __name__ == "__main__":
    main()
