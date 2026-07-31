# MCAA Newsletter — the Windows app

A single window with three numbered steps, for producing the monthly newsletter
on Windows 11 with Office 2010. It replaces the three Python scripts in
`Scripts/`, which were one-time migration tools and cannot run on her machine.

```
Which newsletter?   [ August 2026 ▼ ]

 ✓  1. Start the August 2026 newsletter        [ Open in Word ]
 ▶  2. Check the photos and make the PDF       [ Check and make the PDF ]
 ○  3. Publish                                 [ Publish ]
```

Exactly one button is enabled at a time. State is read off the filesystem on
every refresh — the app stores nothing between runs, so it cannot disagree with
what is actually in the folder.

## Building

Requires the .NET SDK. The project targets `net48`, which is preinstalled on
Windows 11, so there is no runtime for her to install.

```
dotnet build  App/MCAANewsletter/MCAANewsletter.csproj -c Release
```

The `Microsoft.NETFramework.ReferenceAssemblies` package means this also
compiles on macOS and Linux — useful for checking the build, though the
resulting `.exe` only runs on Windows.

Output lands in `App/MCAANewsletter/bin/Release/MCAA Newsletter.exe`. It has no
dependencies beyond the framework, so that one file is the whole program.

## Installing on her PC

1. Copy the newsletter folder — `Drafts/`, `Published/`, `Template/` — anywhere
   on her machine. Her Desktop is fine. **Not** the USB stick: it is FAT32 with
   no journaling and already had Word crash-recovery leftovers on it.
2. Drop `MCAA Newsletter.exe` into that folder, beside those three subfolders.
3. Run it once yourself. Windows will show *"Windows protected your PC"* because
   the `.exe` is unsigned — click **More info → Run anyway**. That only happens
   the first time, but do it for her rather than leaving her to meet it alone.

On first run the app finds the folders by looking at where it is, so a normal
install needs no setting up at all.

Settings are per Windows account. If two people log into that PC under
different accounts, each sets it up once.

## Where it looks

Settled in this order, and the order matters:

1. **Saved settings**, in `%APPDATA%\MCAA Newsletter\settings.txt`. If they exist
   they are the answer.
2. **Otherwise** — first run only — a walk up from the `.exe` looking for a
   folder holding `Template/MCAA-Newsletter-MASTER.docx`.
3. **Whatever came of those is re-checked**, every launch. If it does not hold
   up, the settings window opens with the reason on screen.

Auto-detection deliberately does *not* run as a fallback when saved settings
turn out to be wrong. A folder that has moved — a network drive offline, a
renamed directory — would otherwise see the program quietly adopt some other
newsletter folder near itself and start work on the wrong master. Stopping to
ask is the better failure.

The settings window (*"Change where the files live…"*, bottom right) sets the
folder, the three subfolder names, the master file name, the per-issue name
pattern and the draft ending. Every box re-checks the whole setup as it is
typed and **Save stays greyed until it is usable** — a settings screen that
lets you save a folder that is not there has only moved the failure to a worse
moment. Refusing a pattern without both `{year}` and `{month}` is the check
that matters most: without them every issue resolves to one file name and each
month overwrites the last.

Note what is *not* saved: anything about the state of an issue. Whether a draft
exists, whether the PDF is made, whether it is published — still read off the
filesystem on every refresh, so the app still cannot disagree with what is
actually in the folder.

Delete `settings.txt` and the program goes back to finding the folder itself.

## What each step does

**1. Start** copies `Template/MCAA-Newsletter-MASTER.docx` to
`Drafts/{YYYY} {Month} MCAA Newsletter-DRAFT.docx` and offers to open it. It
refuses to overwrite an existing draft.

**2. Check and make the PDF** scans for stretched photos, offers to repair them,
then exports the PDF beside the draft. The document is opened **read-only** and
closed **without saving** — a Word save on this document re-duplicates every
image and restores the revision IDs, which is the bloat the app exists to avoid.

**3. Publish** copies the PDF byte-for-byte to `Published/{YYYY} {Month} MCAA
Newsletter.pdf`, writes a slimmed `.docx` beside it, and optionally — unticked
by default, behind an explicit confirmation — makes that slimmed document the
new master.

## The two document operations

Both work directly on the zip/OOXML package. Neither goes through Word.

**Photo repair** (`DocxPackage.RepairPhotos`) is a port of
`Scripts/fix_aspect_ratios.py`, thresholds unchanged. A photo up to 10% out of
shape gets a centred `<a:srcRect>` crop, leaving the box footprint alone; beyond
10% the over-long box dimension is reduced instead, because a crop that deep
removes real content — on one artwork it was cutting off the artist's signature.
A box may only ever get *smaller*, which is asserted in code, so nothing in the
floating layout can shift. Pictures under 200px on the short side or beyond 3:1
are left alone as decorative chrome; that exclusion is what stops the green
header pills having their rounded end caps sliced off.

**Slimming** (`DocxPackage.Slim`) combines the de-dupe from
`Scripts/shrink_archive.py` with the cruft strip from `make_word_master.py`. It
collapses byte-identical media parts, repoints the relationships, and removes
revision IDs and stale web markup. Embedded fonts are kept, so the script-font
signature still renders anywhere.

This is not a one-time cleanup. Word 2010 writes the document in Compatibility
Mode 14, which gives every `mc:AlternateContent` fallback its own private copy of
the image bytes — so the duplication comes back on **every save**. Measured on
the master in this repo: 22 media parts and 7.86 MB as committed, 42 parts and
13.70 MB after one open-and-save in Word.

Downsampling (1600px / q82) is **on** for publishing. It is the only operation
here with a real tradeoff: 1600px is still sharper than the page prints — a photo
filling the 5.6in main column reproduces at roughly 285 dpi — but the stored
resolution is genuinely reduced. Her working draft is never re-encoded, so the
full-resolution original stays in `Drafts/` if a photo ever needs recovering.

**A photo already at printing size is left completely alone.** If it is within
1600px and already a JPEG, re-encoding would buy nothing and cost a little
quality, so `ImageReducer` returns null without touching it. This matters because
the tool runs every month rather than once: if the master is carried forward from
a published issue, its photos arrive already reduced — and Word drops the zip
processed-stamp whenever it saves, so the stamp alone cannot stop a second pass.
Judging by the image itself always can. Without this, photos that persist across
issues would lose a little quality every month, compounding.

Measured on the current draft, whose photos were already reduced during the
migration: **0 of 42 photos re-encoded**. On a simulated 4284×5712 phone photo,
the case this exists for: **20.1 MB → 0.36 MB, 98% smaller**.

Re-encoding changes a part's extension, which means the relationship targets and
`[Content_Types].xml` have to be rewritten, and part names kept unique.
That last one is not theoretical: these documents contain stems that already
exist under two extensions — the master has `image2`, `image3`, `image4`,
`image6`, `image9`, `image11` and `image13` twice over — so converting
`image2.png` would land on the existing `image2.jpeg`. `UniqueMediaName` handles
it, and the test suite confirms the guard actually fires on the real corpus
rather than sitting there untested.

### Why slimming cannot change the layout

Every piece of layout geometry — box extents, crops, anchors — lives in
`word/document.xml`. Slimming is allowed to remove revision-ID attributes from
that part and nothing else, and `AssertLayoutPreserved` checks exactly that
before the output file is kept: strip rsids from both the input and the output,
and the two must be identical. `AssertRelationshipsResolve` then confirms no
picture reference was left pointing at a part that no longer exists. If either
check fails the file is discarded and nothing is overwritten.

## Testing against known-correct output

`Drafts/MCAA-Newsletter-DRAFT.docx` in this repo is the pre-repair pipeline
intermediate, so it is a ready-made test case with known answers. Scanning it
must produce **24 placements across 12 photos** (each photo appears twice —
Word 2010 writes a second copy into the `mc:Fallback` branch, named `imageN` and
`imageN0`), with these at the extremes:

| image | out by | repair | box before | box after |
|---|---:|---|---|---|
| `image5.jpeg` | +39.8% | resize width | 3.24 × 2.36 in | 2.32 × 2.36 in |
| `image18.jpeg` | −16.2% | resize height | 1.73 × 3.45 in | 1.73 × 2.89 in |
| `image15.jpeg` | +8.8% | crop 8.1% top/bottom | 1.85 × 2.55 in | unchanged |
| `image16.jpeg` | +1.1% | crop 1.1% top/bottom | 2.78 × 2.17 in | unchanged |

`image8.png` (763 × 38) and `image12.png` (1092 × 77) must be reported as
decorative chrome and left alone.

Both copies of the master — the one on disk and `git show
HEAD:Template/MCAA-Newsletter-MASTER.docx` — must scan clean, with zero
distortions. That is the regression check: the repair already ran on them.

## Known limits

- The `.exe` is unsigned, so SmartScreen warns on first run per machine.
- Word must be closed for steps 2 and 3. The app checks and says so plainly,
  looking both for Word's `~$` owner file and for an exclusive-open failure.
- Only `wp:inline` pictures are repaired. Verified sufficient on this document:
  43 inline blocks, 43 `a:blip` references, zero `wp:anchor` pictures and zero
  VML `v:imagedata`. Every photo sits inline inside one of the 111 floating text
  boxes. Anything the repair cannot read is reported rather than skipped
  silently.
- Orphan media parts are reported by `Slim`, not deleted. The draft has 42 media
  parts against 43 blip references and no header/footer images, so some parts may
  be unreferenced; that is worth confirming on real data before anything starts
  deleting parts.
