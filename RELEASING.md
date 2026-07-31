# Releasing

The app ships as a **signed MSIX** with a companion `.appinstaller`. Windows'
built-in App Installer does the updating: once she has installed from the
`.appinstaller` URL, it checks the release page on every launch and installs a
new version in the background. **Publishing the draft GitHub Release is the
entire release action** — there is nothing to tell her and nothing for her to
click.

Ported from the [Janitor](https://github.com/Circuit-Stitch/Janitor) release
workflow, minus the Linux and macOS jobs (this app drives Word over COM, so
Windows is the only platform) and minus its hand-written "Check for updates"
button. Janitor has that button because it is a secrets tool that must never
phone home; this app has no such constraint, so the update policy lives in the
`.appinstaller` XML and there is no update code in the app at all.

## Cutting a release

**One-click (preferred).** Actions → Release → *Run workflow* → type the version
(e.g. `1.0.1`). The `setup` job bumps `<Version>` in
`App/MCAANewsletter/MCAANewsletter.csproj`, commits it to `main`, and the same run
builds and drafts the release off that commit.

**Manual.** Bump `<Version>` in the csproj yourself, then:

```
git tag v1.0.1 && git push origin v1.0.1
```

`vX.Y.Z` must equal the csproj `<Version>` — `setup` fails a tag push otherwise.
The one-click path keeps them equal by construction.

Either way the run ends at a **draft** Release. Review it, edit the *What
changed* section of the notes, then **Publish**. Only then does
`…/releases/latest/download/MCAANewsletter.appinstaller` point at the new
version — so the review before Publish is the release gate. A draft is not
"latest", so a half-finished release never advertises an update.

A draft also defers git-tag creation until Publish, so a failed build leaves no
dangling tag and the run is safely re-runnable.

**Dry run.** Run the workflow with an **empty** version: it builds and packs the
MSIX but signs nothing, uploads nothing and drafts no Release. Use it to check a
packaging change.

## Turning on signing

The workflow builds and packs on every run, but **signs and uploads nothing**
until the `WINDOWS_SIGNING_ENABLED` repo variable is `true` — no unsigned `.msix`
ever leaves the runner. Asking for a real release while it is unset fails in
`setup`, before any version bump is pushed, rather than producing a Release with
nothing in it.

Set these as repo **variables** (Settings → Secrets and variables → Actions →
Variables). None of them is a secret — authentication is GitHub OIDC, so there is
no stored Azure credential.

| Variable | Value |
|---|---|
| `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` | the federated app registration's IDs |
| `AZURE_SIGNING_ENDPOINT` | region endpoint, e.g. `https://eus.codesigning.azure.net/` |
| `AZURE_SIGNING_ACCOUNT` | Trusted Signing account name |
| `AZURE_SIGNING_PROFILE` | certificate profile name |
| `WINDOWS_SIGNING_ENABLED` | `true` |

On the Azure side, four things — note that this repo needs its **own** federated
credential even if the Trusted Signing account is shared with Janitor, because
the OIDC subject names the repository:

1. A Trusted Signing account and **certificate profile**.
2. An Entra app registration with a **federated credential** whose subject is
   `repo:Kyle-Falconer/newsletter-helper:environment:release`. This is a
   different repo *and* a different owner from Janitor's, so its credential does
   not carry over.
3. The **Trusted Signing Certificate Profile Signer** role granted to that app on
   the signing account.
4. A GitHub environment named **`release`** in this repo — the `windows` job runs
   in it so the OIDC subject is trigger-independent and one federated credential
   covers every release.

### The Publisher string is load-bearing

`Publisher` in `App/MCAANewsletter/msix/AppxManifest.xml` **must exactly equal**
the Subject (`CN=…`) of the certificate profile, or signing rejects the package.
It is currently:

```
CN=Circuit Stitch, O=Circuit Stitch, L=West Sacramento, S=California, C=US
```

Under Trusted Signing the Subject comes from the account's **Identity
Validation**, not from the individual certificate profile — so a new profile
under the existing Circuit Stitch account keeps this string, and the same value
must also appear in `MCAANewsletter.appinstaller`. **Check the exact Subject in
the Azure portal before the first signed run** and correct both files if it
differs by so much as a space.

Consequence worth knowing: Windows shows **"Circuit Stitch"** as the publisher on
the install prompt, even though the app is branded for the art association.
Changing that means a separate Trusted Signing account with its own identity
validation for the association — a multi-day verification process, not a config
change.

## Verifying the first real release

"It builds" is not "it updates". End-to-end auto-update is only provable by
publishing two releases and upgrading on a real Windows machine. On the first
one, confirm:

- Trusted Signing accepts the package — i.e. the **Publisher matches**.
- Installing from the `.appinstaller` URL gives **no sideload-trust prompt**
  (Trusted Signing is CA-trusted, so there should be none).
- Publishing a *higher* version afterwards triggers an **in-place update** on
  next launch.
- The packaged app can read and write `settings.txt` under MSIX's storage
  virtualization.

Note the one-time cost of moving from a loose `.exe` to the MSIX: MSIX
virtualizes per-user writes, so settings saved by an unpackaged build are not
visible to the packaged one. She picks the newsletter folder once more, and then
never again.

## What is not set up

- **No CI workflow.** There is no build-on-push check; `release.yml` is the only
  workflow. The test suite needs the real newsletters, which deliberately do not
  live in this repo, so a hosted runner cannot run it as it stands.
- **No high-DPI tile variants.** Only base-size logos (44/150/50 px) are
  generated from `App/MCAANewsletter/assets/MCAA_min.svg`. Add `.scale-200`
  variants if the Start menu tile looks soft — see that folder's README.
