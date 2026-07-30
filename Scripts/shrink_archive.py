#!/usr/bin/env python3
"""
Archive cleanup for the MCAA newsletter .docx files.

Rewrites Published/*.docx in place. Every other file in Published/ is left
alone - in particular each issue's .pdf, which is the archival record of what
actually went out, and the PRINT THIS/ subfolder.

Each document is rebuilt into a .tmp alongside it and only swapped in if it
came out smaller, so an interrupted run cannot leave a half-written file.
Original modification times are kept, so the issue dates survive. The USB stick
is never touched.

Safe to re-run. Each cleaned document is stamped in its zip comment with the
settings it was processed under, and files already carrying the current stamp
are skipped untouched. Without that, a second pass would re-encode every photo
over 400px for almost no size gain and a little lost JPEG quality.

What it does:
  1. Downsamples oversized photos and re-encodes them as JPEG (keeps PNG only
     where transparency is genuinely used).
  2. Collapses byte-identical media parts onto a single part (this is what
     undoes the Word 2010 duplicate-fallback bloat).
  3. Rewrites .rels targets and [Content_Types].xml to match.

It does NOT touch fonts, layout, text, or the .wmf/.emf/.wdp parts, so the
documents still open and render the same.
"""
import zipfile, shutil, os, re, io, sys, hashlib
from pathlib import Path
from PIL import Image

# Paths hang off the repo root so the script runs from any working directory.
ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "Published"     # rewritten in place; .pdf and subfolders untouched

MAX_EDGE = 1600      # px on the long side - ample for a 8.5x11 print page
JPEG_Q = 82
MIN_PHOTO = 400      # below this on the short side, treat as an icon/rule and leave alone

Image.MAX_IMAGE_PIXELS = None

# Processed-marker. A .docx is a zip, and the zip's end-of-central-directory
# comment is not part of the OPC package - no OOXML reader parses it - so this
# rides along without touching a single document part. The settings are baked
# in: change MAX_EDGE or JPEG_Q and every file legitimately becomes eligible
# again. Word rewrites the zip on save and drops the comment, which is the
# right answer too, since a Word save re-inflates the images.
MARKER_FMT = b"MCAA-shrink v1 max_edge=%d q=%d min_photo=%d"


def marker():
    return MARKER_FMT % (MAX_EDGE, JPEG_Q, MIN_PHOTO)


def already_clean(path):
    try:
        with zipfile.ZipFile(path) as z:
            return z.comment == marker()
    except Exception:
        return False        # unreadable or not a zip - let shrink() report it


def alpha_is_used(im):
    if im.mode not in ("RGBA", "LA", "PA") and "transparency" not in im.info:
        return False
    try:
        a = im.convert("RGBA").getchannel("A").getextrema()
        return a[0] < 250
    except Exception:
        return True


def process_image(raw, name):
    """Return (new_bytes, new_ext) or None to keep the original untouched."""
    ext = os.path.splitext(name)[1].lower()
    if ext not in (".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"):
        return None
    try:
        im = Image.open(io.BytesIO(raw))
        im.load()
    except Exception:
        return None

    w, h = im.size
    if min(w, h) < MIN_PHOTO and max(w, h) <= MAX_EDGE:
        return None

    keep_png = alpha_is_used(im)

    if max(w, h) > MAX_EDGE:
        s = MAX_EDGE / max(w, h)
        im = im.resize((max(1, round(w * s)), max(1, round(h * s))), Image.LANCZOS)

    out = io.BytesIO()
    if keep_png:
        im.save(out, "PNG", optimize=True)
        new_ext = ".png"
    else:
        if im.mode not in ("RGB", "L"):
            im = im.convert("RGB")
        im.save(out, "JPEG", quality=JPEG_Q, optimize=True, progressive=True)
        new_ext = ".jpeg"

    nb = out.getvalue()
    if len(nb) >= len(raw) and new_ext == ext:
        return None
    return nb, new_ext


def shrink(path, outpath):
    zin = zipfile.ZipFile(path)
    names = zin.namelist()

    media = {}          # old name -> (bytes, new name)
    by_hash = {}        # content hash -> canonical new name
    rename = {}         # old name -> canonical new name

    for n in names:
        if not n.startswith("word/media/"):
            continue
        raw = zin.read(n)
        res = process_image(raw, n)
        if res:
            data, ext = res
            newn = os.path.splitext(n)[0] + ext
        else:
            data, newn = raw, n
        h = hashlib.sha1(data).hexdigest()
        if h in by_hash:
            rename[n] = by_hash[h]          # dedupe onto the earlier identical part
        else:
            by_hash[h] = newn
            rename[n] = newn
            media[newn] = data

    basemap = {os.path.basename(k): os.path.basename(v) for k, v in rename.items()}

    zout = zipfile.ZipFile(outpath, "w", zipfile.ZIP_DEFLATED, compresslevel=6)
    written = set()
    exts_used = set()

    for item in zin.infolist():
        n = item.filename
        if n.startswith("word/media/"):
            newn = rename[n]
            if newn in written or newn not in media:
                continue
            zout.writestr(newn, media[newn])
            written.add(newn)
            exts_used.add(os.path.splitext(newn)[1].lstrip(".").lower())
            continue

        data = zin.read(n)

        if n.endswith(".rels"):
            t = data.decode("utf8", "ignore")
            def fix(m):
                pre, fn = m.group(1), m.group(2)
                return 'Target="%s%s"' % (pre, basemap.get(fn, fn))
            t = re.sub(r'Target="((?:\.\./)?media/)([^"]+)"', fix, t)
            data = t.encode("utf8")

        if n == "[Content_Types].xml":
            t = data.decode("utf8", "ignore")
            for e in sorted(exts_used | {"jpeg", "png"}):
                if 'Extension="%s"' % e not in t:
                    ct = {"jpeg": "image/jpeg", "png": "image/png"}.get(e)
                    if ct:
                        t = t.replace("<Types ", '<Types ', 1)
                        t = re.sub(r"(<Types[^>]*>)",
                                   r'\1<Default Extension="%s" ContentType="%s"/>' % (e, ct),
                                   t, count=1)
            data = t.encode("utf8")

        zout.writestr(item, data)

    zout.comment = marker()
    zout.close()
    zin.close()


def main():
    files = sorted(SRC.glob("*.docx"))     # non-recursive: PRINT THIS/ is left alone
    print(f"{'file':46}{'before':>10}{'after':>10}{'saved':>9}")
    tb = ta = 0
    for f in files:
        if f.name.startswith("~$"):
            continue
        if already_clean(f):
            b = f.stat().st_size
            tb += b; ta += b
            print(f"{f.name[:45]:46}{b/1048576:9.1f}M{'already clean':>19}")
            continue
        tmp = f.parent / (f.name + ".tmp")
        try:
            shrink(f, tmp)
        except Exception as e:
            # leave the original in place - it is still the good copy
            print(f"{f.name[:45]:46}  FAILED: {e}")
            tmp.unlink(missing_ok=True)
            continue
        b, a = f.stat().st_size, tmp.stat().st_size
        if a >= b:
            # Nothing left to win - keep the original. It therefore stays
            # unstamped and gets retried on the next run, which costs a moment
            # of CPU and nothing else: the re-encode is thrown away with tmp,
            # so no quality is lost.
            tmp.unlink()
            tb += b; ta += b
            print(f"{f.name[:45]:46}{b/1048576:9.1f}M{'unchanged':>19}")
            continue
        shutil.copystat(f, tmp)            # keep the issue's original date
        os.replace(tmp, f)
        tb += b; ta += a
        print(f"{f.name[:45]:46}{b/1048576:9.1f}M{a/1048576:9.1f}M{100*(1-a/b):8.0f}%")
    print(f"\n{'TOTAL':46}{tb/1048576:9.1f}M{ta/1048576:9.1f}M{100*(1-ta/max(tb,1)):8.0f}%")


if __name__ == "__main__":
    main()
