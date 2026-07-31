# Windows verification — 30 July 2026

Everything the handoff listed as unverifiable on macOS has now been run on
Windows. This records what was checked, how, and what came out of it.

**Machine:** Windows 11 Pro 26200, .NET SDK 10.0.302, Word **16.0.20228.20124**
(Microsoft 365, Click-to-Run), primary display 2560×1035 at 100% scaling.

> **The one caveat that applies to everything below.** This machine has Word 16,
> not Office 2010. `Documents.Open` and `ExportAsFixedFormat` take the same named
> arguments from Word 2010 through 16, and the orphaned-process and lock-file
> behaviour is version-independent, so these results should carry — but the PDF
> writer itself is a different engine in 2010, and `UseISO19005_1` /
> `DocStructureTags` were not exercised against it.

## Verified

| | Result |
|---|---|
| `MCAANewsletter.csproj` Release build | 0 warnings, 0 errors |
| `DocxTests` Release build | 0 warnings, 0 errors |
| `DocxTests` run | 29 of 30 as found; **30 of 30** after finding 4 was fixed |
| Photo scan vs the Python reference | 24 placements, 12 photos, worst +39.8%, 3.24×2.36in → 2.32×2.36in, chrome excluded — all matched |
| `.exe` architecture | AnyCPU, ILONLY, **no Prefer32Bit** → runs 64-bit |

### `ImageReducer` against real GDI+

Ran `TryReduce` over **87 real media parts** extracted from
`Template/MCAA-Newsletter-MASTER.docx`, `2025 AUGUST NEWSLETTER FINAL.docx` and
`2025 DECEMBER NEWSLETTER FINAL.docx`.

- **0 of 87 re-encoded.** Every photo was already ≤1600px and already JPEG, so
  the no-op guard returned null — matching the README's "0 of 42" claim on the
  draft. 12 `.wdp` parts are undecodable by GDI+ and are excluded by extension
  before any decode is attempted.
- **Opaque-alpha detection works.** A 32bppArgb PNG with every pixel opaque was
  correctly classified as having no meaningful alpha and converted to JPEG
  (1,063,509 → 509,753 bytes).
- **Transparent PNG stays PNG.** A 2400×1800 photographic PNG with a genuinely
  transparent region resized to 1600×1200, kept its alpha, and came out 37.8%
  smaller.
- **The "don't make it bigger" guard fires.** A 2000×1500 PNG of high-frequency
  noise downscaled to a *larger* PNG, so `ImageReducer.cs:77` correctly returned
  null and left it alone.
- **No OOM on a phone photo.** 4284×5712 → 1200×1600, 69.7 MB → 106 KB, 7.8s,
  peak working set 589 MB. Safe because the process is 64-bit; it would have
  been marginal under a 2 GB address space.
- **Second pass is a no-op.** Feeding the reducer's own output straight back in
  returns null every time — the compounding-quality-loss guard holds.

### `WordExport` against real Word

- **Export works.** 11.8s, `%PDF-1.7`, 5 page objects, 24 embedded font
  programs. All twelve named arguments to `ExportAsFixedFormat` accepted.
- **The document is not modified.** SHA-256 of the `.docx` identical before and
  after. This is the load-bearing claim and it holds.
- **No orphaned `WINWORD.EXE`** on success, on a missing file, or on a corrupt
  `.docx`. All three failure paths raise `UserFixableException` with a sentence
  she could act on; the corrupt-file case surfaces Word's own reason
  ("The file appears to be corrupted.").
- **The `~$` derivation is correct.** Word wrote
  `~$26 August MCAA Newsletter-DRAFT.docx` for
  `2026 August MCAA Newsletter-DRAFT.docx`, exactly what `OwnerFilePath`
  computes. Confirmed a second time with a different name length
  (`nl-draft.docx` → `~$-draft.docx`).
- **It does not hijack or kill a running Word.** With Word open on an unrelated
  document holding unsaved edits, exporting left the process alive, the document
  open, still dirty, the edit intact, `Visible` true, and `DisplayAlerts` /
  `ScreenUpdating` back to normal. The dangerous case — the *same* document open
  — is blocked upstream by `IsOpenElsewhere` in both step 2 and step 3.

### Display scaling

The process reports **`PROCESS_DPI_UNAWARE`**, so Windows bitmap-stretches the
whole window at 125% and 150%: proportions are preserved and nothing can clip.
Client area measures exactly the 680×590 the code asks for. Every label and
button was measured against its box at 100% and all fit.

Windows text scaling (Accessibility → Text size) was tested for real at 150% and
the rendered window was **pixel-for-pixel identical** (0 of 109,620 sampled
pixels differed) — the app hardcodes point sizes, so it is immune.

### The three steps, driven through the real UI

Three consecutive months in a clean install folder, clicking the actual buttons.

- **Step 1** created `2026 August MCAA Newsletter-DRAFT.docx`, byte-for-byte
  identical to the master, and offered to open it. Pressed a second time it
  opened the existing draft in Word rather than overwriting — hash and mtime
  unchanged.
- **Step 2** on a draft with a photo stretched 40% by a side handle: detected it,
  showed *"1 photo looks stretched out of shape"* with a thumbnail and the fix
  *"Make the space narrower — 3.2 × 2.4 in becomes 2.3 × 2.4 in. Nothing is cut
  off."*, applied it, and re-scanned clean. **Media parts stayed at 22** across
  the export, so the read-only open genuinely does not re-bloat the file. A
  `(before photo fix)` backup was written alongside.

  Two separate pictures were stretched and the dialog reported one photo. That is
  the intended grouping, not a miss: `PlacementCount = 2`,
  `DistinctPhotos.Count = 1` — both placements reference the same media part and
  are collapsed by content fingerprint so she is not shown the same photo twice.
- **Step 3** copied the PDF **byte-for-byte** (SHA-256 match), left the draft
  untouched, and left the master untouched with the checkbox unticked.
- **Master update.** Unticked on August → master unchanged. Ticked on September →
  master replaced (8,241,155 → 9,000,711 bytes) and the message added *"Next
  month will start from this issue."*

### Downsampling, end to end

A 4284×5712 JPEG through the whole chain:

| stage | size | pixels |
|---|---:|---|
| original file | 1,588,138 | 4284×5712 |
| September draft, after Word saved it | 753,982 | 1683×2244 |
| **September published** | **214,828** | **1200×1600** |
| master (carried forward) | 214,828 | 1200×1600 |
| October draft | 214,828 | 1200×1600 |
| **October published** | **214,828** | **1200×1600** |

The draft keeps the full-size copy. The published copy is downsampled once. When
the photo is carried into the next month it comes out **byte-for-byte
identical** — no second re-encode, no compounding loss.

### Failure paths

| Situation | What she sees |
|---|---|
| Draft open in Word, step 2 | *"The newsletter is still open in Word. Please save it and close Word, then click this button again."* |
| Month already started | Button reads "Open in Word"; opens the existing draft, never overwrites |
| Month not started | Steps 2 and 3 greyed with *"Available once you have started the newsletter."* |
| `.exe` outside the newsletter folder | Names the folder it is in and says to move it |
| PDF from last export still open | Handled in `WordExport`, message asks her to close it |
| Draft PDF deleted, then Publish | Not reachable through the window — deleting the PDF demotes step 3 before she can click it, so `"The PDF has not been made yet"` is defensive code only |

## Findings

Findings 1–4 were fixed after this run; the descriptions below are the state as
found, and each records what was done. Findings 5 and 6 are behaviour to know
about, not defects.

| | Finding | Outcome |
|---|---|---|
| 1 | Stale `~$` file dead-ends the app | fixed — offers to clear it |
| 2 | Published month with the draft tidied away shows nonsense | fixed — steps re-check what is on disk |
| 3 | "came down from 8.4 MB to 8.4 MB" | fixed — only said when the figures differ |
| 4 | Collision-guard check can never pass from a clean clone | fixed — the test builds its own fixture |
| 5 | Word compresses photos on insert | by design, documented |
| 6 | Window taller than a 1080p screen at 175% | edge case, documented |

### 1. A stale `~$` file leaves her permanently stuck — worth fixing

If Word crashes (or is killed, or the USB stick is pulled), its `~$` owner file
survives. `IsOpenElsewhere` returns true on the owner file alone, so steps 2 and
3 refuse to run and tell her to *"save it and close Word"* — which she has
already done and which cannot help. There is no way out from inside the app.

Reproduced with Word not running and the file not locked:

```
WINWORD running          : NO
stale owner file on disk : True
file opens exclusively   : yes - not locked
IsOpenElsewhere says     : True      <- blocks step 2 and step 3
```

Deleting the `~$` file cleared it immediately. This matters here specifically:
the handoff notes the USB stick "already had Word crash-recovery leftovers on
it", so this has happened at least once already, and the person it happens to has
no command line.

**Fixed.** The owner file is in the check for a good reason — Word does not
always hold an exclusive handle — so it was kept, and only the dead case was
given a way out. `WordExport.HasStaleOwnerFile` is true when the owner file
exists *and* the document opens exclusively, which means nothing is really
holding it; `ClearToTouchTheDraft` in `MainForm` then asks:

> **Left over from last time**
> The September 2026 newsletter looks like it is open in Word, but Word does not
> have it — this is usually what Word leaves behind when it closes unexpectedly.
> Shall I tidy that up and carry on?

Yes deletes the leftover and the step continues. Verified both ways: with a
planted stale file the dialog appears, the file is removed and the PDF is made;
with Word genuinely holding the document `HasStaleOwnerFile` is false and the
original *"still open in Word"* message is shown unchanged, so the guard is not
weakened.

The stale-versus-live distinction rests on Word holding an exclusive handle
while it has the document open, and **that was confirmed on Word 16 only**. If
Word 2010 ever leaves the document unlocked with an owner file present, this
would offer to clear a lock that is real. The dialog asks before doing anything,
so the worst case is a question she can answer No to — but it is worth a check
on her machine.

### 2. Publish, then tidy up Drafts, and the window goes wrong in three places

`IssueState.For` marks all three steps `Done` when `FullyPublished`, and never
re-checks that the draft survived. But `DraftStarted` is only assigned
`if (s.DraftExists)`. So once the published files exist, the draft can be gone
while the window still claims it is there.

Reproduced by deleting `2026 October MCAA Newsletter-DRAFT.docx` and its `.pdf`
after publishing October. She sees:

```
✓  1. Start the October 2026 newsletter
      Started . Your copy is in the Drafts folder.        [ Open in Word ]
✓  2. Check the photos and make the PDF
      PDF made 31 December.                          [ Make the PDF again ]
```

Three separate faults from the one cause:

- **"Started ."** — [`MainForm.cs:154`](MCAANewsletter/MainForm.cs#L154).
  `DraftStarted` is null, `Format` returns `""`, and the sentence still asserts
  the copy is in the Drafts folder when it is not.
- **"PDF made 31 December"** — [`MainForm.cs:183`](MCAANewsletter/MainForm.cs#L183)
  calls `File.GetLastWriteTime(Issue.DraftPdf)` with no existence check. For a
  missing file .NET returns `1601-01-01 UTC`, which in local time formats as
  *31 December*. Every other date goes through `IssueState`'s existence-checked
  nullable; this one call bypasses it.
- **The button lies.** It reads *"Open in Word"*, but `Step1Clicked` tests
  `state.DraftExists`, finds false, and falls through to `File.Copy` — so it
  silently starts a **fresh draft from the master**. Confirmed by clicking it:
  the draft did not exist before, existed after, and the dialog said *"Your copy
  of the October 2026 newsletter is ready."*

Nothing is destroyed — the fresh draft is written only because there was none —
but for someone who tidies up her Drafts folder after publishing, the window
describes a state that does not exist and a button does something other than
what it says.

**Fixed** in `IssueState.For`: when `FullyPublished`, steps 1 and 2 are now
derived from whether the draft and its PDF are actually still there, rather than
being assumed done. `MainForm` reads the new existence-checked
`DraftPdfMadeOn` instead of calling `File.GetLastWriteTime` directly. The same
scenario now reads:

```
▶  1. Start the October 2026 newsletter
      Makes your own copy for October 2026 from the master, ready to edit.
                                                  [ Start this newsletter ]
○  2. Check the photos and make the PDF
      Available once you have started the newsletter.
✓  3. Publish
      Published 30 July. All done.       [ Open the Published folder ]
```

### 3. "came down from 8.4 MB to 8.4 MB" — [`MainForm.cs:353`](MCAANewsletter/MainForm.cs#L353)

The saving is announced whenever `BytesSaved > 0`, but formatted `{0:0.0} MB`.
On the August issue the real change was 8,788,577 → 8,787,040 bytes, so she was
told the file *"came down from 8.4 MB to 8.4 MB"*. Cosmetic, but it reads as a
program that has miscounted.

**Fixed** — the sentence is now added only when the two rounded figures actually
read differently. The saving still happens either way; it just is not announced
when there is nothing visible to announce.

### 4. `DocxTests` check 30 can never pass from a clean checkout

```
[FAIL] the name-collision guard was actually exercised
       -> no collisions occurred, so the guard is unproven
```

Not a regression. The guard fires when one media stem exists under two
convertible extensions, and **no document in this corpus has that** — verified
across all 24 `.docx` files in `Drafts/`, `Published/` and `Template/`. The
README's "the master has `image2`…`image13` twice over" describes the
*Word-bloated 42-part* master that existed on the macOS machine; the committed
master is the clean 22-part version, so the condition is absent.

Since `Drafts/MCAA-Newsletter-DRAFT.docx` is gitignored, this check would fail
for anyone cloning the repo.

**Fixed** by giving the test its own fixture rather than by weakening the
assertion. `BuildCollisionFixture` copies a real document into the scratch
directory, renaming one convertible part so its stem already exists as a `.jpeg`
— `image8.png` becomes `image2.png` while `image2.jpeg` is still there — and
repoints the relationship to match. Re-encoding then genuinely wants a name that
is taken, so `UniqueMediaName` has to resolve it, and the guard is proven from
any checkout instead of depending on a Word-bloated master happening to be
lying around.

The donor is padded past the largest part in the document first: the stand-in
re-encoder only rewrites parts larger than the smallest JPEG, and without that
the fixture built but the donor was skipped, so the collision never occurred —
the check went on failing with the fixture in place until this was found.

Suite now reports **PASS — 30 checks**.

### 5. Word compresses pictures on insert, so downsampling has less to do

Inserting a 4284×5712 photo and saving produced a **1683×2244** image in the
`.docx` — Word resamples to 220 ppi of the displayed size on save. The app's
downsampling still fires (1683 > 1600) and still halves it again, but the
README's "20 MB → 0.36 MB" describes a document Word has not yet touched. This
also explains why 0 of 87 archived photos needed re-encoding.

One consequence worth knowing: if she inserts a large photo **and** stretches it
before the first save, Word resamples it to the stretched box and the repair can
no longer see it. Observed directly — a freshly inserted 4284×5712 photo
stretched +40% came back as 2356×2244, matching the box exactly, with nothing
left to detect.

**This is limited to photos Word is still downsampling.** Stretching photos that
are already in the newsletter was tested separately and Word left every one of
them alone: two pictures stretched +40%, saved, and all **22 media parts came
back byte-for-byte identical** — 0 resampled, 0 re-encoded. Both stretches were
detected. So the blind spot applies to the first save after inserting an
oversized photo, not to ordinary editing of the newsletter's existing pictures.

### 6. At 175% scaling the window is taller than a 1080p screen

696×629 at 100% → 1044×944 at 150% (fits a 1080p display) → **1218×1101 at 175%**
(does not). The form is `FixedSingle` with `MaximizeBox = false`, so it could not
be resized to cope. 150% is the common maximum on 1080p, so this is an edge case
— but worth knowing before she changes a display setting.

## Repository state

`Template/MCAA-Newsletter-MASTER.docx` is **clean** here — 8,241,155 bytes, 22
media parts, matching the committed version, and `git status` agrees. The open
decision in the handoff about the re-bloated master does not apply to this
checkout.

Two files were left in `Published/` by a run before this verification started —
`2026 August MCAA Newsletter.docx` and `.pdf`. They were trial output, not a real
issue, and have been **removed** on Kyle's instruction. `Published/` is otherwise
untouched: it is the archival record of what actually went out.

Changed by this work:

```
 M App/DocxTests/Program.cs          collision fixture (finding 4)
 M App/MCAANewsletter/IssueState.cs  step state re-checks the disk (finding 2)
 M App/MCAANewsletter/MainForm.cs    stale-lock recovery, dates, MB wording
 M App/MCAANewsletter/WordExport.cs  HasStaleOwnerFile / RemoveOwnerFile
?? App/VERIFICATION.md               this file
```

All verification ran in `%TEMP%\mcaa-install`, `%TEMP%\mcaa-com`,
`%TEMP%\mcaa-ui`, `%TEMP%\mcaa-gdi` and `%TEMP%\mcaa-media1`. Nothing in the
repository was modified.
