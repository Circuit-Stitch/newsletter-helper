# MCAA Newsletter

A small Windows app that produces the Mendocino County Art Association's monthly
newsletter. One window, three numbered steps:

1. **Start the monthly issue**: copies the newsletter template into the `Drafts` folder under a dated file name, ready for you to write in.
2. **Check the photos and make the PDF**: finds photos that have been squashed or stretched out of shape, offers to put them right, then makes the PDF next to the draft.
3. **Publish**: copies the finished PDF into the `Published` folder without the "DRAFT" in its name, ready for sending out to your readers. A slimmed-down copy of the Word document goes with it, and your draft stays where it is.

<p align="center">
  <a href="https://circuitstitchpackages.blob.core.windows.net/packages/MCAANewsletter.appinstaller">
    <img src=".github/assets/download-for-windows.png" alt="Download for Windows" width="282">
  </a>
</p>

<p align="center">
  <a href="https://github.com/Circuit-Stitch/newsletter-helper/releases/latest">Release notes and older versions</a>
</p>

## Installing

[Download the MCAA Newsletter installer](https://circuitstitchpackages.blob.core.windows.net/packages/MCAANewsletter.appinstaller) and open it. Windows hands the file to App Installer and installs the app.

The app is signed by Circuit Stitch, so Windows installs it without stopping to
warn you about it.

The button and link above always serve the current version. The same file is attached to every
[release](https://github.com/Circuit-Stitch/newsletter-helper/releases) if you
would rather download it from there.

Then open **MCAA Newsletter** from the Start menu. The first launch asks where
the newsletters are. Point it at the folder holding the three subfolders:

```
MyNewsletters/        <- point the app here
├── Template/         the document each issue starts from
├── Drafts/           the issue being worked on
└── Published/        finished issues
```

Call that top folder anything, and keep it anywhere. The
[full layout](#the-newsletter-folder) below shows the files that end up in it.

Updates take care of themselves. Windows checks for a new version each time the
app starts and installs it in the background.

Settings are stored per Windows account, so each user of a shared PC sets the
folder once.

## Requirements

| | |
|---|---|
| Windows | 10 version 1809 or later, or Windows 11. x64 |
| Microsoft Word | Needed for step 2 only. Tested against Word 2010 |
| .NET | None to install. The app targets .NET Framework 4.8, which ships with Windows |

## The Three Steps

Exactly one button is enabled at a time.

```
Which newsletter?   [ August 2026 ▼ ]

 ✓  1. Start the August 2026 newsletter        [ Open in Word ]
 ▶  2. Check the photos and make the PDF       [ Check and make the PDF ]
 ○  3. Publish                                 [ Publish ]
```

**1. Start** copies the master template to
`Drafts/{YYYY} {Month} MCAA Newsletter-DRAFT.docx` and offers to open it. It
refuses to overwrite an existing draft.

**2. Check and make the PDF** finds photos that have been stretched out of
shape, offers to repair them, then exports the PDF beside the draft. The
document is opened read-only and closed without saving.

**3. Publish** copies the PDF to `Published/`, writes a slimmed `.docx` beside
it, and can optionally promote that slimmed document to the new master.

The app stores nothing between runs. Every status you see is read off the
filesystem on each refresh, so it cannot disagree with what is actually in the
folder.

## The Newsletter Folder

Everything the app touches lives in one folder. You choose where it sits and what
it is called.

```
MyNewsletters/
│
├── Template/
│   ├── MCAA-Newsletter-MASTER.docx             what every issue starts from
│   └── MCAA-Newsletter-MASTER (previous).docx  safety copy
│
├── Drafts/
│   ├── 2026 August MCAA Newsletter-DRAFT.docx  step 1 makes this
│   ├── 2026 August MCAA Newsletter-DRAFT.pdf   step 2 makes this
│   └── 2026 August MCAA Newsletter-DRAFT (before photo fix).docx
│
└── Published/
    ├── 2026 July MCAA Newsletter.docx
    ├── 2026 July MCAA Newsletter.pdf
    ├── 2026 August MCAA Newsletter.docx        step 3 makes these two
    └── 2026 August MCAA Newsletter.pdf
```

The two files in parentheses are written only when they are needed.
`(previous)` appears when you promote a slimmed document to master, so the old
master is never simply gone. `(before photo fix)` appears when photo repair
runs, so the unrepaired draft is still there.

Every name above is a setting:

| Piece | Default |
|---|---|
| Subfolder names | `Template`, `Drafts`, `Published` |
| Master file | `MCAA-Newsletter-MASTER.docx` |
| Per-issue pattern | `{year} {month} MCAA Newsletter` |
| Draft ending | `-DRAFT` |

The pattern has to keep both `{year}` and `{month}`, and the settings window
refuses to save one that drops either. Without them every issue resolves to the
same file name and each month overwrites the last.

Month names come from the invariant culture. A machine set to another locale
still writes `August`, so the archive cannot end up with two spellings of the
same month.

## What the Document Operations Do

Both work directly on the `.docx` zip package. Neither goes through Word.

**Photo repair** fixes pictures whose box no longer matches their real aspect
ratio. A photo up to 10% out of shape gets a centered crop, which leaves the box
footprint alone. Beyond 10% the box is shrunk instead, because a crop that deep
starts removing real content. A box may only ever get smaller, so nothing in the
floating layout shifts. Small or very wide pictures are treated as decorative
chrome and left alone.

**Slimming** collapses byte-identical copies of the same image, repoints the
relationships, and strips revision IDs and stale web markup. Embedded fonts are
kept.

Slimming is not a one-time cleanup. Word 2010 writes the document in
Compatibility Mode 14, which gives every fallback branch its own private copy of
the image bytes, so the duplication returns on every save. Measured on the master
in this repository: 22 media parts and 7.86 MB as committed, 42 parts and
13.70 MB after a single open-and-save in Word.

Publishing also downsamples photos to 1600px at quality 82. A photo already
within that size and already a JPEG is left untouched, so images carried from
one issue to the next do not lose quality every month. On a 4284×5712 phone
photo the reduction is 20.1 MB to 0.36 MB.

Slimming cannot change the layout, and this is checked rather than assumed. All
layout geometry lives in `word/document.xml`. Slimming may remove revision-ID
attributes from that part and nothing else. Before the output file is kept, both
input and output are stripped of revision IDs and compared — they must be
identical — and every picture reference is confirmed to still resolve. If either
check fails the file is discarded and nothing is overwritten.

## Scope

This was written for one newsletter and one person's workflow, so the app's
folder layout and file naming are configurable but its shape is not. The two
document operations are general: any Word document written by Word 2010 carries
the same duplicated image bytes.

## App Development

### Building From Source

Requires the .NET SDK.

```
dotnet build "App/MCAANewsletter/MCAANewsletter.csproj" -c Release
```

Output lands in `App/MCAANewsletter/bin/Release/MCAA Newsletter.exe`. It has no
dependencies beyond the framework, so that one file is the whole program.

The project pulls `Microsoft.NETFramework.ReferenceAssemblies`, so it also
compiles on macOS and Linux. The resulting `.exe` still only runs on Windows.

A locally built `.exe` is unsigned, so SmartScreen warns on first run. The
released MSIX is signed and does not.

### Packaging

The app ships as an MSIX, signed by Circuit Stitch through Azure Artifact
Signing, with a companion `.appinstaller` that keeps installed copies up to
date. [RELEASING.md](RELEASING.md) covers how a release is built and published.

### See Also

- [App/README.md](App/README.md) — how the app finds its folder, what settings
  it validates, and the full test corpus with expected numbers.
- [RELEASING.md](RELEASING.md) — how a release is built, signed and published.
- [Scripts/README.md](Scripts/README.md) — the Python tools this app replaced.
  Three were one-time migration steps, kept as the record of what was done to
  the archive. The fourth still rebuilds the workflow diagram.

## License

Source code is covered under the [MIT License](LICENSE). The newsletters themselves are not part of this repository and
are not covered by it.

