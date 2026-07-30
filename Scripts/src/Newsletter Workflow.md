# Newsletter Workflow

**Mendocino County Art Association**

How to put the newsletter together each month without the file growing to 200 megabytes.

---

## What was going wrong

> Nothing you did. The newsletter file has been handed down and re-saved for years, and one
> habit in it quietly multiplies — photos going in at full camera size.

A photo straight off a phone is about 4,000 pixels wide. On the page it prints about an inch
and a half. Word keeps every one of those pixels anyway. Across the last twelve issues that
added up to **1.2 gigabytes** of newsletters — of which about 95% was photo data nobody ever
sees.

Two smaller things came along for the ride. Photos were being stretched to fit the columns, so
about a quarter of them are subtly the wrong shape. And Office 2010 keeps a hidden second copy
of every image, which doubled the file again.

## The one rule

> ### Never put a photo into the newsletter straight from a phone, a camera, or an email attachment. Shrink it first.
>
> Everything else on this page is detail. If only one thing sticks, make it this one — it is the
> difference between a 7 MB newsletter and a 200 MB one.

A good working size is **1600 pixels** on the long side. That is still sharper than anything the
newsletter prints, and it is roughly forty times smaller than the original.

| A real photo from the September issue | Size |
| --- | --- |
| As it went in — 4284 × 5712, saved as PNG | 26.4 MB |
| Shrunk to 1600 px, saved as JPEG | **0.45 MB** |

Side by side on the printed page, they look the same.

---

## Monthly workflow in Canva

Canva is free, runs in a web browser on any computer, and makes the two biggest problems
impossible rather than merely avoidable — it shrinks photos for you on the way out, and photos
dropped into a frame cannot be stretched out of shape.

1. **Open last month's newsletter and make a copy.**
   File → Make a copy. Rename it to the new month straight away, so there is never a question
   about which one you are working in.
2. **Change the date in the green bar.**
   Click the header bar across the top of page one and type the new month. Do the same on the
   page footers.
3. **Swap the photos.**
   Click a photo, click Upload, choose the new one, and drag it onto the frame. Canva crops it
   to fit the space automatically. To change which part of the photo shows, double-click it and
   slide it around.
4. **Update the standing sidebar.**
   The workshops-and-events list on page one and the exhibit list on page two are the parts that
   change quietly. Do these before the articles, while you are still fresh.
5. **Paste in the submissions.**
   Click into a text box and paste. If the text does not fit, make the box taller rather than
   shrinking the type below 9 point — that is the size the newsletter has always used, and
   smaller starts to be hard to read.
6. **Download as PDF Print.**
   Share → Download → PDF Print. This is the file you send out and the file that gets archived.
   It should land somewhere around 1–5 MB.
7. **Save the PDF where the others live.**
   The Canva design stays in Canva — you do not need to save a copy of it anywhere. Only the PDF
   needs filing.

> **One limit worth knowing.** The free Canva account holds 5 GB of uploaded photos. That is a
> few years of newsletters. When it eventually fills, delete the oldest uploads — the finished
> designs are not affected.

---

## If you stay in Word instead

This works, and the file stays reasonable, but it takes more discipline because Word will not
stop you doing the wrong thing.

1. **Start from the master file.**
   Open `Template/MCAA-Newsletter-MASTER.docx` and immediately do Save As with the new month's
   name. Never work in the master itself.
2. **Shrink the photos before you touch Word.**
   Install **Microsoft PowerToys** — it is free and official. It adds a "Resize pictures" option
   when you right-click photos in a folder. Select the month's photos, right-click, resize to
   Large (1600px). It makes copies, so the originals are safe.
3. **Insert, never paste.**
   Use Insert → Picture and pick the resized file. Pasting a photo out of an email is what puts
   a full-size image into the document.
4. **Only ever drag the corner handles.**
   Dragging a corner keeps the photo's shape. Dragging the handle on a side or the top squashes
   it. This is exactly how a quarter of the photos in past issues ended up looking slightly off
   — one was stretched 40% too wide. The twelve in the master file have already been put right;
   this rule is about keeping it that way.
5. **Compress once before the final save.**
   Click any photo → Picture Tools → Compress Pictures → All pictures, Print (220 ppi), and tick
   "Delete cropped areas". Then save, and make the PDF.

> **Expect the file to roughly double when you save.** That is the Office 2010 quirk, and it is
> normal. Starting from photos that are the right size, it means about 8 MB becomes about 11 MB
> — which is fine. It only became a problem before because it was doubling photos that were
> already far too big.

---

## Asking contributors for photos

The easiest version of this problem is the one that never arrives. Something like this in the
submissions email helps:

> "Photos are welcome — please send them as JPEG, and if your phone or email offers a size when
> you attach them, Medium or Large is perfect. Full size is more than the newsletter can use."

Most phone mail apps offer exactly that choice when attaching a photo, and most people will pick
the smaller option if asked.

---

# Technical notes — for Kyle

Everything now lives in this repository. The USB stick is only ever read, never written to.

```
MCAA_Newsletter/
├── Newsletter Workflow.pdf          ← this document
├── newsletter-design-spec.md
├── Published/                       ← the archive, cleaned in place (.pdf untouched)
├── Drafts/                          ← work in progress
│   └── MCAA-Newsletter-DRAFT.docx
├── Template/
│   └── MCAA-Newsletter-MASTER.docx
└── Scripts/
    ├── shrink_archive.py
    ├── make_word_master.py
    ├── fix_aspect_ratios.py
    ├── render_workflow.py           ← rebuilds the PDF below from the source above
    └── src/
        └── Newsletter Workflow.md   ← edit this, then re-run render_workflow.py
```

A file is named for where it lives, not for how far through the pipeline it got: **DRAFT** in
`Drafts/`, **MASTER** in `Template/`. There is only ever one of each.

## What was actually wrong

- **210 PNGs totalling 1,058 MB** across 12 issues — 88% of the drive. All full-resolution
  camera photos (4284×5712, 3024×4032), all RGB with no alpha channel, so PNG bought nothing.
  Only 32 JPEGs, averaging 0.55 MB.

- **Compatibility mode 14 duplicates every image** — not the Word version, which is what it
  first looked like. Every issue writes `mc:AlternateContent` fallbacks, but only mode 14 gives
  each fallback its own private copy of the image bytes:

  | Issue | Mode | AltContent | Media parts | Dup |
  | --- | --- | --- | --- | --- |
  | March 2026 | 12 | 0 | 29 / 29 | no |
  | May 2026 | 15 | 118 | 25 / 25 | no |
  | August 2026 | 14 | 111 | 41 / 22 | **yes** |

  Mode 15 writes fallbacks but shares one image part. Word for Mac 16.0 reproduced the
  duplication on this document too, because the *document* is mode 14. Since mode 14 is Word
  2010's native format, she cannot convert her way out of it — which is why the answer is small
  images rather than a settings change. It was 25.7 MB on August alone, and it recurs on every
  save.

- **Every issue is a Save-As of the previous one.** The rsid count in `settings.xml` climbs
  monotonically and never resets: 7,471 → 9,027 from July 2025 to August 2026. Along with it
  rode 1,259 stale web `<div>`s and 99.5 MB of never-subsetted embedded fonts.

- **24% of placed photos are geometrically distorted** (42 of 174 real photos, excluding
  decorative rules), 9% by more than 10%, worst at +39.8%. 40% carry a `srcRect` crop, so she is
  using the crop tool — she is just fighting side handles.

## How the distortion was repaired

Two repairs, chosen by severity. Worst case went from **39.8% off to 0.1%** across all 24
placements (12 unique photos, each appearing twice because of the Word 2010 fallback).

- **Under 10% — centred `srcRect` crop.** Box footprint untouched, so nothing in the floating
  layout can shift. Non-destructive: full image stays embedded, and Word's crop tool drags it
  back out. Seven photos.
- **Over 10% — shrink the over-long box dimension.** A deep crop removes real content: on the
  butterfly watercolour it was slicing off the artist's signature. Resizing loses nothing, and
  because a dimension only ever gets *smaller*, it cannot create a collision. Five photos,
  e.g. 3.24×2.36in → 2.32×2.36in.
- The green header pills (763×38, 1092×77) are deliberately stretched to page width and were
  excluded — cropping them would slice off their rounded end caps.
- Verified after: no box grew, `wp:extent` / `a:ext` proportion preserved on every picture,
  media bytes byte-identical, decorative chrome untouched.

## The three passes

Each script resolves its paths from the repository root, so they run from any directory.

| # | Script | Reads | Writes |
| --- | --- | --- | --- |
| 1 | `shrink_archive.py` | `Published/*.docx` | the same files, in place |
| 2 | `make_word_master.py` | `Published/2026 AUG newsletter_final.docx` | `Drafts/MCAA-Newsletter-DRAFT.docx` |
| 3 | `fix_aspect_ratios.py` | `Drafts/MCAA-Newsletter-DRAFT.docx` | `Template/MCAA-Newsletter-MASTER.docx` |

**Pass 1 rewrites the archive in place.** Only `.docx` files are touched — each issue's `.pdf`
is the archival record of what actually went out and is never opened. `PRINT THIS/` is left
alone (the glob is deliberately non-recursive). Each document is rebuilt into a `.tmp` alongside
it and only swapped in if it came out smaller, so an interrupted run cannot leave a half-written
file, and original modification times are preserved so the issue dates survive.

**It is safe to re-run.** Each cleaned document is stamped in its zip comment —
`MCAA-shrink v1 max_edge=1600 q=82 min_photo=400` — and files carrying the current stamp are
skipped untouched. The zip's end-of-central-directory comment is not part of the OPC package, so
no OOXML reader ever sees it. Without the stamp, a second pass would re-encode every photo over
400px for almost no size gain and a little lost JPEG quality. The settings are baked in, so
changing `MAX_EDGE` or `JPEG_Q` correctly makes every file eligible again. Word drops the comment
when it rewrites the zip on save — also correct, since a Word save re-inflates the images.

## Results

Pass 1, all 20 documents: **1,198.1 MB → 164.9 MB (86%)**. The twelve newsletter issues are
below; the remaining eight are submissions drafts and one-offs, together 14.5 MB before.

| Issue | Before | After | Saved |
| --- | ---: | ---: | ---: |
| 2025 August | 193.0 MB | 20.1 MB | 90% |
| 2025 September | 184.9 MB | 20.1 MB | 89% |
| 2026 March | 132.2 MB | 10.8 MB | 92% |
| 2026 May | 125.6 MB | 15.9 MB | 87% |
| 2026 April | 106.6 MB | 13.1 MB | 88% |
| 2025 December | 92.9 MB | 20.0 MB | 78% |
| 2026 February | 86.4 MB | 13.1 MB | 85% |
| 2025 October | 81.8 MB | 15.7 MB | 81% |
| 2026 August | 56.1 MB | 7.9 MB | 86% |
| 2025 July | 49.5 MB | 7.2 MB | 85% |
| 2026 January | 45.4 MB | 8.2 MB | 82% |
| 2025 November | 29.1 MB | 9.3 MB | 68% |

The larger remainders are embedded fonts, not images — December alone carries 17.7 MB of them.
Fonts were deliberately kept so the script-font signature survives on any machine; dropping them
would roughly halve the master again.

Verified against the pre-cleanup originals in git: `word/document.xml` is **byte-identical in all
20 documents**, every image relationship still resolves, every XML part parses, and no content
type is left uncovered. Layout and text cannot have changed, because they were never touched.

Passes 2 and 3 turn the August issue into the master: cruft stripped (rsids, webSettings divs,
stale metadata) and all twelve distorted photos corrected, landing at **7.9 MB** from the archive
copy's 56.1 MB. That remainder is 4.6 MB of embedded fonts, 3.1 MB of images and 0.2 MB of
everything else — so the fonts are now the largest thing in the file by some margin.

Verified after pass 3: the text is character-for-character identical to the draft, and of the 154
picture boxes, **none grew** — the repair only ever shrinks a box or crops inside it, so nothing
in the floating layout can shift.

Opening the master in Word and saving it undoes part of this. Word restores the rsids and
re-duplicates every image, which took the file to 13.9 MB last time it happened. That is the
Office 2010 behaviour described above, not a fault — just re-run passes 2 and 3 to rebuild it.

## Two things I did not fix

- **She is working directly on the USB stick.** It is FAT32 with no journaling, 1.9 GB total, and
  it had Word crash-recovery leftovers on it including a lock file dated 1997. Worth moving her
  working copy to the desktop or OneDrive and using the stick only for handoff.
- **The layout is 88–240 floating text boxes** with no tables and no real columns. It renders
  well but it is brittle — page 4 of the May issue is already clipping a line of text inside a
  box. Canva sidesteps this; a Word rebuild would not, unless it were rebuilt on tables.

## Building the Canva template

`newsletter-design-spec.md`, in the project root, has the measurements
needed to rebuild the newsletter in Canva: page setup and margins, the two-part grid with
per-page column splits, the sampled colour palette, the type scale as actually used, the seven
standing elements worth building once, and the photo-frame shapes. Geometry is measured from the
rendered May 2026 PDF at 150 dpi and colours from pixel sampling, so it describes the real
document rather than an approximation of it.

The single most valuable thing in it: build every photo slot as a Canva **Frame**. A photo
dropped into a frame is cropped to fill and cannot be stretched, which makes the distortion
described above impossible rather than merely discouraged.

---

*Prepared from the twelve issues on the MCAA drive, July 2025 – August 2026.*
