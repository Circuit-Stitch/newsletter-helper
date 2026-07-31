# The one-time migration — technical notes

These three Python scripts cleaned up twelve years of accumulated newsletter and built the
first master. **They have done their job.** The monthly workflow is now the Windows app in
`App/` — see `App/README.md`. Nothing here runs on her machine anyway: no Python, no Pillow,
no command line.

They are kept because they are the record of what was done to the archive, and because the
app's document surgery is a port of their logic. Do not run them casually — pass 2 and 3 in
particular are bootstrap steps hard-wired to the August 2026 issue.

| # | Script | Reads | Writes |
| --- | --- | --- | --- |
| 1 | `shrink_archive.py` | `Published/*.docx` | the same files, in place |
| 2 | `make_word_master.py` | `Published/2026 AUG newsletter_final.docx` | `Drafts/MCAA-Newsletter-DRAFT.docx` |
| 3 | `fix_aspect_ratios.py` | `Drafts/MCAA-Newsletter-DRAFT.docx` | `Template/MCAA-Newsletter-MASTER.docx` |

`render_workflow.py` is separate and still current: it rebuilds `Newsletter Workflow.pdf`
from `Scripts/src/Newsletter Workflow.md`. Edit the markdown, re-run the script. It shells out
to headless Chrome, which is the only capable renderer on this machine.

## What was actually wrong

- **210 PNGs totalling 1,058 MB** across 12 issues — 88% of the drive. All full-resolution
  camera photos (4284×5712, 3024×4032), all RGB with no alpha channel, so PNG bought nothing.
  Only 32 JPEGs, averaging 0.55 MB.

- **Compatibility mode 14 duplicates every image** — not the Word version, which is what it
  first looked like. Every issue writes `mc:AlternateContent` fallbacks, but only mode 14
  gives each fallback its own private copy of the image bytes:

  | Issue | Mode | AltContent | Media parts | Dup |
  | --- | --- | --- | --- | --- |
  | March 2026 | 12 | 0 | 29 / 29 | no |
  | May 2026 | 15 | 118 | 25 / 25 | no |
  | August 2026 | 14 | 111 | 41 / 22 | **yes** |

  Mode 15 writes fallbacks but shares one image part. Word for Mac 16.0 reproduced the
  duplication on this document too, because the *document* is mode 14. Since mode 14 is Word
  2010's native format, she cannot convert her way out of it.

- **Every issue is a Save-As of the previous one.** The rsid count in `settings.xml` climbs
  monotonically and never resets: 7,471 → 9,027 from July 2025 to August 2026. Along with it
  rode 1,259 stale web `<div>`s and 99.5 MB of never-subsetted embedded fonts.

- **24% of placed photos are geometrically distorted** (42 of 174 real photos, excluding
  decorative rules), 9% by more than 10%, worst at +39.8%. 40% carry a `srcRect` crop, so she
  is using the crop tool — she is just fighting side handles.

## This is why the app still de-dupes every month

The duplication is **not** historical cruft that was cleaned once. It comes back on every
save, because the document is mode 14 and she saves every month. Measured on the master in
this repository:

| | zip | media parts | duplicates | rsids |
| --- | ---: | ---: | ---: | ---: |
| As committed, post-cleanup | 7.86 MB | 22 | 0 | 0 |
| After one open-and-save in Word | 13.70 MB | 42 | 19 | 4 |

The app's publish step undoes it again on the way into `Published/`. On the current draft that
is 13.52 MB → 10.52 MB, 42 media parts back down to 22.

Note that a PDF export shrinking to ~6 MB says nothing about this — that is the PDF being
re-encoded. The `.docx` keeps every duplicate part.

The app also downsamples on publish, at the same 1600px / q82 this pipeline used. That adds
almost nothing on the current draft — 10.52 MB → 10.40 MB — because these photos were already
reduced by `shrink_archive.py`. The app detects that and skips them rather than re-encoding,
which is the difference between a one-shot script and one that runs every month: without the
check, a photo carried forward through the master would lose quality on every publish. The
gain is entirely on fresh material, where a 4284×5712 phone photo goes from 20.1 MB to
0.36 MB.

## How the distortion was repaired

Two repairs, chosen by severity. Worst case went from **39.8% off to 0.1%** across all 24
placements (12 unique photos, each appearing twice because of the Word 2010 fallback).

- **Under 10% — centred `srcRect` crop.** Box footprint untouched, so nothing in the floating
  layout can shift. Non-destructive: the full image stays embedded, and Word's crop tool
  drags it back out. Seven photos.
- **Over 10% — shrink the over-long box dimension.** A deep crop removes real content: on the
  butterfly watercolour it was slicing off the artist's signature. Resizing loses nothing, and
  because a dimension only ever gets *smaller*, it cannot create a collision. Five photos,
  e.g. 3.24×2.36in → 2.32×2.36in.
- The green header pills (763×38, 1092×77) are deliberately stretched to page width and were
  excluded — cropping them would slice off their rounded end caps.

This is the logic `App/MCAANewsletter/DocxPackage.cs` ports, thresholds unchanged.
`App/DocxTests` checks the port against the exact numbers above.

## Results of the archive pass

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

The larger remainders are embedded fonts, not images — December alone carries 17.7 MB of
them. Fonts were deliberately kept so the script-font signature survives on any machine;
dropping them would roughly halve the master again.

Verified against the pre-cleanup originals in git: `word/document.xml` is **byte-identical in
all 20 documents**, every image relationship still resolves, every XML part parses, and no
content type is left uncovered. Layout and text cannot have changed, because they were never
touched.

## Two things not fixed

- **She was working directly on the USB stick.** FAT32, no journaling, 1.9 GB total, with Word
  crash-recovery leftovers on it including a lock file dated 1997. The app is meant to live in
  a folder on her own drive; the stick should only ever be handoff.
- **The layout is 88–240 floating text boxes** with no tables and no real columns. It renders
  well but it is brittle — page 4 of the May issue is already clipping a line of text inside a
  box. A rebuild on tables would fix it; nothing short of that will.

## The Canva route, not taken

`newsletter-design-spec.md` in the project root has everything needed to rebuild the
newsletter in Canva: page setup and margins, the two-part grid with per-page column splits,
the sampled colour palette, the type scale as actually used, the seven standing elements
worth building once, and the photo-frame shapes. Geometry is measured from the rendered May
2026 PDF at 150 dpi and colours from pixel sampling.

The single most valuable thing in it: build every photo slot as a Canva **Frame**, which makes
stretching impossible rather than merely discouraged. The Windows app catches the same problem
after the fact instead. The spec is kept in case the Canva move is ever revisited.
