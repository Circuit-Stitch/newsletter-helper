# Releasing

The app ships as a **signed MSIX** with a companion `.appinstaller`. Windows'
built-in App Installer does the updating: once she has installed from the
`.appinstaller` URL, it checks that URL on every launch and installs a new
version in the background. **Publishing the draft GitHub Release is the entire
release action** — there is nothing to tell her and nothing for her to click.

The packages are served from **public Azure Blob Storage**, not GitHub Releases.
App Installer needs a direct, stable URL served with the right content type, and
a GitHub Release asset is neither:

| | GitHub Release asset | Azure blob |
|---|---|---|
| Request | two 302s, ending at an expiring signed URL | one 200 |
| `Content-Type` | `application/octet-stream` | `application/appinstaller` |
| `Content-Disposition` | `attachment` | none |

A browser handed `octet-stream` with `attachment` saves the file instead of
launching App Installer, which turns a one-click install into a download she has
to find and open. The blob URL is also the one baked into every installed copy,
so it has to keep working regardless.

This repo is public, so the release assets *are* downloadable anonymously and an
install from them does work — the `.appinstaller` file carries the blob URL, so
updates still arrive. The blob link is simply the better one to hand out.

Hosting the bytes off GitHub costs nothing in trust: the anchor is the `.msix`'s
Authenticode signature, which Windows verifies before installing regardless of
where it was downloaded from.

So a release has two stages, and the GitHub Release is still the gate:

```
release.yml   tag or one-click  ->  build, pack, sign  ->  DRAFT Release
                                                              |
                                                     you review and Publish
                                                              |
publish.yml   on: release published  ->  mirror packages to Blob Storage
                                                              |
                                              her next launch picks it up
```

Nothing reaches the blob — and so nothing is offered as an update — until you
click Publish. The GitHub Release keeps the archive copy and the changelog.

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
changed* section of the notes, then **Publish**.

**Publish that draft — do not create a new release.** The packages are attached
to the draft, and GitHub happily allows a second, hand-made release on the same
tag. Publishing that one instead fires `publish.yml` against a release with no
assets, so it uploads nothing and clients stay on the old version. Renaming the
draft before publishing is fine; replacing it is not. Publishing fires `publish.yml`,
which mirrors the packages to Blob Storage — that upload is what actually offers
the update, so the review before Publish is the release gate. Draft releases
never fire the `published` event, so a half-finished release cannot advertise
itself.

A draft also defers git-tag creation until Publish, so a failed build leaves no
dangling tag and the run is safely re-runnable.

`publish.yml` uploads the `.msix` **before** the `.appinstaller`, so there is no
window in which a launching client is told about a version it cannot yet
download. It then fetches both back **anonymously** and fails if either is not
HTTP 200 — the only check that actually proves what clients depend on, since
RBAC, the container's access level and the content type can each be correct
while the blob still is not publicly readable.

**Dry run.** Run the workflow with an **empty** version: it builds and packs the
MSIX but signs nothing, uploads nothing and drafts no Release. Use it to check a
packaging change.

## Turning on signing

The workflow builds and packs on every run, but **signs and uploads nothing**
until the `WINDOWS_SIGNING_ENABLED` repo variable is `true` — no unsigned `.msix`
ever leaves the runner. Asking for a real release while it is unset fails in
`setup`, before any version bump is pushed, rather than producing a Release with
nothing in it.

Set these as **repository** variables (Settings → Secrets and variables → Actions
→ Variables), *not* environment variables on the `release` environment. None of
them is a secret — authentication is GitHub OIDC, so there is no stored Azure
credential.

Repository scope is load-bearing for `WINDOWS_SIGNING_ENABLED`: it is read by the
`setup` job, which deliberately declares no `environment:`. Set it on the
environment instead and `setup` sees an empty string, so every real release fails
with "signing is not enabled" immediately after you enabled it. The five `AZURE_*`
variables are only read inside the `windows` job, which does declare the
environment, so those would work at either scope — but `vars.X` reads both and
looks identical either way, so splitting them across scopes just makes half the
configuration invisible from whichever job you happen to be reading.

| Variable | Value |
|---|---|
| `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` | the federated app registration's IDs |
| `AZURE_SIGNING_ENDPOINT` | the account's region endpoint — copy it from the account's Overview blade rather than typing it, e.g. `https://eus.codesigning.azure.net/` |
| `AZURE_SIGNING_ACCOUNT` | Artifact Signing account name |
| `AZURE_SIGNING_PROFILE` | certificate profile name |
| `WINDOWS_SIGNING_ENABLED` | `true` |
| `AZURE_STORAGE_ACCOUNT` | storage account holding the packages |
| `AZURE_STORAGE_CONTAINER` | container name within it, e.g. `packages` |
| `AZURE_DOWNLOAD_BASE_URL` | public base URL of that container, **no trailing slash**, e.g. `https://<account>.blob.core.windows.net/packages` |

`AZURE_DOWNLOAD_BASE_URL` has to agree with the other two — it is substituted
into the `.appinstaller` at build time and **baked into every installed copy**,
while the upload uses the account and container names. Point them at different
places and the release succeeds, the install works, and updates silently never
arrive. The `release.yml` build fails if it is unset or not `https://`, and
`publish.yml` fetches it back anonymously after uploading, which catches a
mismatch — but only after a release has been published.

> **Note on names.** Microsoft renamed **Trusted Signing → Artifact Signing** in
> January 2026. The portal now lists *Artifact Signing Accounts*, and the roles
> are *Artifact Signing Certificate Profile Signer* and *Artifact Signing
> Identity Verifier*. The GitHub Action moved from
> `azure/trusted-signing-action@v0` to `azure/artifact-signing-action@v2`, and its
> `trusted-signing-account-name` input is deprecated in favour of
> `signing-account-name` — this workflow uses the current names. Older
> write-ups, including Janitor's, still use the old ones.

On the Azure side, four things — note that this repo needs its **own** federated
credential even though the Artifact Signing account is shared with Janitor,
because the OIDC subject names the repository:

1. An Artifact Signing account and **certificate profile**.
2. An Entra app registration with a **federated credential** whose subject is
   the **immutable** form (see below) — not the familiar
   `repo:Circuit-Stitch/newsletter-helper:environment:release`. Same org as
   Janitor, different repo, so Janitor's credential does not cover this one.
3. The **Artifact Signing Certificate Profile Signer** role granted to that app on
   the signing account.
4. A GitHub environment named **`release`** in this repo — the `windows` job runs
   in it so the OIDC subject is trigger-independent and one federated credential
   covers every release. `publish.yml` uses the same environment, so that single
   credential covers the blob upload too.
5. A **storage account + container** for the packages, and the **Storage Blob
   Data Contributor** role on it for the same app registration. See below.

### Blob Storage for the packages

Create a storage account and a container (`packages` is fine). Two settings are
load-bearing:

- On the **storage account**: *Allow Blob anonymous access* must be **Enabled**.
  It defaults to disabled on new accounts, and while it is off, the container's
  own setting cannot take effect.
- On the **container**: anonymous access level **Blob**, not *Container* and not
  *Private*. *Blob* allows reading a blob whose exact name you know, which is all
  App Installer needs. *Container* would additionally let anyone list the
  contents, which buys nothing.

Then grant the app registration **Storage Blob Data Contributor** on the account
or container (Access control (IAM), same flow as the signing role — remember to
type into the members picker). `publish.yml` uploads with `--auth-mode login`
using the OIDC identity, so **no storage account key is ever stored**. The
Owner/Contributor roles do *not* include data-plane access; this data role is
separate and its absence is a 403 at upload time.

Only two blobs are ever written, and they are **overwritten** each release:
`MCAANewsletter.msix` and `MCAANewsletter.appinstaller`. That stability is the
whole point — it is what makes the URL baked into installed copies keep working,
exactly as GitHub's `…/releases/latest/download/` did.

Cost is negligible: two files of a few hundred KB, downloaded by one PC. Egress
is billed, so keep the container's access level at *Blob* rather than publishing
a listing that could be crawled.

### The app registration, click by click

Creating it: **Single tenant** is correct. Leave **Redirect URI blank** — redirect
URIs are for interactive browser sign-in, where a user authenticates and is
redirected back with a code. GitHub Actions OIDC has no browser and no user: the
workflow presents a GitHub-issued JWT straight to the token endpoint. The field
is simply unused here.

Then, on the new registration → **Certificates & secrets → Federated
credentials → Add credential**. Not *Client secrets* — the entire point of OIDC
is that no long-lived credential is stored anywhere.

#### Use the "Other issuer" scenario, not the GitHub one

This repo presents an **immutable subject claim** — GitHub appends numeric owner
and repository IDs, so the subject is:

```
repo:Circuit-Stitch@222346232/newsletter-helper@1317913512:environment:release
```

The portal's *GitHub Actions deploying Azure resources* scenario builds the
subject from the org and repo **names** and cannot produce that form, so a
credential created that way never matches and `azure/login` fails with
`AADSTS700213: No matching federated identity record found`. Use **Other
issuer** and type the subject in:

| Field | Value |
|---|---|
| Issuer | `https://token.actions.githubusercontent.com` |
| Subject identifier | `repo:Circuit-Stitch@222346232/newsletter-helper@1317913512:environment:release` |
| Audience | `api://AzureADTokenExchange` |

[GitHub made this the default for every repository created after 15 July 2026](https://github.blog/changelog/2026-04-23-immutable-subject-claims-for-github-actions-oidc-tokens/);
older repos keep the plain form unless opted in, which is why **Janitor's
credential looks different** — it predates the change. The IDs defend against
name recycling: delete a repo and recreate it under the same name and the ID
differs, so the old credential no longer matches.

If a subject ever needs rebuilding, don't reconstruct it by hand — run the
workflow and copy the `subject claim` line the `azure/login` step prints. That is
the authoritative value.

Entity type still matters conceptually: `:environment:release` is what makes the
subject independent of how the run was triggered, so one credential covers a tag
push, a one-click dispatch, **and** `publish.yml`'s blob upload. A `:ref:` subject
would need one credential per branch or tag. It is why both jobs declare
`environment: release`.

Finally, on the **Artifact Signing account** → Access control (IAM) → Add role
assignment → **Artifact Signing Certificate Profile Signer** → assign to this app
registration. Without it, `azure/login` succeeds and the signing step then fails
with an authorization error, which reads misleadingly like a bad credential.

On the Members tab, **type the app registration's name** — the picker shows only
a couple of default users until you search, and never enumerates service
principals. If nothing matches, paste the **Application (client) ID** instead. If
that also fails, the account here is a directory **guest**, and Entra's default
guest access restrictions can stop the picker enumerating objects that plainly
exist. The CLI assigns by ID and needs no directory search:

```bash
az role assignment create \
  --role "Artifact Signing Certificate Profile Signer" \
  --assignee <APPLICATION_CLIENT_ID> \
  --scope "/subscriptions/<SUB_ID>/resourceGroups/<RG>/providers/Microsoft.CodeSigning/codeSigningAccounts/<ACCOUNT>"
```

The three IDs for the variables table: `AZURE_CLIENT_ID` is the registration's
**Application (client) ID**, `AZURE_TENANT_ID` its **Directory (tenant) ID**, and
`AZURE_SUBSCRIPTION_ID` is the subscription holding the Artifact Signing account.

### So is the repository path

`MCAANewsletter.appinstaller` hardcodes
`https://github.com/Circuit-Stitch/newsletter-helper/releases/latest/download/…`,
and that URL is **baked into every installed copy** — it is where App Installer
looks on each launch. Renaming or moving the repo therefore breaks updates for
everyone already installed, and GitHub's redirect does **not** save you: it
redirects `git push`, but App Installer will not follow a redirect to a package
whose identity it has not already trusted.

The OIDC subject, by contrast, now survives a move: the immutable claim
identifies the repo by numeric ID rather than by path, so renaming the repo or
the org does not invalidate the federated credential.

If the repo ever moves again, updating this file only fixes *future* installs.
Anyone already on the old URL has to reinstall from the new `.appinstaller` by
hand.

### The Publisher string is load-bearing

`Publisher` in `App/MCAANewsletter/msix/AppxManifest.xml` **must exactly equal**
the Subject (`CN=…`) of the certificate profile, or signing rejects the package.
It is currently:

```
CN=Circuit Stitch, O=Circuit Stitch, L=West Sacramento, S=California, C=US
```

Under Artifact Signing the Subject comes from the account's **Identity
Validation**, not from the individual certificate profile — so a new profile
under the existing Circuit Stitch account keeps this string. **Check the exact
Subject in the Azure portal before the first signed run.** If it differs by so
much as a space, **three** files need the same edit:

| File | What to change |
|---|---|
| `App/MCAANewsletter/msix/AppxManifest.xml` | `Publisher=` — signing fails without this |
| `App/MCAANewsletter/msix/MCAANewsletter.appinstaller` | `Publisher=` on `<MainPackage>` |
| `.github/release-body.md` | the "signed by Circuit Stitch" line — cosmetic, and the easy one to miss |

Consequence worth knowing: Windows shows **"Circuit Stitch"** as the publisher on
the install prompt, even though the app is branded for the art association.
Changing that means a separate Artifact Signing account with its own identity
validation for the association — a multi-day verification process, not a config
change.

## Verifying the first real release

"It builds" is not "it updates". End-to-end auto-update is only provable by
publishing two releases and upgrading on a real Windows machine. On the first
one, confirm:

- Artifact Signing accepts the package — i.e. the **Publisher matches**.
- Installing from the `.appinstaller` URL gives **no sideload-trust prompt**
  (Artifact Signing is CA-trusted, so there should be none).
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
